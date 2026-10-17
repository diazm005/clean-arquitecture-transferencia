using Domain.Containers.MemoryEvent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventListener.FromMemory;

internal class MicroserviceErrorMemoryListener : BackgroundService
{
    private readonly MicroserviceErrorMemoryQueue _container;
    private readonly ILogger<MicroserviceErrorMemoryListener> _logger;

    public MicroserviceErrorMemoryListener(MicroserviceErrorMemoryQueue container, ILogger<MicroserviceErrorMemoryListener> logger)
    {
        _container = container;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var myEvent in _container.ReadAllAsync(stoppingToken))
        {
            // Aqui debe llamar a un caso de uso para procesar el evento
            // ***** public Task SaveEventAsync(MicroserviceApiEventEntity microserviceEvent) ****
            _logger.LogWarning("Procesando evento: {EventId}", myEvent.TraceId);
        }
    }
}
