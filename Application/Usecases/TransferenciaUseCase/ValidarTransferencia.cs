using Application.Adapters;
using Application.Ports;
using Domain.Entities.Internals;
using Domain.Interfaces;

namespace Application.Usecases.TransferenciaUseCase;

// La capa de aplicación coordina el flujo; la capa de dominio define las reglas y los contratos.
public sealed class ValidarTransferencia(
    IConsultarClienteRepository clientes, IConsultarCuentaRepository cuentas,
    ICoreBancarioGateway core, IAntifraudeService antifraude,
    ISegmentoLimiteRepository limites, IOtpService otp,
    ICamaraCompensacionGateway camara, IAuditoriaRepository auditoria,
    INotificadorCliente notificador, TimeProvider reloj) : IRealizarTransferencia
{
    private readonly SemaphoreSlim _turno = new(1, 1);

    /// <summary>
    /// Procesa una solicitud por turno para evitar débitos duplicados dentro de esta instancia de la aplicación.
    /// </summary>
    public async Task<Transferencia> EjecutarAsync(SolicitudTransferencia s, CancellationToken ct = default)
    {
        await _turno.WaitAsync(ct);
        try { return await ProcesarAsync(s, ct); }
        finally { _turno.Release(); }
    }

    /// <summary>
    /// Valida los datos y las reglas, consulta antifraude y OTP, y coordina el débito y el envío. Reutiliza el resultado de operaciones ya registradas.
    /// </summary>
    private async Task<Transferencia> ProcesarAsync(SolicitudTransferencia s, CancellationToken ct)
    {
        if (s.OperacionId == Guid.Empty || string.IsNullOrWhiteSpace(s.ClienteDni) ||
            s.ClienteDni.Length != 8 || !s.ClienteDni.All(char.IsAsciiDigit) ||
            string.IsNullOrWhiteSpace(s.CuentaOrigen) || string.IsNullOrWhiteSpace(s.CuentaDestino) ||
            string.IsNullOrWhiteSpace(s.BancoDestino) || s.Monto <= 0 || decimal.Round(s.Monto, 2) != s.Monto)
            throw new ArgumentException("Indica un ID de operación, DNI de 8 dígitos, cuentas, banco y monto positivo de hasta dos decimales.");

        var anterior = await auditoria.ObtenerAsync(s.OperacionId, ct);
        if (anterior is not null)
        {
            if (anterior.ClienteDni != s.ClienteDni || anterior.CuentaOrigen != s.CuentaOrigen ||
                anterior.CuentaDestino != s.CuentaDestino || anterior.BancoDestino != s.BancoDestino || anterior.Monto != s.Monto)
                throw new InvalidOperationException("El ID de operación ya se usó con otros datos.");
            // Solo una operación que aún no movió dinero puede volver a evaluarse.
            if (anterior.Estado is not (EstadoTransferencia.RequiereConfirmacion or EstadoTransferencia.NoDisponible))
                return anterior;
        }

        var ahora = reloj.GetUtcNow();
        var fecha = DateOnly.FromDateTime(ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
        var t = new Transferencia(s.OperacionId, s.ClienteDni, s.CuentaOrigen, s.CuentaDestino,
            s.BancoDestino, s.Monto, 0, ahora, EstadoTransferencia.Rechazada, "");
        var cliente = await clientes.ObtenerClienteAsync(s.ClienteDni, ct);
        var cuenta = await cuentas.ObtenerCuentaAsync(s.CuentaOrigen, ct);
        if (cliente is null || cuenta is null || cuenta.ClienteDni != s.ClienteDni)
            return await FinalizarAsync(t with { Mensaje = "Cliente o cuenta de origen inválidos." });
        if (s.CuentaOrigen == s.CuentaDestino && cuenta.Banco == s.BancoDestino)
            return await FinalizarAsync(t with { Mensaje = "Las cuentas de origen y destino deben ser diferentes." });

        t = t with { Comision = CalculadoraDeComision.Calcular(cuenta.Banco, s.BancoDestino) };
        var limite = await limites.ObtenerLimiteAsync(cliente.Segmento, ct);
        var acumulado = await core.ObtenerAcumuladoAsync(cliente.Dni, fecha, ct);
        var error = Reglas.Validar(cuenta, s.Monto, t.Comision, acumulado, limite);
        if (error is not null) return await FinalizarAsync(t with { Mensaje = error });

        NivelRiesgo riesgo;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try { riesgo = await antifraude.EvaluarAsync(t, timeout.Token).WaitAsync(timeout.Token); }
            catch (Exception ex) when (ex is TimeoutException or HttpRequestException ||
                ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                return await FinalizarAsync(t with { Estado = EstadoTransferencia.NoDisponible,
                    Mensaje = "Antifraude no disponible. No se ha debitado dinero." });
            }
        }
        if (riesgo == NivelRiesgo.Alto)
            return await FinalizarAsync(t with { Mensaje = "Operación bloqueada por riesgo alto." });
        if (Reglas.RequiereOtp(s.Monto, riesgo) && !await otp.VerificarAsync(s.ClienteDni, s.OperacionId, s.CodigoOtp, ct))
            return await FinalizarAsync(t with { Estado = EstadoTransferencia.RequiereConfirmacion,
                Mensaje = "Se requiere un OTP válido para confirmar la operación." });

        ct.ThrowIfCancellationRequested();
        // Desde este punto una desconexión HTTP no debe interrumpir el registro de la operación.
        t = t with { Estado = EstadoTransferencia.Pendiente, Mensaje = "Operación pendiente de conciliación." };
        await auditoria.GuardarAsync(t, CancellationToken.None);
        try
        {
            error = await core.DebitarAsync(t, fecha, limite, CancellationToken.None);
            if (error is not null)
                return await FinalizarAsync(t with { Estado = EstadoTransferencia.Rechazada, Mensaje = error });
            if (!string.Equals(cuenta.Banco, s.BancoDestino, StringComparison.OrdinalIgnoreCase))
                await camara.EnviarAsync(t, CancellationToken.None);
        }
        catch (Exception)
        {
            // Agotar el tiempo de espera no prueba que el banco rechazó la transferencia: no reenviar ni devolver saldo a ciegas.
            return await FinalizarAsync(t);
        }
        return await FinalizarAsync(t with { Estado = EstadoTransferencia.Completada, Mensaje = "Transferencia completada." });
    }

    /// <summary>
    /// Guarda el resultado, intenta notificar al cliente y registra si la notificación fue aceptada, sin cambiar el estado financiero ante un fallo de notificación.
    /// </summary>
    private async Task<Transferencia> FinalizarAsync(Transferencia t)
    {
        await auditoria.GuardarAsync(t, CancellationToken.None);
        try
        {
            await notificador.NotificarAsync(t, CancellationToken.None);
            t = t with { Notificada = true };
        }
        catch (Exception)
        {
            // Fallar al notificar no convierte una transferencia completada en rechazada.
        }
        await auditoria.GuardarAsync(t, CancellationToken.None);
        return t;
    }
}
