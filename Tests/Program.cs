using Application.Adapters;
using Application.Handlers.Transferencias;
using Application.Internals.Adapters;
using Application.Ports;
using Microsoft.Extensions.Logging.Abstractions;
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

InitiatePaymentExecutionRequest Solicitud(decimal monto = 100, string destino = "EXTERNA") =>
    new(Guid.NewGuid(), "12345678", "001", destino, "BANCO_B", monto);

(CasoPrueba Caso, CoreBancarioAdapter Core, AuditoriaRepository Auditoria) Crear(
    INotificadorCliente? notificador = null, TimeProvider? reloj = null)
{
    var core = new CoreBancarioAdapter();
    var auditoria = new AuditoriaRepository();
    var handler = new RealizarTransferenciaHandler(core, core, core, new AntifraudeAdapter(), new SegmentoLimiteRepository(),
        new OtpSimulado(), new CamaraCompensacionAdapter(), auditoria,
        notificador ?? new NotificacionesAdapter(), reloj ?? TimeProvider.System,
        NullLogger<RealizarTransferenciaHandler>.Instance);
    return (new(handler), core, auditoria);
}

var f = Crear();
var s = Solicitud();
var r = await f.Caso.EjecutarAsync(s);
Check(r.Status == PaymentExecutionStatus.Completed && r.Fee == 5 && r.CustomerNotified, "Interbancaria, comisión y notificación");
Check((await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 99_895m, "Débito incluye comisión");
await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Caso.EjecutarAsync(s)));
Check((await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 99_895m, "Reintentos concurrentes no duplican débitos");
try { await f.Caso.EjecutarAsync(s with { Amount = 200 }); Check(false, "ID reutilizado"); }
catch (InvalidOperationException) { Check(true, "ID reutilizado con otros datos rechazado"); }

foreach (var monto in new[] { 0m, -1m, 1.001m })
{
    try { await f.Caso.EjecutarAsync(Solicitud(monto)); Check(false, "Monto inválido"); }
    catch (ArgumentException) { Check(true, "Monto inválido: " + monto); }
}

f = Crear();
s = Solicitud(10_000);
r = await f.Caso.EjecutarAsync(s);
Check(r.Status == PaymentExecutionStatus.PendingConfirmation, "OTP desde exactamente 10 000");
Check((await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 100_000, "Solicitar OTP no debita");
r = await f.Caso.EjecutarAsync(s with { OtpCode = "incorrecto" });
Check(r.Status == PaymentExecutionStatus.PendingConfirmation, "OTP incorrecto");
r = await f.Caso.EjecutarAsync(s with { OtpCode = "123456" });
Check(r.Status == PaymentExecutionStatus.Completed, "Confirmar con mismo ID y OTP válido");
Check(f.Auditoria.Historial.Count >= 6, "Auditoría conserva transiciones");

f = Crear();
r = await f.Caso.EjecutarAsync(Solicitud(100, "RIESGO_ALTO") with { OtpCode = "123456" });
Check(r.Status == PaymentExecutionStatus.Rejected, "Riesgo alto bloquea incluso con OTP");
r = await f.Caso.EjecutarAsync(Solicitud(100, "RIESGO_MEDIO"));
Check(r.Status == PaymentExecutionStatus.PendingConfirmation, "Riesgo medio exige OTP bajo 10 000");
r = await f.Caso.EjecutarAsync(Solicitud(100, "SIN_SERVICIO"));
Check(r.Status == PaymentExecutionStatus.Unavailable && (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 100_000,
    "Antifraude sin respuesta no debita");
r = await f.Caso.EjecutarAsync(Solicitud() with { SourceAccountNumber = "003" });
Check(r.Status == PaymentExecutionStatus.Rejected, "Cuenta inactiva");
r = await f.Caso.EjecutarAsync(Solicitud() with { CustomerNationalId = "87654321" });
Check(r.Status == PaymentExecutionStatus.Rejected, "Cuenta ajena");
r = await f.Caso.EjecutarAsync(Solicitud(2000) with { CustomerNationalId = "87654321", SourceAccountNumber = "002" });
Check(r.Status == PaymentExecutionStatus.Rejected, "Saldo debe cubrir también la comisión");

f = Crear();
r = await f.Caso.EjecutarAsync(Solicitud(100, "002") with { DestinationBank = "BANCO_A" });
Check(r.Status == PaymentExecutionStatus.Completed && r.Fee == 0 &&
    (await f.Core.ObtenerCuentaAsync("002"))!.Saldo == 2100, "Mismo banco acredita y no cobra comisión");
r = await f.Caso.EjecutarAsync(Solicitud(100, "001") with { DestinationBank = "BANCO_A" });
Check(r.Status == PaymentExecutionStatus.Rejected, "No transferir a la misma cuenta");
r = await f.Caso.EjecutarAsync(Solicitud(100, "NO_EXISTE") with { DestinationBank = "BANCO_A" });
Check(r.Status == PaymentExecutionStatus.Rejected && (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == 99_900,
    "Destino interno inexistente no debita");

f = Crear();
var concurrentes = await Task.WhenAll(Enumerable.Range(0, 6)
    .Select(_ => f.Caso.EjecutarAsync(Solicitud(10_000) with { OtpCode = "123456" })));
Check(concurrentes.Count(x => x.Status == PaymentExecutionStatus.Completed) == 5 &&
    concurrentes.Count(x => x.Status == PaymentExecutionStatus.Rejected) == 1, "Límite acumulado y concurrencia");

var reloj = new RelojPrueba(new DateTimeOffset(2026, 10, 16, 4, 59, 0, TimeSpan.Zero));
f = Crear(reloj: reloj);
await f.Caso.EjecutarAsync(Solicitud(50_000) with { OtpCode = "123456" });
r = await f.Caso.EjecutarAsync(Solicitud(1));
Check(r.Status == PaymentExecutionStatus.Rejected, "Límite exacto alcanzado");
reloj.Ahora = reloj.Ahora.AddMinutes(2);
r = await f.Caso.EjecutarAsync(Solicitud(1));
Check(r.Status == PaymentExecutionStatus.Completed, "Límite se reinicia a medianoche de Lima");

f = Crear();
s = Solicitud(100, "CAMARA_TIMEOUT");
r = await f.Caso.EjecutarAsync(s);
var saldo = (await f.Core.ObtenerCuentaAsync("001"))!.Saldo;
await f.Caso.EjecutarAsync(s);
Check(r.Status == PaymentExecutionStatus.Pending && saldo == 99_895 &&
    (await f.Core.ObtenerCuentaAsync("001"))!.Saldo == saldo, "Resultado incierto queda pendiente sin repetir débito");

f = Crear(new NotificadorFallido());
r = await f.Caso.EjecutarAsync(Solicitud());
Check(r.Status == PaymentExecutionStatus.Completed && !r.CustomerNotified, "Fallo de notificación conserva éxito");
Console.WriteLine($"Completadas: {total} comprobaciones.");

// Adaptador de prueba: devuelve la transferencia y traduce 400 y 409 sin cuerpo a las excepciones que esperan las comprobaciones.
sealed class CasoPrueba(IRealizarTransferencia handler)
{
    static readonly TraceIdentifierAdapter Trace = new() { MessageIdentifier = "prueba", DeviceIdentifier = "prueba", ChannelIdentifier = "prueba" };

    public async Task<InitiatePaymentExecutionAdapter> EjecutarAsync(InitiatePaymentExecutionRequest s)
    {
        var r = await handler.InitiatePaymentExecutionAsync(Trace, s);
        if (r.SuccessValue is not null) return r.SuccessValue;
        throw r.Status == 400 ? new ArgumentException(r.ValidationValues.First().Message)
            : new InvalidOperationException(r.ValidationValues.First().Message);
    }
}

sealed class NotificadorFallido : INotificadorCliente
{
    public Task NotificarAsync(Transferencia t, CancellationToken ct = default) => throw new HttpRequestException();
}

sealed class RelojPrueba(DateTimeOffset ahora) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = ahora;
    public override DateTimeOffset GetUtcNow() => Ahora;
}
