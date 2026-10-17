# Ejemplo de Uso Optimizado - MicroserviceCallMemoryQueue

## Integración con BackgroundService

### Ejemplo de BackgroundService Optimizado

```csharp
public class MicroserviceCallProcessorService : BackgroundService
{
    private readonly MicroserviceCallMemoryQueue _queue;
    private readonly ILogger<MicroserviceCallProcessorService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public MicroserviceCallProcessorService(
        MicroserviceCallMemoryQueue queue,
        ILogger<MicroserviceCallProcessorService> logger,
        IServiceProvider serviceProvider)
    {
        _queue = queue;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MicroserviceCallProcessor iniciado");

        try
        {
            // Procesamiento en lotes para mejor eficiencia
            await foreach (var batch in _queue.ReadBatchesAsync(batchSize: 100, stoppingToken))
            {
                await ProcessBatchAsync(batch, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("MicroserviceCallProcessor detenido por cancelación");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en MicroserviceCallProcessor");
        }
    }

    private async Task ProcessBatchAsync(
        IReadOnlyList<MicroserviceCallTraceEntity> batch, 
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        
        // Procesar en paralelo con límite de concurrencia
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
        var tasks = batch.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await ProcessSingleItemAsync(item, scope.ServiceProvider, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task ProcessSingleItemAsync(
        MicroserviceCallTraceEntity item,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            // Verificación proactiva de cancelación
            cancellationToken.ThrowIfCancellationRequested();

            // Procesar el elemento
            // ... lógica de procesamiento ...

            _logger.LogDebug("Procesado elemento {ItemId}", item.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Procesamiento cancelado para elemento {ItemId}", item.Id);
            throw; // Re-lanzar para manejo en nivel superior
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando elemento {ItemId}", item.Id);
            // Decidir si re-lanzar o continuar con otros elementos
        }
    }
}
```

### Configuración en Program.cs

```csharp
// Registrar como singleton para compartir entre servicios
builder.Services.AddSingleton(provider =>
{
    // Configuración optimizada para alta carga
    return new MicroserviceCallMemoryQueue(
        capacity: 10000, // Ajustar según memoria disponible
        fullMode: BoundedChannelFullMode.DropOldest, // Para sistemas en tiempo real
        singleReader: true, // Si solo hay un BackgroundService consumiendo
        singleWriter: false // Múltiples productores (controllers, etc.)
    );
});

builder.Services.AddHostedService<MicroserviceCallProcessorService>();
```

### Uso en Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class TraceController : ControllerBase
{
    private readonly MicroserviceCallMemoryQueue _queue;
    private readonly ILogger<TraceController> _logger;

    public TraceController(
        MicroserviceCallMemoryQueue queue,
        ILogger<TraceController> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    [HttpPost("trace")]
    public async Task<IActionResult> AddTrace(
        [FromBody] MicroserviceCallTraceEntity trace,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Intento no bloqueante primero
            if (_queue.TryPush(trace))
            {
                return Ok(new { queued = true, queueSize = _queue.ApproximateCount });
            }

            // Si falla, intentar de forma asíncrona con timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5)); // Timeout de 5 segundos

            var success = await _queue.PushAsync(trace, cts.Token);
            
            if (success)
            {
                return Ok(new { queued = true, queueSize = _queue.ApproximateCount });
            }
            else
            {
                return StatusCode(503, new { error = "Queue is full or closed" });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Request cancelled by client");
            return StatusCode(499, new { message = "Client closed request" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding trace to queue");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("queue-status")]
    public IActionResult GetQueueStatus()
    {
        return Ok(new
        {
            approximateCount = _queue.ApproximateCount,
            capacity = _queue.Capacity,
            canRead = _queue.CanRead,
            canWrite = _queue.CanWrite,
            isCompleted = _queue.IsCompleted
        });
    }
}
```

## Optimizaciones Implementadas

### 1. Uso Eficiente de Memoria
- **Contador long**: Evita overflow en sistemas de alta carga
- **Sealed class**: Mejor performance al evitar virtual calls
- **Campos readonly**: Optimización del compilador
- **Capacidad por defecto aumentada**: Mejor throughput (1000 vs 100)

### 2. Manejo Avanzado de CancellationToken
- **Verificaciones proactivas**: `ThrowIfCancellationRequested()`
- **Propagación correcta**: Re-lanzar `OperationCanceledException`
- **Timeouts configurables**: Evitar bloqueos indefinidos

### 3. Procesamiento en Lotes
- **ReadBatchesAsync**: Procesa múltiples elementos juntos
- **Paralelización controlada**: Usa `SemaphoreSlim` para limitar concurrencia
- **Mejor throughput**: Reduce overhead de context switching

### 4. Configuración Optimizada
- **AllowSynchronousContinuations = false**: Mejor para alta carga
- **DropOldest por defecto**: Mejor para sistemas en tiempo real
- **Parámetros configurables**: Adaptable a diferentes escenarios

### 5. Observabilidad Mejorada
- **Métricas detalladas**: Count, Capacity, CanRead, CanWrite
- **Logging estructurado**: Información útil para debugging
- **Health checks**: Estado del queue disponible via API

### 6. Cleanup Robusto
- **IAsyncDisposable**: Cleanup asíncrono con timeout
- **GC.SuppressFinalize**: Evita finalizer innecesario
- **Exception handling**: Manejo seguro de errores durante dispose

## Consideraciones de Performance

### Memoria
- Cada elemento ocupa ~200-500 bytes dependiendo del contenido
- Con capacidad 10,000: ~2-5 MB de memoria máxima
- Monitorear con métricas de GC

### CPU
- Procesamiento en lotes reduce context switching
- Paralelización aprovecha múltiples cores
- `ConfigureAwait(false)` evita deadlocks

### Red
- Timeouts previenen conexiones colgadas
- Status 499 libera recursos del servidor
- Backpressure evita saturación

## Monitoreo Recomendado

```csharp
// Métricas personalizadas
services.AddSingleton<IMetrics>(provider =>
{
    var queue = provider.GetRequiredService<MicroserviceCallMemoryQueue>();
    
    // Registrar métricas cada 30 segundos
    var timer = new Timer(_ =>
    {
        // Log métricas del queue
        var logger = provider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Queue metrics: Count={Count}, Capacity={Capacity}", 
            queue.ApproximateCount, queue.Capacity);
    }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    
    return new CustomMetrics(timer);
});
```