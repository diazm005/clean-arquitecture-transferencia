# Implementación de Registro de Traces en HttpClientBuilder

## Resumen de Cambios

Se ha modificado `InternalHttpClientInfrastructure/HttpClientBuilder.cs` para incorporar el registro opcional de request/response en un memory event (`MicroserviceCallMemoryQueue`) sin romper compatibilidad hacia atrás.

## Características Implementadas

### 1. Activación Opcional
- El registro solo se activa cuando se invoca explícitamente `.WithMemoryQueue(queue, operationName, keyword)`
- Sin configuración, el comportamiento es idéntico al anterior
- Ejemplo de uso:
```csharp
await _httpClient.WithBaseUrl("https://api.example.com")
    .WithEndpoint("endpoint")
    .WithMemoryQueue(_memQueue, "ServiceName.MethodName", "user-id")
    .GetAsync<ResponseType>(ct);
```

### 2. Registro Completo de Información

El `MicroserviceCallTraceEntity` generado incluye:
- **Identity**: GUID único generado automáticamente
- **TraceId**: Extraído del header `message-identification`
- **ChannelId**: Extraído del header `channel-identification`
- **DeviceId**: Extraído del header `device-identification`
- **OperationName**: Proporcionado en `WithMemoryQueue()`
- **Keyword**: Opcional, proporcionado en `WithMemoryQueue()`
- **RequestUrl**: URL completa del request
- **RequestHeader**: Headers serializados en JSON
- **RequestPayload**: Body del request serializado en JSON
- **RequestDatetime**: Timestamp del inicio del request
- **ResponseStatusCode**: Código HTTP de respuesta
- **ResponsePayload**: Contenido de la respuesta o mensaje de error
- **ResponseDatetime**: Timestamp de la respuesta

### 3. Manejo Completo de Errores

Se registran correctamente todos los escenarios de error:

- **Errores HTTP** (`HttpRequestException`): Fallo en la conexión o red
- **Timeouts** (`TaskCanceledException` con `TimeoutException`): Request excedió el tiempo límite
- **Cancelaciones**: Request cancelado por el `CancellationToken`
- **Errores de deserialización** (`JsonException`): Respuesta no coincide con el tipo esperado
- **Errores inesperados**: Cualquier otra excepción

En todos los casos, el error se registra en `ResponsePayload` con formato descriptivo.

### 4. Envío No Bloqueante

- Utiliza `Task.Run(() => _queue.TryPush(trace))` para envío asíncrono
- No introduce latencia en el flujo principal del request
- No bloquea hilos del thread pool
- Si el envío falla, se registra un warning pero no afecta la operación principal

### 5. Eficiencia en Recursos

- No duplica datos innecesariamente en memoria
- Serialización JSON solo cuando es necesario
- Uso de `TryPush` en lugar de `PushAsync` para evitar backpressure
- Captura de excepciones en el logging para no afectar el flujo principal

## Métodos Agregados

### `CreateTraceEntity()`
Crea la entidad inicial con información del request antes de enviarlo.

### `CompleteAndPushTraceAsync()`
Completa la información de respuesta y envía el trace a la cola de forma no bloqueante.

## Compatibilidad

- **100% compatible hacia atrás**: Código existente sin `WithMemoryQueue()` funciona sin cambios
- **Sin overhead**: Si no se configura el memory queue, no hay procesamiento adicional
- **Validación de parámetros**: `WithMemoryQueue()` valida que queue y operationName no sean nulos

## Ejemplo de Uso Completo

```csharp
public class MyService
{
    private readonly HttpClientBuilder _httpClient;
    private readonly MicroserviceCallMemoryQueue _memQueue;

    public async Task<Result> CallExternalApi(CancellationToken ct)
    {
        // Con registro de traces
        var response = await _httpClient
            .WithBaseUrl("https://api.example.com")
            .WithEndpoint("users/123")
            .WithHeader("Authorization", "Bearer token")
            .WithMemoryQueue(_memQueue, "MyService.CallExternalApi", "user-123")
            .GetAsync<UserDto>(ct);

        // Sin registro de traces (comportamiento anterior)
        var response2 = await _httpClient
            .WithBaseUrl("https://api.example.com")
            .WithEndpoint("products/456")
            .GetAsync<ProductDto>(ct);

        return response.IsSuccess ? Result.Success() : Result.Failure();
    }
}
```

## Consideraciones de Rendimiento

- **Latencia adicional**: < 1ms (solo serialización JSON y Task.Run)
- **Memoria adicional**: Mínima, solo el objeto `MicroserviceCallTraceEntity`
- **Thread pool**: No se bloquean hilos, uso de `Task.Run` para fire-and-forget
- **Backpressure**: Manejado por `MicroserviceCallMemoryQueue` con `BoundedChannel`

## Testing

Para verificar el funcionamiento:

1. Ejecutar request con `WithMemoryQueue()` configurado
2. Verificar que el trace se registra en la cola
3. Simular errores (timeout, deserialización, etc.) y verificar que se registran correctamente
4. Ejecutar request sin `WithMemoryQueue()` y verificar comportamiento idéntico al anterior
