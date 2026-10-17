using System.Collections.Concurrent;
using Domain.Entities.Internals;
using Domain.Interfaces;
namespace NotificacionInfrastructure;

public sealed class NotificacionesAdapter : INotificadorCliente
{
    private readonly ConcurrentQueue<Transferencia> _notificaciones = new();
    public IReadOnlyCollection<Transferencia> Notificaciones => _notificaciones.ToArray();
    /// <summary>
    /// Agrega el resultado a la cola de notificaciones en memoria. No envía SMS ni correos reales.
    /// </summary>
    public Task NotificarAsync(Transferencia t, CancellationToken ct = default)
    {
        _notificaciones.Enqueue(t);
        return Task.CompletedTask;
    }
}
