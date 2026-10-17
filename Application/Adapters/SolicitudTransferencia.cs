using System.ComponentModel.DataAnnotations;
namespace Application.Adapters;

public sealed record SolicitudTransferencia(
    Guid OperacionId,
    [Required, RegularExpression(@"\d{8}")] string ClienteDni,
    [Required, StringLength(30)] string CuentaOrigen,
    [Required, StringLength(30)] string CuentaDestino,
    [Required, StringLength(20)] string BancoDestino,
    decimal Monto,
    string? CodigoOtp = null);
