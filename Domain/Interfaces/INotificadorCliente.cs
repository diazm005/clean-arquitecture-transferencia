using Domain.Entities.Internals;
namespace Domain.Interfaces;
public interface INotificadorCliente
{
    /// <summary>
    /// Solicita la notificación del resultado al cliente a través del canal implementado por el adaptador.
    /// </summary>
    Task NotificarAsync(Transferencia transferencia, CancellationToken ct = default);
}
