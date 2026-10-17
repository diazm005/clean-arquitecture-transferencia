using Domain.Containers.MemoryEvent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace EventListener.FromMemory;

internal class MicroserviceCallMemoryListener : BackgroundService
{
    private readonly MicroserviceCallMemoryQueue _container;
    private readonly ILogger<MicroserviceCallMemoryListener> _logger;

    public MicroserviceCallMemoryListener(MicroserviceCallMemoryQueue container, ILogger<MicroserviceCallMemoryListener> logger)
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
            _logger.LogWarning("New mem event Detected: {}", JsonSerializer.Serialize(myEvent));
        }
    }
}