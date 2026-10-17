---
name: messaging-kafka
description: Producción y consumo de mensajes Kafka siguiendo Ports & Adapters. Cubre IKafkaProducerPort, KafkaProducerAdapter (Confluent.Kafka), consumer BackgroundService en EventListener, configuración y anti-patrones. Se activa cuando se menciona Kafka, productor, consumidor, topic, IKafkaProducerPort o Confluent.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "[topic-o-entidad-a-publicar]"
---

# Messaging Kafka Skill — Confluent.Kafka + Hexagonal Architecture

Desacopla la publicación y el consumo de eventos de la lógica de negocio. La capa de Application solo conoce `IKafkaProducerPort` — nunca `Confluent.Kafka` directamente.

---

## 1. Arquitectura

```
Use Case (Application)
    └── IKafkaProducerPort       ← interfaz en Domain/Interfaces
          └── KafkaProducerAdapter   ← implementación en KafkaInfrastructure
                └── IProducer<string, string> (Confluent.Kafka)
                      └── Kafka Broker

EventListener (hosted service)
    └── BackgroundService
          └── IConsumer<string, string>
                └── Kafka Broker
```

Proyectos involucrados:
- `Domain/Interfaces/IKafkaProducerPort.cs` — contrato de publicación
- `KafkaInfrastructure/KafkaProducerAdapter.cs` — implementación del producer
- `KafkaInfrastructure/KafkaSetting.cs` — registro DI
- `EventListener/FromExternal/MicroserviceCallKafkaListener.cs` — consumer
- `EventListener/EventListenerSetting.cs` — registro del hosted service
- `SystemAPI/ApplicationSetting.cs` — Composition Root

---

## 2. NuGet Packages

```xml
<!-- KafkaInfrastructure/KafkaInfrastructure.csproj -->
<PackageReference Include="Confluent.Kafka" Version="2.6.0" />

<!-- EventListener/EventListener.csproj -->
<PackageReference Include="Confluent.Kafka" Version="2.6.0" />
```

---

## 3. `IKafkaProducerPort` — La Interfaz

Ubicación: `Domain/Interfaces/IKafkaProducerPort.cs`

```csharp
namespace Domain.Interfaces;

public interface IKafkaProducerPort
{
    Task<bool> PublishAsync(string topic, string key, string payload, CancellationToken ct = default);
}
```

| Parámetro | Descripción |
|---|---|
| `topic` | Nombre del topic Kafka (ej. `"user-registered"`) |
| `key` | Clave de particionamiento (ej. `userId`, `traceId`) |
| `payload` | Mensaje serializado como JSON |
| `ct` | Token de cancelación para respetar el ciclo de vida |

> La interfaz vive en `Domain` — nunca referencia `Confluent.Kafka`.

---

## 4. Configuración

### `appsettings.json`

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "GroupId": "system-api-group",
    "AutoOffsetReset": "Earliest",
    "EnableAutoCommit": false,
    "SaslUsername": "",
    "SaslPassword": "",
    "SecurityProtocol": "Plaintext"
  }
}
```

### Variables de entorno (Docker / Producción)

```bash
Kafka__BootstrapServers=kafka-broker:9092
Kafka__SaslUsername=my-user
Kafka__SaslPassword=my-password
Kafka__SecurityProtocol=SaslSsl
```

---

## 5. `KafkaSetting.cs` — Registro DI

Ubicación: `KafkaInfrastructure/KafkaSetting.cs`

```csharp
using Confluent.Kafka;
using Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KafkaInfrastructure;

public static class KafkaSetting
{
    public static void AddKafkaInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Kafka");

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = section["BootstrapServers"] ?? "localhost:9092",
            Acks             = Acks.Leader,
        };

        services.AddSingleton<IProducer<string, string>>(
            new ProducerBuilder<string, string>(producerConfig).Build());

        services.AddSingleton<IKafkaProducerPort, KafkaProducerAdapter>();
    }
}
```

---

## 6. `KafkaProducerAdapter.cs` — Implementación

Ubicación: `KafkaInfrastructure/KafkaProducerAdapter.cs`

```csharp
using Confluent.Kafka;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace KafkaInfrastructure;

internal sealed class KafkaProducerAdapter : IKafkaProducerPort
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducerAdapter> _logger;

    public KafkaProducerAdapter(IProducer<string, string> producer, ILogger<KafkaProducerAdapter> logger)
    {
        _producer = producer;
        _logger   = logger;
    }

    public async Task<bool> PublishAsync(string topic, string key, string payload, CancellationToken ct = default)
    {
        try
        {
            var message = new Message<string, string> { Key = key, Value = payload };
            var result  = await _producer.ProduceAsync(topic, message, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Kafka message published. Topic={Topic} Key={Key} Offset={Offset}",
                topic, key, result.Offset);

            return result.Status == PersistenceStatus.Persisted;
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex,
                "Kafka produce failed. Topic={Topic} Key={Key} Error={Error}",
                topic, key, ex.Error.Reason);
            return false;
        }
    }
}
```

---

## 7. Uso en Use Case

```csharp
public class UserRegisterCase : IUserRegisterPort
{
    private readonly IKafkaProducerPort _kafka;
    private readonly ILogger<UserRegisterCase> _logger;

    public UserRegisterCase(IKafkaProducerPort kafka, ILogger<UserRegisterCase> logger)
    {
        _kafka  = kafka;
        _logger = logger;
    }

    public async Task<EasyResult<UserRegisterAdapter>> ExecuteAsync(
        UserRegisterRequestAdapter request,
        TraceIdentifierAdapter header,
        CancellationToken ct = default)
    {
        // ... validaciones y lógica ...

        var payload = JsonSerializer.Serialize(new
        {
            request.Username,
            request.Email,
            header.MessageIdentifier,
            OccurredAt = DateTime.UtcNow
        });

        await _kafka.PublishAsync("user-registered", header.MessageIdentifier, payload, ct);

        return EasyResult<UserRegisterAdapter>.Success(result);
    }
}
```

> Si el publish falla, decide según el contexto de negocio: ¿es un error crítico que debe fallar el Use Case, o es un evento secundario que puede ignorarse?

---

## 8. Consumer BackgroundService

Ubicación: `EventListener/FromExternal/MicroserviceCallKafkaListener.cs`

```csharp
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventListener.FromExternal;

internal sealed class MicroserviceCallKafkaListener : BackgroundService
{
    private readonly ILogger<MicroserviceCallKafkaListener> _logger;
    private readonly ConsumerConfig _config;
    private const string Topic = "microservice-calls";

    public MicroserviceCallKafkaListener(ConsumerConfig config, ILogger<MicroserviceCallKafkaListener> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kafka consumer es síncrono — usar Task.Run para no bloquear el thread del host
        return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private void ConsumeLoop(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(_config).Build();
        consumer.Subscribe(Topic);

        _logger.LogInformation("Kafka consumer started. Topic={Topic}", Topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = consumer.Consume(ct);
                if (result is null) continue;

                _logger.LogInformation(
                    "Kafka message received. Topic={Topic} Key={Key} Offset={Offset}",
                    result.Topic, result.Message.Key, result.Offset);

                // Aquí invocar un Use Case para procesar el mensaje
                // await _processUseCase.ExecuteAsync(result.Message.Value, ct);

                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Kafka consumer stopping gracefully.");
        }
        finally
        {
            consumer.Close();
        }
    }
}
```

---

## 9. Registro del Consumer en EventListenerSetting

```csharp
// EventListener/EventListenerSetting.cs
public static void AddKafkaConsumerServices(this IServiceCollection services, IConfiguration configuration)
{
    var consumerConfig = new ConsumerConfig
    {
        BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
        GroupId          = configuration["Kafka:GroupId"] ?? "system-api-group",
        AutoOffsetReset  = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
    };

    services.AddSingleton(consumerConfig);
    services.AddHostedService<MicroserviceCallKafkaListener>();
}
```

### En `ApplicationSetting.cs`

```csharp
services.AddKafkaInfrastructure(configuration);  // producer
// En Program.cs (donde se registra EventListener):
services.AddKafkaConsumerServices(configuration); // consumer
```

---

## 10. Anti-patrones a Evitar

```csharp
// ❌ Usar IProducer<string, string> directamente en el Use Case (rompe Hexagonal)
private readonly IProducer<string, string> _producer;
await _producer.ProduceAsync("topic", message);

// ✅ Siempre inyectar IKafkaProducerPort
private readonly IKafkaProducerPort _kafka;
await _kafka.PublishAsync("topic", key, payload, ct);

// ❌ Serializar el payload como objeto anónimo sin rastreabilidad
var payload = JsonSerializer.Serialize(new { data = entity });

// ✅ Incluir siempre MessageIdentifier para correlación de trazas
var payload = JsonSerializer.Serialize(new { entity.Id, header.MessageIdentifier, OccurredAt = DateTime.UtcNow });

// ❌ EnableAutoCommit = true (puede perder mensajes si el proceso falla)
EnableAutoCommit = true

// ✅ Siempre commit manual después de procesar exitosamente
consumer.Commit(result);

// ❌ No registrar el producer como Singleton (crea una conexión por request)
services.AddTransient<IProducer<string, string>>(...);

// ✅ Producer debe ser Singleton — el cliente Kafka es thread-safe y costoso de construir
services.AddSingleton<IProducer<string, string>>(...);

// ❌ Bloquear el thread del host con Consume() directamente en ExecuteAsync
protected override void ExecuteAsync(...) { consumer.Consume(ct); }

// ✅ Usar Task.Run para no bloquear el thread del HostedService
return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
```

---

## 11. Checklist antes de hacer PR

- [ ] `IKafkaProducerPort` está en `Domain/Interfaces` (sin referencia a Confluent.Kafka).
- [ ] `KafkaProducerAdapter` está en `KafkaInfrastructure` y es `internal sealed`.
- [ ] El producer está registrado como `Singleton` en `KafkaSetting.cs`.
- [ ] El Use Case inyecta `IKafkaProducerPort`, no `IProducer<string, string>`.
- [ ] El payload incluye `MessageIdentifier` para correlación de trazas.
- [ ] El consumer usa `Task.Run` en `ExecuteAsync` para evitar bloqueo.
- [ ] `EnableAutoCommit = false` y se hace commit manual tras procesar.
- [ ] El consumer cierra con `consumer.Close()` en el bloque `finally`.
- [ ] `BootstrapServers` viene de `IConfiguration` (no hardcodeado).
- [ ] `AddKafkaInfrastructure` está invocado en `ApplicationSetting.cs`.
