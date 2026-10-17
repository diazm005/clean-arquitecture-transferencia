---
name: result-pattern
description: Patrón de resultado estandarizado para Use Cases y Controllers usando EasyResult<T>, FluentValidationExecutor, EasyResponseHelper y EasyBianResponseHelper. Se activa cuando se menciona EasyResult, Result Pattern, ValidationResultAdapter, FluentValidation, respuestas de error, códigos de estado HTTP o manejo de resultados.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "[componente-a-implementar]"
---

# Result Pattern Skill — EasyResult + FluentValidation

Elimina excepciones del flujo de negocio. Cada Use Case retorna `EasyResult<T>`, los Controllers leen `IsSuccess` y delegan el formato al helper correspondiente.

---

## 1. `EasyResult<T>` — El Contenedor

Ubicación: `Application/Internals/Executors/EasyResult.cs`

### Propiedades

| Propiedad | Tipo | Descripción |
|---|---|---|
| `IsSuccess` | `bool` | `true` = operación completada |
| `Status` | `int` | Código HTTP sugerido (200, 204, 400, 422…) |
| `SuccessValue` | `T?` | Payload de respuesta en caso de éxito |
| `ValidationValues` | `IReadOnlyCollection<ValidationResultAdapter>` | Errores de validación en caso de fallo |

### Métodos de Creación

```csharp
// Éxito con datos → Status 200
EasyResult<MyAdapter>.Success(myAdapter)

// Sin contenido → Status 204
EasyResult<MyAdapter>.Empty()

// Fallo de validación de headers → Status 422
EasyResult<MyAdapter>.Failure(422, headerErrors)

// Fallo de validación de request → Status 400
EasyResult<MyAdapter>.Failure(400, requestErrors)
```

---

## 2. `ValidationResultAdapter` — Estructura de Error

Ubicación: `Application/Internals/Adapters/ValidationResultAdapter.cs`

```csharp
public sealed class ValidationResultAdapter
{
    public required string Code    { set; get; }  // Código del error (ej. "21002")
    public required string Message { set; get; }  // Mensaje legible
    public string?         Field   { set; get; }  // Campo afectado (opcional)
}
```

### Construcción Correcta (required properties)

```csharp
// ✅ CORRECTO — object initializer con required properties
new ValidationResultAdapter { Code = "21007", Message = "Invalid email format", Field = "Email" }

// ❌ INCORRECTO — no existe constructor posicional
new ValidationResultAdapter("21007", "Invalid email")
```

---

## 3. `MessageCatalog` — Códigos de Error del Dominio

Ubicación: `Domain/Catalogs/MessageCatalog.cs`

```csharp
MessageCatalog.GetErrorByCode(21002) // "Cannot be empty"
```

| Código | Mensaje |
|---|---|
| `21001` | Cannot be null |
| `21002` | Cannot be empty |
| `21003` | It is out of the allowed bounds |
| `21004` | Allowed minimum length |
| `21005` | Allowed maximum length |
| `21006` | Invalid type |
| `21007` | Invalid value |
| `21008` | Invalid name |
| `21009` | Invalid |
| `21010` | Use alphanumeric characters (a-z and 0-9) |
| `21011` | Use allowed characters (a-z) |
| `21012` | Use numeric characters |
| `21096` | Does not authorize data processing |
| `21097` | Incorrect parameters |
| `21098` | General internal error |

---

## 4. `FluentValidationExecutor` — Ejecución Sincrónica

Ubicación: `Application/Internals/Executors/FluentValidationExecutor.cs`

```csharp
// Firma
IReadOnlyCollection<ValidationResultAdapter> FluentValidationExecutor.Execute<T>(T input, IValidator<T> validator)
```

Ejecuta el validator y devuelve una colección vacía si es válido, o una lista de `ValidationResultAdapter` si hay errores (los campos `Code`, `Message` y `Field` se mapean desde `ErrorCode`, `ErrorMessage` y `PropertyName`).

### Ejemplo de Validator

```csharp
public class MyRequestAdapterValidator : AbstractValidator<MyRequestAdapter>
{
    public MyRequestAdapterValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
                .WithMessage(MessageCatalog.GetErrorByCode(21002))
                .WithErrorCode("21002")
            .MinimumLength(3)
                .WithMessage(MessageCatalog.GetErrorByCode(21004))
                .WithErrorCode("21004")
            .MaximumLength(100)
                .WithMessage(MessageCatalog.GetErrorByCode(21005))
                .WithErrorCode("21005");

        RuleFor(x => x.Email)
            .NotEmpty()
                .WithMessage(MessageCatalog.GetErrorByCode(21002))
                .WithErrorCode("21002")
            .EmailAddress()
                .WithMessage(MessageCatalog.GetErrorByCode(21007))
                .WithErrorCode("21007");
    }
}
```

---

## 5. Patrón de Use Case

El Use Case valida headers primero (422), luego el request (400), y retorna `Task.FromResult` (sin `async`/`await` innecesario).

```csharp
public class MyFeatureCase : IMyFeaturePort
{
    private readonly ILogger<MyFeatureCase> _logger;

    public MyFeatureCase(ILogger<MyFeatureCase> logger)
    {
        _logger = logger;
    }

    public Task<EasyResult<MyFeatureAdapter>> ExecuteAsync(
        MyFeatureRequestAdapter request,
        TraceIdentifierAdapter header,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "MyFeatureCase started. MessageId={MessageId} DeviceId={DeviceId}",
            header.MessageIdentifier,
            header.DeviceIdentifier);

        // 1. Validar headers (422 Unprocessable Entity)
        var headerErrors = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
        if (headerErrors.Any())
            return Task.FromResult(EasyResult<MyFeatureAdapter>.Failure(422, headerErrors));

        // 2. Validar request (400 Bad Request)
        var requestErrors = FluentValidationExecutor.Execute(request, new MyFeatureRequestAdapterValidator());
        if (requestErrors.Any())
            return Task.FromResult(EasyResult<MyFeatureAdapter>.Failure(400, requestErrors));

        // 3. Lógica de negocio
        var result = new MyFeatureAdapter { /* ... */ };

        _logger.LogInformation(
            "MyFeatureCase completed. ResultId={ResultId} MessageId={MessageId}",
            result.Id,
            header.MessageIdentifier);

        return Task.FromResult(EasyResult<MyFeatureAdapter>.Success(result));
    }
}
```

> **Regla:** Si el Use Case no tiene operaciones `await`, usar `Task.FromResult(...)` en lugar de `async Task<...>`. Evita la advertencia **MA0004** del analizador Meziantou.

---

## 6. Controller Estándar — `EasyResponseHelper`

Para endpoints sin requisito de timeout explícito.

```csharp
[HttpPost("create")]
public async Task<IActionResult> Create(
    [FromHeader(Name = "x-device-identifier")] string? deviceIdentifier,
    [FromHeader(Name = "x-message-identifier")] string? messageIdentifier,
    [FromHeader(Name = "x-channel-identifier")] string? channelIdentifier,
    [FromBody] MyFeatureRequestAdapter request,
    CancellationToken ct = default)
{
    var headers = new TraceIdentifierAdapter
    {
        DeviceIdentifier  = deviceIdentifier,
        MessageIdentifier = messageIdentifier,
        ChannelIdentifier = channelIdentifier
    };

    try
    {
        var result = await _myFeatureUsecase.ExecuteAsync(request, headers, ct);

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "TraceId=[{Headers}] Validation=[{ValidationErrors}]",
                LoggerMapperHelper.ToString(headers),
                LoggerMapperHelper.ToString(result.ValidationValues.FirstOrDefault()!));

            return StatusCode(result.Status, EasyResponseHelper.WarningResponse(result.ValidationValues));
        }

        if (result.Status == 204)
        {
            _logger.LogInformation("TraceId=[{Headers}]", LoggerMapperHelper.ToString(headers));
            return NoContent();
        }

        return Ok(EasyResponseHelper.SuccessResponse(result.SuccessValue!));
    }
    catch (Exception ex)
    {
        var tracer = Regex.Replace(ex.StackTrace ?? "", @"\sat\s(.*?)\sin\s", string.Empty).Trim();
        _logger.LogError(
            "Message=[{Message}] TraceId=[{Headers}] StackTrace={Trace}",
            ex.Message,
            LoggerMapperHelper.ToString(headers),
            tracer);

        return StatusCode(500, EasyResponseHelper.ErrorResponse("11033"));
    }
}
```

### `EasyResponseHelper` — Métodos Disponibles

| Método | Uso |
|---|---|
| `SuccessResponse<T>(T data)` | Respuesta 200 con payload |
| `WarningResponse(IReadOnlyCollection<ValidationResultAdapter>)` | Respuesta 4xx con lista de errores |
| `ErrorResponse(string errorCode, string message)` | Respuesta 500 con código de error genérico |

---

## 7. Controller BIAN — `EasyBianResponseHelper` + Timeout

Para endpoints que requieren timeout explícito y formato de respuesta BIAN (clave dinámica por tipo).

```csharp
[HttpGet("retrieve")]
public async Task<IActionResult> Retrieve(
    [FromHeader(Name = "x-device-identifier")] string? deviceIdentifier,
    [FromHeader(Name = "x-message-identifier")] string? messageIdentifier,
    [FromHeader(Name = "x-channel-identifier")] string? channelIdentifier,
    CancellationToken ct = default)
{
    // Timeout de 7 segundos enlazado con el CancellationToken del cliente
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(7));

    var headers = new TraceIdentifierAdapter
    {
        DeviceIdentifier  = deviceIdentifier,
        MessageIdentifier = messageIdentifier,
        ChannelIdentifier = channelIdentifier
    };

    try
    {
        var result = await _myFeatureUsecase.ExecuteAsync(headers, timeoutCts.Token);

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "TraceId=[{Headers}] Validation=[{ValidationErrors}]",
                LoggerMapperHelper.ToString(headers),
                LoggerMapperHelper.ToString(result.ValidationValues.FirstOrDefault()!));

            return StatusCode(result.Status, EasyBianResponseHelper.WarningResponse(result.ValidationValues));
        }

        if (result.Status == 204)
        {
            _logger.LogInformation("TraceId=[{Headers}]", LoggerMapperHelper.ToString(headers));
            return NoContent();
        }

        return Ok(EasyBianResponseHelper.SuccessResponse(result.SuccessValue!));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // El cliente cerró la conexión
        _logger.LogWarning("Operation cancelled by client TraceId=[{Headers}]", LoggerMapperHelper.ToString(headers));
        return StatusCode(499, EasyBianResponseHelper.ErrorResponse("499", "Client Closed Request"));
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        // Timeout de 7s superado
        _logger.LogWarning("Operation cancelled by timeout (7s) TraceId=[{Headers}]", LoggerMapperHelper.ToString(headers));
        return StatusCode(408, EasyBianResponseHelper.ErrorResponse("408", "Request Timeout"));
    }
    catch (Exception ex)
    {
        var tracer = Regex.Replace(ex.StackTrace ?? "", @"\sat\s(.*?)\sin\s", string.Empty).Trim();
        _logger.LogError(
            "Message=[{Message}] TraceId=[{Headers}] StackTrace={Trace}",
            ex.Message,
            LoggerMapperHelper.ToString(headers),
            tracer);

        return StatusCode(500, EasyBianResponseHelper.ErrorResponse());
    }
}
```

### `EasyBianResponseHelper` — Métodos Disponibles

| Método | Uso |
|---|---|
| `SuccessResponse(object data)` | Respuesta 200 — clave derivada del nombre del tipo (ej. `"MyFeatureResponse"`) |
| `WarningResponse(IReadOnlyCollection<ValidationResultAdapter>)` | Respuesta 4xx — clave `"errors"` con array BIAN |
| `ErrorResponse(string errorCode, string message)` | Respuesta 4xx/5xx con código y mensaje explícitos |
| `ErrorResponse()` | Respuesta 500 con código `"1099"` y mensaje genérico |

### Diferencia de Formato JSON

**EasyResponseHelper** (estándar):
```json
{ "response": { "id": "123", "name": "Test" } }
```

**EasyBianResponseHelper** (BIAN — clave dinámica por tipo):
```json
{ "myFeatureResponse": { "id": "123", "name": "Test" } }
```

---

## 8. Tabla de Códigos HTTP

| Código | Semántica | Origen |
|---|---|---|
| `200` | Éxito con payload | `EasyResult.Success(data)` |
| `204` | Éxito sin contenido | `EasyResult.Empty()` |
| `400` | Request inválido (negocio/formato) | `EasyResult.Failure(400, requestErrors)` |
| `403` | No autorizado | `EasyResult.Failure(403, errors)` |
| `404` | Recurso no encontrado | `EasyResult.Failure(404, errors)` |
| `408` | Timeout interno (7s) | `OperationCanceledException` + timeout |
| `422` | Headers de traza inválidos | `EasyResult.Failure(422, headerErrors)` |
| `499` | Cliente cerró la conexión | `OperationCanceledException` + `ct.IsCancellationRequested` |
| `500` | Error inesperado de infraestructura | `catch (Exception)` |

---

## 9. Anti-patrones a Evitar

```csharp
// ❌ Lanzar excepciones para flujo de negocio
throw new NotFoundException("El usuario no existe");

// ✅ Retornar EasyResult.Failure con código semántico
return Task.FromResult(EasyResult<MyAdapter>.Failure(404, new[]
{
    new ValidationResultAdapter { Code = "21009", Message = MessageCatalog.GetErrorByCode(21009), Field = "UserId" }
}));

// ❌ Constructor posicional (no existe)
new ValidationResultAdapter("21002", "Cannot be empty")

// ✅ Object initializer con required properties
new ValidationResultAdapter { Code = "21002", Message = "Cannot be empty", Field = "Name" }

// ❌ Validación async innecesaria (MA0004)
var validation = await _validator.ValidateAsync(request);

// ✅ FluentValidationExecutor sincrónico
var errors = FluentValidationExecutor.Execute(request, new MyRequestAdapterValidator());

// ❌ async Task sin await (MA0004 — crea state machine innecesaria)
public async Task<EasyResult<MyAdapter>> ExecuteAsync(...)
{
    return EasyResult<MyAdapter>.Success(data);
}

// ✅ Task.FromResult cuando no hay operaciones await
public Task<EasyResult<MyAdapter>> ExecuteAsync(...)
{
    return Task.FromResult(EasyResult<MyAdapter>.Success(data));
}

// ❌ SuccessValue sin null-forgiving operator (CS8604 warning)
return Ok(EasyResponseHelper.SuccessResponse(result.SuccessValue));

// ✅ null-forgiving operator — IsSuccess garantiza que SuccessValue no es null
return Ok(EasyResponseHelper.SuccessResponse(result.SuccessValue!));

// ❌ Loguear ValidationValues completo (puede exponer PII)
_logger.LogWarning("Errors: {Errors}", result.ValidationValues);

// ✅ Loguear solo el primer error usando LoggerMapperHelper
_logger.LogWarning("TraceId=[{Headers}] Validation=[{ValidationErrors}]",
    LoggerMapperHelper.ToString(headers),
    LoggerMapperHelper.ToString(result.ValidationValues.FirstOrDefault()!));
```

---

## 10. Checklist antes de hacer PR

- [ ] El Use Case retorna `EasyResult<T>`, nunca lanza excepciones para flujo de negocio.
- [ ] La validación de headers usa `TraceIdentifierAdapterValidator` con status 422.
- [ ] La validación del request usa el validator específico con status 400.
- [ ] Usa `FluentValidationExecutor.Execute()` (sync), no `ValidateAsync()`.
- [ ] `ValidationResultAdapter` se construye con object initializer (required properties).
- [ ] Los códigos de error vienen de `MessageCatalog.GetErrorByCode(code)`.
- [ ] Los Use Cases sin `await` usan `Task.FromResult(...)`, no `async Task`.
- [ ] El controller usa `result.SuccessValue!` (null-forgiving operator) en el Success path.
- [ ] El controller loguea `LogWarning` cuando `!result.IsSuccess` y `LogError` en el `catch`.
- [ ] Los controllers BIAN usan `CancellationTokenSource.CreateLinkedTokenSource` + 7s timeout.
- [ ] Los controllers BIAN capturan `OperationCanceledException` con `when (ct.IsCancellationRequested)` (499) y `when (timeoutCts.IsCancellationRequested)` (408) **antes** del `catch (Exception)`.
- [ ] Los tests usan `NullLogger<T>.Instance` en lugar de mockear `ILogger`.
