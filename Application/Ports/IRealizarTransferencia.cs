using Application.Adapters;
using Domain.Entities.Internals;
namespace Application.Ports;
public interface IRealizarTransferencia
{
    /// <summary>
    /// Procesa la solicitud de transferencia y devuelve el estado de la operación junto con su motivo.
    /// </summary>
    Task<Transferencia> EjecutarAsync(SolicitudTransferencia solicitud, CancellationToken ct = default);
}
