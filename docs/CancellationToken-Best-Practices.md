# Mejores Prácticas para CancellationToken y Status Code 499

## Implementación Realizada

### 1. Status Code 499 - Client Closed Request
- **Qué es**: El status code 499 indica que el cliente cerró la conexión antes de recibir la respuesta completa del servidor.
- **Por qué es importante**: Permite liberar recursos del servidor cuando el cliente ya no está esperando la respuesta.

### 2. CancellationToken en Controllers
Se agregó `CancellationToken cancellationToken = default` a todos los métodos del controlador:

```csharp
[HttpGet("retrieve")]
public async Task<IActionResult> Register(
    [FromHeader(Name = "x-device-identifier")] string? deviceIdentifier,
    [FromHeader(Name = "x-message-identifier")] string? messageIdentifier,
    [FromHeader(Name = "x-channel-identifier")] string? channelIdentifier,
    CancellationToken cancellationToken = default
)
```

### 3. Manejo de OperationCanceledException
Se implementó el catch específico para cancelaciones:

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    _logger.LogInformation("Request cancelled by client. TraceId=[{Headers}]", LoggerMapperHelper.ToString(headers));
    return StatusCode(499, new { message = "Client closed request" });
}
```

### 4. Verificaciones de Cancelación
Se agregaron verificaciones proactivas:

```csharp
// Verificar si el cliente ya canceló la solicitud
cancellationToken.ThrowIfCancellationRequested();
```

### 5. Propagación a Capas Inferiores
- **Interfaz actualizada**: `IExamplePort` ahora incluye `CancellationToken` en todos sus métodos
- **Implementación actualizada**: `ExampleCase` maneja correctamente las cancelaciones
- **Verificaciones en UseCase**: Se agregaron verificaciones antes de operaciones costosas

### 6. Middleware Global (Opcional)
Se creó `RequestCancellationMiddleware` para manejo global de cancelaciones.

## Beneficios de la Implementación

### 1. Liberación de Recursos
- Los recursos del servidor se liberan inmediatamente cuando el cliente cancela
- Reduce la carga del servidor en escenarios de alta concurrencia
- Mejora la eficiencia general del sistema

### 2. Mejor Observabilidad
- Logs específicos para cancelaciones de cliente
- Diferenciación entre errores del servidor y cancelaciones del cliente
- Métricas más precisas sobre el comportamiento de la API

### 3. Experiencia del Usuario
- Respuestas más rápidas en escenarios de red inestable
- Mejor manejo de timeouts del cliente
- Reducción de requests "zombie"

## Cuándo se Activa

### Escenarios Comunes:
1. **Timeout del cliente**: El cliente tiene un timeout configurado y cancela la request
2. **Navegación del usuario**: El usuario navega a otra página antes de que termine la request
3. **Cierre de aplicación**: La aplicación cliente se cierra mientras hay requests pendientes
4. **Problemas de red**: Conexión inestable que causa desconexiones

### Ejemplo de Activación:
```javascript
// Cliente JavaScript con timeout
const controller = new AbortController();
setTimeout(() => controller.abort(), 5000); // Timeout de 5 segundos

fetch('/api/endpoint', {
    signal: controller.signal
}).catch(err => {
    if (err.name === 'AbortError') {
        console.log('Request cancelled');
    }
});
```

## Configuración del Middleware (Opcional)

Para usar el middleware global, agregar en `Program.cs`:

```csharp
// Después de otros middlewares pero antes de UseRouting
app.UseRequestCancellation();
```

## Consideraciones Adicionales

### 1. Base de Datos
- Asegurar que las operaciones de base de datos también soporten CancellationToken
- Configurar timeouts apropiados en connection strings

### 2. HTTP Clients
- Pasar CancellationToken a HttpClient.SendAsync()
- Configurar timeouts en HttpClient

### 3. Operaciones de I/O
- File operations, network calls, etc. deben usar CancellationToken
- Usar ConfigureAwait(false) para mejor performance

### 4. Monitoreo
- Agregar métricas para requests canceladas (status 499)
- Alertas si hay un incremento inusual de cancelaciones

## Resultado Final

La implementación asegura que:
- ✅ Los recursos se liberan cuando el cliente cancela
- ✅ Se retorna status code 499 apropiadamente
- ✅ Se logea la información necesaria para debugging
- ✅ La cancelación se propaga a través de todas las capas
- ✅ El sistema es más resiliente y eficiente