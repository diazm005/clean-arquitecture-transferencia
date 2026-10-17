---
name: serilog-otel
description: Guía de observabilidad completa para el microservicio. Cubre logging estructurado con Serilog, trazabilidad distribuida con OpenTelemetry, correlación de logs con TraceIdentifierAdapter, protección de PII y patrones de logging en Controllers y Use Cases. Se activa cuando se menciona logging, trazabilidad, Serilog, OTEL, observabilidad, logs o monitoreo.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "[componente-a-instrumentar]"
---

# Observability Skill — Serilog + OpenTelemetry

Visibilidad total del microservicio mediante logging estructurado y trazabilidad distribuida. Cada solicitud es rastreable desde el controller hasta la infraestructura usando el `TraceIdentifierAdapter`.

---

## 1. Configuración en `Program.cs`

### Serilog (ya configurado)

```csharp
builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration) // lee appsettings.json
    .Enrich.FromLogContext()                        // captura LogContext.PushProperty
    .Enrich.WithEnvironmentName()                   // añade entorno (Development/Production)
    .WriteTo.Console());                            // sink de consola
```

### OpenTelemetry (ya configurado)

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("SystemApi3.0")                                          // fuente personalizada
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("SystemApi3.0"))
        .AddAspNetCoreInstrumentation()  // captura peticiones HTTP entrantes
        .AddHttpClientInstrumentation()  // captura peticiones HTTP salientes (InternalHttpClient)
        .AddConsoleExporter());          // desarrollo — cambiar a OTLP en producción
```

---

## 2. Configuración en `appsettings.json`

El proyecto usa la sección `Logging` de Microsoft (compatible con Serilog vía `ReadFrom.Configuration`):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning",
      "System.Net.Http.HttpClient": "Warning",
      "System.Net.Http.HttpClient.ArifyClient": "Warning"
    }
  }
}
```

Para habilitar logging estructurado en JSON (producción) añadir sección `Serilog` nativa:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/log-.json",
          "rollingInterval": "Day",
          "formatter": "Serilog.Formatting.Json.JsonFormatter, Serilog"
        }
      }
    ]
  }
}
```

---

## 3. Patrón de Logging en Use Cases

Cada Use Case inyecta `ILogger<T>` y emite trazas semánticas al inicio y fin. **Nunca loguear el objeto `request` completo** (riesgo de PII).

```csharp
public class UserRegisterCase : IUserRegisterPort
{
    private readonly ILogger<UserRegisterCase> _logger;

    public UserRegisterCase(ILogger<UserRegisterCase> logger)
    {
        _logger = logger;
    }

    public Task<EasyResult<UserRegisterAdapter>> ExecuteAsync(
        UserRegisterRequestAdapter request,
        TraceIdentifierAdapter header,
        CancellationToken ct = default)
    {
        // ✅ Log de inicio — solo TraceIdentifier, nunca el request completo
        _logger.LogInformation(
            "UserRegisterCase started. MessageId={MessageId} DeviceId={DeviceId}",
            header.MessageIdentifier,
            header.DeviceIdentifier);

        // ... validación y lógica de negocio ...

        // ✅ Log de fin — solo identificadores sin PII
        _logger.LogInformation(
            "UserRegisterCase completed. UserId={UserId} MessageId={MessageId}",
            result.UserId,
            header.MessageIdentifier);

        return Task.FromResult(EasyResult<UserRegisterAdapter>.Success(result));
    }
}
```

---

## 4. Patrón de Logging en Controllers

Los controllers usan `LoggerMapperHelper` para serializar el `TraceIdentifierAdapter` y los errores de validación de forma segura:

```csharp
// ✅ Warning cuando el use case retorna fallo (validación / negocio)
_logger.LogWarning(
    "TraceId=[{Headers}] Validation=[{ValidationErrors}]",
    LoggerMapperHelper.ToString(headers),
    LoggerMapperHelper.ToString(result.ValidationValues.FirstOrDefault()!));

// ✅ Error cuando hay excepción inesperada de infraestructura
var tracer = Regex.Replace(ex.StackTrace ?? string.Empty, @"\sat\s(.*?)\sin\s", string.Empty).Trim();
_logger.LogError(
    "Message=[{Message}] TraceId=[{Headers}] StackTrace={Trace}",
    ex.Message,
    LoggerMapperHelper.ToString(headers),
    tracer);
```

### `LoggerMapperHelper` — serialización segura

Convierte cualquier objeto a string de propiedades primitivas usando reflexión. Filtra `null` y tipos complejos. Se encuentra en `SystemAPI/Helpers/LoggerMapperHelper.cs`:

```csharp
// Salida para TraceIdentifierAdapter:
// "DeviceIdentifier:device-test MessageIdentifier:message-test ChannelIdentifier:channel-test"

LoggerMapperHelper.ToString(headers)   // ✅ seguro — solo tipos primitivos
LoggerMapperHelper.ToString(request)   // ❌ evitar — puede exponer PII
```

---

## 5. Protección de PII en Logs

**Nunca incluir en logs:**

| Campo | Riesgo | Alternativa |
|---|---|---|
| `request.Password` | Credencial | No loguear — usar solo en validación |
| `request.Email` | PII personal | Loguear solo el `UserId` resultante |
| `request.Phone` | PII personal | No loguear |
| Connection strings | Seguridad | Usar variables de entorno |
| JWT tokens | Seguridad | No loguear — solo claims relevantes |

```csharp
// ❌ MAL — expone PII
_logger.LogInformation("Registrando usuario {Email}", request.Email);

// ✅ BIEN — solo identifica la traza
_logger.LogInformation("UserRegisterCase started. MessageId={MessageId}", header.MessageIdentifier);

// ✅ BIEN — loguear el resultado, no el input
_logger.LogInformation("UserRegisterCase completed. UserId={UserId}", result.UserId);
```

---

## 6. Logging Semántico — Reglas del Proyecto

| Regla | Mal ❌ | Bien ✅ |
|---|---|---|
| Usar plantillas, no interpolación | `$"Orden {id}"` | `"Orden {OrderId}", id` |
| Nombre de propiedad descriptivo | `{0}` | `{OrderId}` |
| Nivel correcto según contexto | `LogError` para validaciones | `LogWarning` para fallos esperados |
| No concatenar strings | `"Error: " + ex.Message` | `"Error: {Message}", ex.Message` |
| No loguear objetos completos | `LoggerMapperHelper.ToString(request)` | Loguear solo campos específicos |

```csharp
// Niveles de uso en este proyecto:
_logger.LogInformation(...)  // inicio/fin de use case, pasos del flujo feliz
_logger.LogWarning(...)      // resultado IsSuccess=false (validación, negocio)
_logger.LogError(...)        // excepciones no esperadas en el catch del controller
```

---

## 7. Enriquecimiento de Contexto con `TraceIdentifierAdapter`

Para que todos los logs de una request compartan el `MessageIdentifier` de forma automática:

```csharp
using (Serilog.Context.LogContext.PushProperty("MessageId", header.MessageIdentifier))
using (Serilog.Context.LogContext.PushProperty("DeviceId", header.DeviceIdentifier))
{
    // Todos los logs dentro de este bloque incluirán MessageId y DeviceId
    _logger.LogInformation("Iniciando procesamiento");
    await _repository.SaveAsync(entity, ct);
    _logger.LogInformation("Procesamiento completado");
}
```

> Útil en casos de uso con múltiples llamadas a infraestructura donde repetir los headers en cada log sería verboso.

---

## 8. Trazabilidad Distribuida — Custom Spans

Para medir operaciones de negocio específicas dentro de un Use Case:

```csharp
private static readonly ActivitySource ActivitySource = new("SystemApi3.0");

public Task<EasyResult<MyAdapter>> ExecuteAsync(...)
{
    using var activity = ActivitySource.StartActivity("MyUseCase.ExecuteAsync");
    activity?.SetTag("messageId", header.MessageIdentifier);
    activity?.SetTag("deviceId", header.DeviceIdentifier);

    // ... lógica ...

    activity?.SetStatus(ActivityStatusCode.Ok);
    return Task.FromResult(EasyResult<MyAdapter>.Success(result));
}
```

### Cambiar a OTLP para producción

```csharp
// Program.cs — reemplazar AddConsoleExporter() por:
.AddOtlpExporter(opt =>
{
    opt.Endpoint = new Uri(builder.Configuration["Otel:Endpoint"]!);
})
```

Herramientas compatibles: **Jaeger**, **Zipkin**, **AWS X-Ray**, **Grafana Tempo**, **Application Insights**.

---

## 9. Middleware de Request Logging (Actual)

El proyecto tiene un middleware inline en `Program.cs` que loguea todas las respuestas no-200 y health checks:

```csharp
app.Use(async (context, next) =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    await next.Invoke();
    if (context.Response.StatusCode != 200 || context.Request.Path.StartsWithSegments("/health"))
    {
        logger.LogWarning("{Method} {StatusCode} {Path}",
            context.Request.Method,
            context.Response.StatusCode,
            context.Request.Path);
    }
});
```

> ⚠️ **Nota:** La condición `!= 200 || /health` loguea health checks exitosos (200). Si esto no es deseado, cambiar a `!= 200 && !path.StartsWithSegments("/health")`.

**Alternativa con Serilog nativo** (más completa, incluye duración y status):

```csharp
// Program.cs — añadir antes de UseRouting/UseAuthorization
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "{Method} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (ctx, elapsed, ex) =>
        ex != null || ctx.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : ctx.Response.StatusCode >= 400
                ? LogEventLevel.Warning
                : LogEventLevel.Information;
});
```

---

## 10. Anti-patrones a Evitar

```csharp
// ❌ Interpolación de string (pierde estructura semántica, MA0002 analyzer)
_logger.LogInformation($"Procesando orden {orderId}");

// ✅ Plantilla con propiedad nombrada
_logger.LogInformation("Procesando orden {OrderId}", orderId);

// ❌ Resolver logger DENTRO del lambda (se ejecuta en cada request — ineficiente)
app.Use(async (context, next) =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();  // nueva instancia por request
    await next.Invoke();
});

// ✅ Capturar el logger UNA SOLA VEZ antes del app.Use()
var logger = app.Services.GetRequiredService<ILogger<Program>>();
app.Use(async (context, next) =>
{
    await next.Invoke();
    if (context.Response.StatusCode != 200)
        logger.LogWarning("{Method} {StatusCode} {Path}",
            context.Request.Method, context.Response.StatusCode, context.Request.Path);
});

// ❌ Loguear con LogError para fallos de validación esperados
_logger.LogError("Email inválido");

// ✅ Usar el nivel correcto
_logger.LogWarning("TraceId=[{TraceId}] Validation=[{Error}]", ..., ...);

// ❌ Loguear objeto request completo (puede exponer PII)
_logger.LogInformation("Request: {Request}", JsonSerializer.Serialize(request));

// ✅ Loguear solo el TraceIdentifier
_logger.LogInformation("Started. MessageId={MessageId}", header.MessageIdentifier);
```

---

## 11. Checklist de Observabilidad

Antes de hacer PR, verificar:

- [ ] El Use Case loguea `LogInformation` al inicio con `MessageId` y `DeviceId`.
- [ ] El Use Case loguea `LogInformation` al fin con el identificador del resultado (ej. `UserId`).
- [ ] El Controller loguea `LogWarning` cuando `result.IsSuccess == false`.
- [ ] El Controller loguea `LogError` en el `catch` con `ex.Message` y `StackTrace` sanitizado.
- [ ] Ningún log incluye campos PII (`Password`, `Email`, `Phone`, tokens).
- [ ] Todas las plantillas usan propiedades nombradas `{PropertyName}`, no interpolación `$""`.
- [ ] Los mocks de `ILogger` en tests usan `NullLogger<T>.Instance` (no setup de `LogInformation`).
