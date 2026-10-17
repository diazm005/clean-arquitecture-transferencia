using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
using Application.Ports;
using Domain.Entities.Internals;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Handlers.Transferencias;

// La capa de aplicación coordina el flujo; la capa de dominio define las reglas y los contratos.
public sealed class RealizarTransferenciaHandler(
    IConsultarClienteRepository customers, IConsultarCuentaRepository accounts,
    ICoreBancarioGateway core, IAntifraudeService antifraud,
    ISegmentoLimiteRepository limits, IOtpService otp,
    ICamaraCompensacionGateway clearingHouse, IAuditoriaRepository audit,
    INotificadorCliente notifier, TimeProvider clock,
    ILogger<RealizarTransferenciaHandler> logger) : IRealizarTransferencia
{
    private static readonly InitiatePaymentExecutionRequestValidator Validator = new();
    private readonly SemaphoreSlim _turn = new(1, 1);

    /// <summary>
    /// Processes one request at a time to avoid duplicate debits within this application instance.
    /// </summary>
    public async Task<EasyResult<InitiatePaymentExecutionAdapter>> InitiatePaymentExecutionAsync(
        TraceIdentifierAdapter trace, InitiatePaymentExecutionRequest request, CancellationToken ct = default)
    {
        using var _ = logger.BeginTraceScope(trace, request.TransactionReference);
        logger.LogInformation("Solicitud de transferencia recibida.");
        await _turn.WaitAsync(ct);
        try { return await ProcessAsync(request, ct); }
        finally { _turn.Release(); }
    }

    /// <summary>
    /// Orchestrates the flow: validates the request, reuses previous operations, applies business rules, evaluates risk and OTP, and executes the movement.
    /// </summary>
    private async Task<EasyResult<InitiatePaymentExecutionAdapter>> ProcessAsync(InitiatePaymentExecutionRequest request, CancellationToken ct)
    {
        var validation = Validator.Validate(request);
        if (!validation.IsValid)
        {
            logger.LogWarning("Solicitud inválida.");
            return EasyResult<InitiatePaymentExecutionAdapter>.Failure(400, validation.Errors
                .Select(e => new ValidationResultAdapter { Code = "400", Message = e.ErrorMessage, Field = e.PropertyName })
                .ToList());
        }

        var previous = await GetPreviousResultAsync(request, ct);
        if (previous is not null) return previous;

        var now = clock.GetUtcNow();
        var date = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-5)).DateTime);
        var transfer = new Transferencia(request.TransactionReference, request.CustomerNationalId, request.SourceAccountNumber,
            request.DestinationAccountNumber, request.DestinationBank, request.Amount, 0, now, EstadoTransferencia.Rechazada, "");

        var customer = await customers.ObtenerClienteAsync(request.CustomerNationalId, ct);
        var account = await accounts.ObtenerCuentaAsync(request.SourceAccountNumber, ct);
        if (customer is null || account is null || account.ClienteDni != request.CustomerNationalId)
            return await FinalizeAsync(transfer with { Mensaje = "Cliente o cuenta de origen inválidos." });
        if (request.SourceAccountNumber == request.DestinationAccountNumber && account.Banco == request.DestinationBank)
            return await FinalizeAsync(transfer with { Mensaje = "Las cuentas de origen y destino deben ser diferentes." });

        transfer = transfer with { Comision = CalculadoraDeComision.Calcular(account.Banco, request.DestinationBank) };
        var limit = await limits.ObtenerLimiteAsync(customer.Segmento, ct);
        var accumulated = await core.ObtenerAcumuladoAsync(customer.Dni, date, ct);
        var error = Reglas.Validar(account, request.Amount, transfer.Comision, accumulated, limit);
        if (error is not null) return await FinalizeAsync(transfer with { Mensaje = error });

        var risk = await EvaluateRiskAsync(transfer, ct);
        if (risk is null)
            return await FinalizeAsync(transfer with { Estado = EstadoTransferencia.NoDisponible,
                Mensaje = "Antifraude no disponible. No se ha debitado dinero." });
        if (risk == NivelRiesgo.Alto)
            return await FinalizeAsync(transfer with { Mensaje = "Operación bloqueada por riesgo alto." });
        if (Reglas.RequiereOtp(request.Amount, risk.Value) &&
            !await otp.VerificarAsync(request.CustomerNationalId, request.TransactionReference, request.OtpCode, ct))
            return await FinalizeAsync(transfer with { Estado = EstadoTransferencia.RequiereConfirmacion,
                Mensaje = "Se requiere un OTP válido para confirmar la operación." });

        ct.ThrowIfCancellationRequested();
        return await ExecuteMovementAsync(transfer, account, date, limit);
    }

    /// <summary>
    /// Returns the stored result when the operation cannot be re-evaluated, 409 when the reference is reused with other data, or null when it is new or has not moved money yet.
    /// </summary>
    private async Task<EasyResult<InitiatePaymentExecutionAdapter>?> GetPreviousResultAsync(InitiatePaymentExecutionRequest request, CancellationToken ct)
    {
        var previous = await audit.ObtenerAsync(request.TransactionReference, ct);
        if (previous is null) return null;
        if (previous.ClienteDni != request.CustomerNationalId || previous.CuentaOrigen != request.SourceAccountNumber ||
            previous.CuentaDestino != request.DestinationAccountNumber || previous.BancoDestino != request.DestinationBank ||
            previous.Monto != request.Amount)
        {
            logger.LogWarning("ID de operación reutilizado con otros datos.");
            return EasyResult<InitiatePaymentExecutionAdapter>.Failure(409, "409", "El ID de operación ya se usó con otros datos.");
        }
        // Solo una operación que aún no movió dinero puede volver a evaluarse.
        if (previous.Estado is EstadoTransferencia.RequiereConfirmacion or EstadoTransferencia.NoDisponible) return null;
        logger.LogInformation("Operación ya registrada con estado {Status}; se reutiliza el resultado.", previous.Estado);
        return Respond(previous);
    }

    /// <summary>
    /// Queries the antifraud service with a 2 second limit. Returns null when the service is unavailable.
    /// </summary>
    private async Task<NivelRiesgo?> EvaluateRiskAsync(Transferencia transfer, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try { return await antifraud.EvaluarAsync(transfer, timeout.Token).WaitAsync(timeout.Token); }
        catch (Exception ex) when (ex is TimeoutException or HttpRequestException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Antifraude no disponible.");
            return null;
        }
    }

    /// <summary>
    /// Records the operation as pending, debits the core and sends it to the clearing house when the destination bank differs. From here an HTTP disconnect does not interrupt the record.
    /// </summary>
    private async Task<EasyResult<InitiatePaymentExecutionAdapter>> ExecuteMovementAsync(
        Transferencia transfer, Cuenta account, DateOnly date, decimal limit)
    {
        transfer = transfer with { Estado = EstadoTransferencia.Pendiente, Mensaje = "Operación pendiente de conciliación." };
        await audit.GuardarAsync(transfer, CancellationToken.None);
        try
        {
            var error = await core.DebitarAsync(transfer, date, limit, CancellationToken.None);
            if (error is not null)
                return await FinalizeAsync(transfer with { Estado = EstadoTransferencia.Rechazada, Mensaje = error });
            if (!string.Equals(account.Banco, transfer.BancoDestino, StringComparison.OrdinalIgnoreCase))
                await clearingHouse.EnviarAsync(transfer, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Agotar el tiempo de espera no prueba que el banco rechazó la transferencia: no reenviar ni devolver saldo a ciegas.
            logger.LogError(ex, "Resultado incierto al debitar o enviar a la cámara; la operación queda pendiente.");
            return await FinalizeAsync(transfer);
        }
        return await FinalizeAsync(transfer with { Estado = EstadoTransferencia.Completada, Mensaje = "Transferencia completada." });
    }

    /// <summary>
    /// Stores the result, tries to notify the customer and records whether the notification was accepted, without changing the financial state when notification fails.
    /// </summary>
    private async Task<EasyResult<InitiatePaymentExecutionAdapter>> FinalizeAsync(Transferencia transfer)
    {
        await audit.GuardarAsync(transfer, CancellationToken.None);
        try
        {
            await notifier.NotificarAsync(transfer, CancellationToken.None);
            transfer = transfer with { Notificada = true };
        }
        catch (Exception ex)
        {
            // Fallar al notificar no convierte una transferencia completada en rechazada.
            logger.LogWarning(ex, "No se pudo notificar al cliente.");
        }
        await audit.GuardarAsync(transfer, CancellationToken.None);
        logger.LogInformation("Transferencia finalizada con estado {Status}.", transfer.Estado);
        return Respond(transfer);
    }

    /// <summary>
    /// Translates the operation state to the HTTP status of the result, keeping the payment execution as the body.
    /// </summary>
    private static EasyResult<InitiatePaymentExecutionAdapter> Respond(Transferencia transfer) =>
        EasyResult<InitiatePaymentExecutionAdapter>.Success(transfer.ToAdapter<InitiatePaymentExecutionAdapter>(), transfer.Estado switch
        {
            EstadoTransferencia.Completada => 200,
            EstadoTransferencia.Pendiente => 202,
            EstadoTransferencia.NoDisponible => 503,
            EstadoTransferencia.RequiereConfirmacion => 409,
            _ => 422
        });
}
