using Domain.Containers.MemoryEvent;
using EventListener.FromMemory;
using Microsoft.Extensions.DependencyInjection;

namespace EventListener;

public static class EventListenerSetting
{
    public static void AddInMemoryEventListenerServices(this IServiceCollection services)
    {
        // Added Queue in Memory for Api Events
        services.AddSingleton<MicroserviceCallMemoryQueue>(provider => new MicroserviceCallMemoryQueue());
        services.AddSingleton<MicroserviceErrorMemoryQueue>(provider => new MicroserviceErrorMemoryQueue());

        // Hosted Services
        services.AddHostedService<MicroserviceCallMemoryListener>();
        services.AddHostedService<MicroserviceErrorMemoryListener>();

        // Kafka Lister Seting
    }
}
