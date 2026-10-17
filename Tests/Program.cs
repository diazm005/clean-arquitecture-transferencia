using Application.Adapters;
using Application.Usecases.TransferenciaUseCase;
using AntifraudeInfrastructure;
using AuditoriaInfrastructure;
using CamaraCompensatorioInfrastructure;
using CoreBancarioInfrastructure;
using Domain.Entities.Internals;
using Domain.Interfaces;
using NotificacionInfrastructure;

var total = 0;
void Check(bool ok, string nombre)
{
    if (!ok) throw new Exception("FALLÓ: " + nombre);
    Console.WriteLine("OK: " + nombre);
    total++;
}

SolicitudTransferencia Solicitud(decimal monto = 100, string destino = "EXTERNA") =>
    new(Guid.NewGuid(), "12345678", "001", destino, "BANCO_B", monto);

(ValidarTransferencia Caso, CoreBancarioAdapter Core, AuditoriaRepository Auditoria) Crear(
    INotificadorCliente? notificador = null, TimeProvider? reloj = null)
{
    var core = new CoreBancarioAdapter();
    var auditoria = new AuditoriaRepository();
    return (new(core, core, core, new AntifraudeAdapter(), new SegmentoLimiteRepository(),
        new OtpSimulado(), new CamaraCompensacionAdapter(), auditoria,
        notificador ?? new NotificacionesAdapter(), reloj ?? TimeProvider.System), core, auditoria);
}

var f = Crear();
var s = Solicitud();
var r = await f.Caso.EjecutarAsync(s);
Check(r.Estado == EstadoTransferencia.Completada && r.Comision == 5 && r.Notificada, "Interbancaria, comisión y notificación");
Check((await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 99_895m, "Débito incluye comisión");
await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Caso.EjecutarAsync(s)));
Check((await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 99_895m, "Reintentos concurrentes no duplican débitos");
try { await f.Caso.EjecutarAsync(s with { Monto = 200 }); Check(false, "ID reutilizado"); }
catch (InvalidOperationException) { Check(true, "ID reutilizado con otros datos rechazado"); }

foreach (var monto in new[] { 0m, -1m, 1.001m })
{
    try { await f.Caso.EjecutarAsync(Solicitud(monto)); Check(false, "Monto inválido"); }
    catch (ArgumentException) { Check(true, "Monto inválido: " + monto); }
}

f = Crear();
s = Solicitud(10_000);
r = await f.Caso.EjecutarAsync(s);
Check(r.Estado == EstadoTransferencia.RequiereConfirmacion, "OTP desde exactamente 10 000");
Check((await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 100_000, "Solicitar OTP no debita");
r = await f.Caso.EjecutarAsync(s with { CodigoOtp = "incorrecto" });
Check(r.Estado == EstadoTransferencia.RequiereConfirmacion, "OTP incorrecto");
r = await f.Caso.EjecutarAsync(s with { CodigoOtp = "123456" });
Check(r.Estado == EstadoTransferencia.Completada, "Confirmar con mismo ID y OTP válido");
Check(f.Auditoria.Historial.Count >= 6, "Auditoría conserva transiciones");

f = Crear();
r = await f.Caso.EjecutarAsync(Solicitud(100, "RIESGO_ALTO") with { CodigoOtp = "123456" });
Check(r.Estado == EstadoTransferencia.Rechazada, "Riesgo alto bloquea incluso con OTP");
r = await f.Caso.EjecutarAsync(Solicitud(100, "RIESGO_MEDIO"));
Check(r.Estado == EstadoTransferencia.RequiereConfirmacion, "Riesgo medio exige OTP bajo 10 000");
r = await f.Caso.EjecutarAsync(Solicitud(100, "SIN_SERVICIO"));
Check(r.Estado == EstadoTransferencia.NoDisponible && (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 100_000,
    "Antifraude sin respuesta no debita");
r = await f.Caso.EjecutarAsync(Solicitud() with { CuentaOrigen = "003" });
Check(r.Estado == EstadoTransferencia.Rechazada, "Cuenta inactiva");
r = await f.Caso.EjecutarAsync(Solicitud() with { ClienteDni = "87654321" });
Check(r.Estado == EstadoTransferencia.Rechazada, "Cuenta ajena");
r = await f.Caso.EjecutarAsync(Solicitud(2000) with { ClienteDni = "87654321", CuentaOrigen = "002" });
Check(r.Estado == EstadoTransferencia.Rechazada, "Saldo debe cubrir también la comisión");

f = Crear();
r = await f.Caso.EjecutarAsync(Solicitud(100, "002") with { BancoDestino = "BANCO_A" });
Check(r.Estado == EstadoTransferencia.Completada && r.Comision == 0 &&
    (await f.Core.ObtenerCuentaAsync("002"))!.Saldo == 2100, "Mismo banco acredita y no cobra comisión");
r = await f.Caso.EjecutarAsync(Solicitud(100, "001") with { BancoDestino = "BANCO_A" });
Check(r.Estado == EstadoTransferencia.Rechazada, "No transferir a la misma cuenta");
r = await f.Caso.EjecutarAsync(Solicitud(100, "NO_EXISTE") with { BancoDestino = "BANCO_A" });
Check(r.Estado == EstadoTransferencia.Rechazada && (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 99_900,
    "Destino interno inexistente no debita");

f = Crear();
var concurrentes = await Task.WhenAll(Enumerable.Range(0, 6)
    .Select(_ => f.Caso.EjecutarAsync(Solicitud(10_000) with { CodigoOtp = "123456" })));
Check(concurrentes.Count(x => x.Estado == EstadoTransferencia.Completada) == 5 &&
    concurrentes.Count(x => x.Estado == EstadoTransferencia.Rechazada) == 1, "Límite acumulado y concurrencia");

var reloj = new RelojPrueba(new DateTimeOffset(2026, 10, 16, 4, 59, 0, TimeSpan.Zero));
f = Crear(reloj: reloj);
await f.Caso.EjecutarAsync(Solicitud(50_000) with { CodigoOtp = "123456" });
r = await f.Caso.EjecutarAsync(Solicitud(1));
Check(r.Estado == EstadoTransferencia.Rechazada, "Límite exacto alcanzado");
reloj.Ahora = reloj.Ahora.AddMinutes(2);
r = await f.Caso.EjecutarAsync(Solicitud(1));
Check(r.Estado == EstadoTransferencia.Completada, "Límite se reinicia a medianoche de Lima");

f = Crear();
s = Solicitud(100, "CAMARA_TIMEOUT");
r = await f.Caso.EjecutarAsync(s);
var saldo = (await f.Core.ObtenerCuentaAsync("001"))!.Saldo;
await f.Caso.EjecutarAsync(s);
Check(r.Estado == EstadoTransferencia.Pendiente && saldo == 99_895 &&
    (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == saldo, "Resultado incierto queda pendiente sin repetir débito");

f = Crear(new NotificadorFallido());
r = await f.Caso.EjecutarAsync(Solicitud());
Check(r.Estado == EstadoTransferencia.Completada && !r.Notificada, "Fallo de notificación conserva éxito");
Console.WriteLine($"Completadas: {total} comprobaciones.");

sealed class NotificadorFallido : INotificadorCliente
{
    public Task NotificarAsync(Transferencia t, CancellationToken ct = default) => throw new HttpRequestException();
}

sealed class RelojPrueba(DateTimeOffset ahora) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = ahora;
    public override DateTimeOffset GetUtcNow() => Ahora;
}
