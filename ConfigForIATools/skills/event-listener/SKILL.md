---
name: event-listener
description: Patrón de colas en memoria con System.Threading.Channels y BackgroundService. Cubre XxxMemoryQueue (Domain), XxxMemoryListener (EventListener), capacidad máxima, backpressure y registro DI. Se activa cuando se menciona MemoryQueue, Channel, BackgroundService, EventListener, cola en memoria, MicroserviceCallMemoryQueue o evento en memoria.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "[nombre-del-evento-o-entidad]"
---

# Event Listener Skill — System.Threading.Channels + BackgroundService

Desacopla la escritura de eventos del procesamiento. El request HTTP publica en la cola y retorna de inmediato. El `BackgroundService` consume la cola en segundo plano sin bloquear el thread del request.

---

## 1. Arquitectura

```
Controller / Use Case / Adapter
    └── XxxMemoryQueue.PushAsync()    ← fire-and-forget, non-blocking
          └── Channel<XxxEntity>      ← System.Threading.Channels (Domain)
                └── XxxMemoryListener ← BackgroundService (EventListener)
                      └── Use Case / Repository para persistir el evento
```

Proyectos involucrados:
- `Domain/Entities/Internals/XxxEntity.cs` — entidad del evento
- `Domain/Containers/MemoryEvent/XxxMemoryQueue.cs` — cola genérica con Channel
- `EventListener/FromMemory/XxxMemoryListener.cs` — BackgroundService consumidor
- `EventListener/EventListenerSetting.cs` — registro DI de colas y listeners

---

## 2. Colas Existentes en el Proyecto

| Queue | Entity | Listener | Propósito |
|---|---|---|---|
| `MicroserviceCallMemoryQueue` | `MicroserviceCallTraceEntity` | `MicroserviceCallMemoryListener` | Traza de llamadas HTTP salientes |
| `MicroserviceErrorMemoryQueue` | `MicroserviceErrorTraceEntity` | `MicroserviceErrorMemoryListener` | Registro de errores del microservicio |

> Seguir el mismo patrón para cualquier nuevo tipo de evento.

---

## 3. Entidad del Evento

Ubicación: `Domain/Entities/Internals/XxxTraceEntity.cs`

```csharp
namespace Domain.Entities.Internals;

public sealed class UserActionTraceEntity
{
    public required string TraceId     { get; set; }
    public required string UserId      { get; set; }
    public required string Action      { get; set; }   // "login", "register", "update"
    public required DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? Metadata            { get; set; }   // JSON adicional si se necesita
}
```

> La entidad vive en `Domain` — no tiene dependencias externas.

---

## 4. `XxxMemoryQueue` — La Cola

Ubicación: `Domain/Containers/MemoryEvent/UserActionMemoryQueue.cs`

```csharp
using Domain.Entities.Internals;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Domain.Containers.MemoryEvent;

public sealed class UserActionMemoryQueue : IDisposable
{
    private readonly Channel<UserActionTraceEntity> _channel;
    private int _length;
    private bool _disposed;
    private const int Capacity = 500; // Límite fijo para no saturar memoria

    public UserActionMemoryQueue(BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
    {
        var options = new BoundedChannelOptions(Capacity)
        {
            FullMode     = fullMode,   // Wait | DropWrite | DropOldest | DropNewest
            SingleReader = false,      // múltiples consumidores permitidos
            SingleWriter = false       // múltiples productores permitidos
        };

        _channel = Channel.CreateBounded<UserActionTraceEntity>(options);
    }

    // ---- Producción ----

    public async ValueTask<bool> PushAsync(UserActionTraceEntity item, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);

        while (await _channel.Writer.WaitToWriteAsync(ct).ConfigureAwait(false))
        {
            if (_channel.Writer.TryWrite(item))
            {
                Interlocked.Increment(ref _length);
                return true;
            }
        }

        return false;
    }

    public bool TryPush(UserActionTraceEntity item)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);

        if (_channel.Writer.TryWrite(item))
        {
            Interlocked.Increment(ref _length);
            return true;
        }

        return false;
    }

    // ---- Consumo continuo (BackgroundService) ----

    public async IAsyncEnumerable<UserActionTraceEntity> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                Interlocked.Decrement(ref _length);
                yield return item;
            }
        }
    }

    // ---- Estado / métricas ----

    public int ApproxLength => Interlocked.CompareExchange(ref _length, 0, 0);
    public bool IsCompleted  => _channel.Reader.Completion.IsCompleted;

    public void Complete() => _channel.Writer.TryComplete();

    public void Dispose()
    {
        if (!_disposed)
        {
            Complete();
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UserActionMemoryQueue));
    }
}
```

### Modos de `BoundedChannelFullMode`

| Modo | Comportamiento cuando la cola está llena |
|---|---|
| `Wait` (default) | El productor espera hasta que haya espacio — backpressure real |
| `DropWrite` | Descarta el mensaje nuevo — nunca bloquea el productor |
| `DropOldest` | Descarta el mensaje más antiguo para hacer espacio |
| `DropNewest` | Descarta el mensaje más reciente al intentar escribir |

> Para trazabilidad usar `Wait`. Para eventos de baja prioridad donde no importa perder alguno, usar `DropOldest`.

---

## 5. `XxxMemoryListener` — BackgroundService Consumidor

Ubicación: `EventListener/FromMemory/UserActionMemoryListener.cs`

```csharp
using Domain.Containers.MemoryEvent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventListener.FromMemory;

internal sealed class UserActionMemoryListener : BackgroundService
{
    private readonly UserActionMemoryQueue _queue;
    private readonly ILogger<UserActionMemoryListener> _logger;

    // Si necesitas persistir, inyecta el Use Case aquí (IServiceScopeFactory para Scoped)
    private readonly IServiceScopeFactory _scopeFactory;

    public UserActionMemoryListener(
        UserActionMemoryQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<UserActionMemoryListener> logger)
    {
        _queue        = queue;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UserActionMemoryListener started.");

        await foreach (var evt in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(evt, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Loguear pero NO re-throw — un error en un evento no debe detener el listener
                _logger.LogError(ex,
                    "Failed to process UserAction event. TraceId={TraceId} Action={Action}",
                    evt.TraceId, evt.Action);
            }
        }

        _logger.LogInformation("UserActionMemoryListener stopped.");
    }

    private async Task ProcessAsync(Domain.Entities.Internals.UserActionTraceEntity evt, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing UserAction. TraceId={TraceId} UserId={UserId} Action={Action}",
            evt.TraceId, evt.UserId, evt.Action);

        // Usar IServiceScopeFactory para resolver servicios Scoped (ej. repositorios)
        await using var scope = _scopeFactory.CreateAsyncScope();
        // var repository = scope.ServiceProvider.GetRequiredService<IUserActionRepository>();
        // await repository.SaveAsync(evt, ct);
    }
}
```

> **Importante:** Si el Use Case o repositorio es `Scoped` (transient/scoped), no lo inyectes directo en el constructor del BackgroundService (que es `Singleton`). Usa `IServiceScopeFactory`.

---

## 6. Producir Eventos desde un Adapter

Así es como `FakeApiQueryInfra` publica en `MicroserviceCallMemoryQueue` — mismo patrón para nuevas colas:

```csharp
public class UserRegisterSqlAdapter : IUserRegisterRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly UserActionMemoryQueue _queue;

    public UserRegisterSqlAdapter(IDbConnectionFactory factory, UserActionMemoryQueue queue)
    {
        _factory = factory;
        _queue   = queue;
    }

    public async Task<int> SaveAsync(UserEntity user, CancellationToken ct = default)
    {
        // ... lógica de persistencia ...

        // Publicar evento en la cola (fire-and-forget, no bloquea)
        _queue.TryPush(new UserActionTraceEntity
        {
            TraceId    = Guid.NewGuid().ToString(),
            UserId     = user.Id.ToString(),
            Action     = "register",
            OccurredAt = DateTime.UtcNow
        });

        return newId;
    }
}
```

> Usar `TryPush` (síncrono) si no quieres await en el adapter. Usar `PushAsync` si necesitas backpressure garantizado.

---

## 7. Registro DI en `EventListenerSetting`

```csharp
// EventListener/EventListenerSetting.cs
public static class EventListenerSetting
{
    public static void AddInMemoryEventListenerServices(this IServiceCollection services)
    {
        // Colas existentes
        services.AddSingleton<MicroserviceCallMemoryQueue>(_ => new MicroserviceCallMemoryQueue());
        services.AddSingleton<MicroserviceErrorMemoryQueue>(_ => new MicroserviceErrorMemoryQueue());

        // Nueva cola
        services.AddSingleton<UserActionMemoryQueue>(_ => new UserActionMemoryQueue());

        // Listeners existentes
        services.AddHostedService<MicroserviceCallMemoryListener>();
        services.AddHostedService<MicroserviceErrorMemoryListener>();

        // Nuevo listener
        services.AddHostedService<UserActionMemoryListener>();
    }
}
```

> Las colas se registran como `Singleton` — existe una sola instancia compartida entre todos los productores y el listener.

---

## 8. Métricas / Observabilidad

Exponer `ApproxLength` en el health check para detectar saturación:

```csharp
// En un HealthCheck personalizado
public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
{
    var length = _queue.ApproxLength;

    return length < 400
        ? Task.FromResult(HealthCheckResult.Healthy($"Queue length: {length}"))
        : Task.FromResult(HealthCheckResult.Degraded($"Queue near capacity: {length}/500"));
}
```

---

## 9. Anti-patrones a Evitar

```csharp
// ❌ Cola sin límite (unbounded) — puede agotar la memoria del proceso
_channel = Channel.CreateUnbounded<XxxEntity>();

// ✅ Siempre usar BoundedChannel con capacidad fija
_channel = Channel.CreateBounded<XxxEntity>(new BoundedChannelOptions(500) { ... });

// ❌ Re-throw en el loop del listener — detiene el procesamiento de todos los eventos
await foreach (var evt in _queue.ReadAllAsync(ct))
    await ProcessAsync(evt, ct); // si lanza, el loop termina

// ✅ Capturar excepción por evento y continuar
try { await ProcessAsync(evt, ct); }
catch (Exception ex) { _logger.LogError(ex, "..."); }

// ❌ Inyectar servicio Scoped directamente en BackgroundService (Singleton)
private readonly IProductRepository _repo; // registrado como Transient/Scoped

// ✅ Usar IServiceScopeFactory para resolver servicios Scoped por evento
await using var scope = _scopeFactory.CreateAsyncScope();
var repo = scope.ServiceProvider.GetRequiredService<IProductRepository>();

// ❌ Registrar la cola como Transient (cada producer tiene su propia cola, el listener ve otra)
services.AddTransient<UserActionMemoryQueue>(...);

// ✅ Singleton — una sola cola compartida
services.AddSingleton<UserActionMemoryQueue>(_ => new UserActionMemoryQueue());

// ❌ Usar await en PushAsync sin CancellationToken cuando el host está cerrando
await _queue.PushAsync(event); // puede quedar colgado si la cola está llena y el host cierra

// ✅ Usar TryPush (no bloquea) o pasar el CancellationToken del request
_queue.TryPush(event);                      // fire-and-forget
await _queue.PushAsync(event, ct);          // con backpressure y cancelación
```

---

## 10. Checklist antes de hacer PR

- [ ] La entidad del evento está en `Domain/Entities/Internals/` sin dependencias externas.
- [ ] La cola usa `BoundedChannel` con capacidad máxima definida (≤ 500).
- [ ] La cola está registrada como `Singleton` en `EventListenerSetting`.
- [ ] El listener extiende `BackgroundService` y está en `EventListener/FromMemory/`.
- [ ] El loop del listener captura excepciones por evento (no re-throw que detenga el loop).
- [ ] Si el listener usa servicios `Scoped`, los resuelve con `IServiceScopeFactory`.
- [ ] `AddHostedService<XxxMemoryListener>()` está en `EventListenerSetting`.
- [ ] El producer usa `TryPush` (síncrono) o `PushAsync` con `CancellationToken`.
- [ ] Se loguea `TraceId` en el listener para correlación con el request original.
- [ ] No se loguea PII en el listener (solo IDs y acciones, no datos personales).
