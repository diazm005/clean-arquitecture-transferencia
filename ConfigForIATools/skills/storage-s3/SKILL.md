---
name: storage-s3
description: Almacenamiento persistente de archivos en Amazon S3 usando Ports & Adapters. Cubre IStoragePort, S3Adapter, subida/descarga/borrado de objetos, integración con EasyResult y streaming desde controllers. Se activa cuando se menciona S3, AWS, storage, almacenamiento, archivos, upload, download, bucket o IStoragePort.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "[caso-de-uso-o-tipo-de-archivo]"
---

# AWS S3 Storage Skill — AWSSDK.S3 + Ports & Adapters

Almacenamiento persistente de objetos en S3 sin acoplar el SDK de AWS a la capa de Application. La lógica de negocio solo conoce `IStoragePort`.

---

## 1. Arquitectura

```
Controller (SystemAPI)
    └── Use Case (Application)
          └── IStoragePort          ← interfaz en Domain
                └── S3Adapter       ← implementación en AwsS3Infrastructure
                      └── IAmazonS3 (Singleton)
                            └── Amazon S3
```

Proyectos involucrados:
- `Domain/Interfaces/IStoragePort.cs` — contrato
- `AwsS3Infrastructure/S3Adapter.cs` — implementación con `TransferUtility`
- `AwsS3Infrastructure/AwsS3Setting.cs` — registro DI
- `SystemAPI/ApplicationSetting.cs` — Composition Root

---

## 2. `IStoragePort` — La Interfaz

Ubicación: `Domain/Interfaces/IStoragePort.cs`

```csharp
public interface IStoragePort
{
    Task<string>  UploadAsync(Stream data, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream?> DownloadAsync(string fileKey, CancellationToken ct = default);
    Task          DeleteAsync(string fileKey, CancellationToken ct = default);
}
```

| Método | Retorno | Descripción |
|---|---|---|
| `UploadAsync(stream, fileName, contentType, ct)` | `string` — el `fileKey` almacenado | Sube el objeto a S3 vía `TransferUtility` (soporta multipart automático) |
| `DownloadAsync(fileKey, ct)` | `Stream?` — `null` si no existe (404) | Retorna el stream del objeto; no lanza excepción en not-found |
| `DeleteAsync(fileKey, ct)` | `Task` | Elimina el objeto; operación idempotente (S3 no lanza error si no existe) |

> `UploadAsync` retorna el `fileKey` para que el Use Case lo persista como referencia. Nunca construyas la URL pública directamente desde el Use Case.

---

## 3. Configuración

### `appsettings.json` — claves exactas

```json
{
  "Aws": {
    "Region": "us-east-1",
    "S3": {
      "BucketName": "mi-bucket-produccion"
    }
  }
}
```

> Las claves son `"Aws:Region"` y `"Aws:S3:BucketName"` — no `"AwsS3:Region"`.

### `ApplicationSetting.cs` — ya registrado

```csharp
services.AddAwsS3Infrastructure(configuration);
```

Esto registra:
- `IAmazonS3` → `Singleton` (cliente AWS reutilizado)
- `IStoragePort` → `Singleton` (S3Adapter, stateless)

### Variables de entorno (Docker / Producción)

```bash
# Config del bucket
Aws__Region=us-east-1
Aws__S3__BucketName=mi-bucket-produccion

# Credenciales AWS (opción A — variables de entorno)
AWS_ACCESS_KEY_ID=AKIA...
AWS_SECRET_ACCESS_KEY=secret...

# Opción B — IAM Role asignado a la instancia/contenedor (recomendado en AWS)
# No requiere variables de credenciales; el SDK las toma automáticamente
```

### Autenticación AWS — Credential Provider Chain

`AmazonS3Client` busca credenciales en este orden automáticamente:
1. Variables de entorno (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`)
2. Archivo `~/.aws/credentials` (desarrollo local con `aws configure`)
3. IAM Role de la instancia EC2 / tarea ECS / pod EKS (**recomendado en producción**)

---

## 4. Patrón de Upload en Use Case

El Controller extrae los datos del `IFormFile` y los pasa al Use Case como primitivos. `IFormFile` es un concern de ASP.NET Core y no debe cruzar a la capa de Application.

### Adapter del request

```csharp
// Application/Usecases/UploadDocument/UploadDocumentRequestAdapter.cs
public sealed class UploadDocumentRequestAdapter
{
    public required Stream   Data        { get; init; }
    public required string   FileName    { get; init; }
    public required string   ContentType { get; init; }
}
```

### Use Case — Upload

```csharp
public class UploadDocumentCase : IUploadDocumentPort
{
    private readonly IStoragePort _storage;
    private readonly ILogger<UploadDocumentCase> _logger;

    public UploadDocumentCase(IStoragePort storage, ILogger<UploadDocumentCase> logger)
    {
        _storage = storage;
        _logger  = logger;
    }

    public async Task<EasyResult<UploadDocumentAdapter>> ExecuteAsync(
        UploadDocumentRequestAdapter request,
        TraceIdentifierAdapter header,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "UploadDocumentCase started. FileName={FileName} MessageId={MessageId}",
            request.FileName, header.MessageIdentifier);

        // 1. Validar headers
        var headerErrors = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
        if (headerErrors.Any())
            return EasyResult<UploadDocumentAdapter>.Failure(422, headerErrors);

        // 2. Validar request
        var requestErrors = FluentValidationExecutor.Execute(request, new UploadDocumentRequestAdapterValidator());
        if (requestErrors.Any())
            return EasyResult<UploadDocumentAdapter>.Failure(400, requestErrors);

        // 3. Construir key jerárquico
        var safeFileName = Path.GetFileName(request.FileName);   // evita path traversal
        var fileKey = $"documents/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid()}-{safeFileName}";

        // 4. Subir a S3 — retorna el fileKey almacenado
        var storedKey = await _storage.UploadAsync(request.Data, fileKey, request.ContentType, ct);

        _logger.LogInformation(
            "UploadDocumentCase completed. FileKey={FileKey} MessageId={MessageId}",
            storedKey, header.MessageIdentifier);

        return EasyResult<UploadDocumentAdapter>.Success(new UploadDocumentAdapter
        {
            FileKey = storedKey
        });
    }
}
```

### Controller — Upload con `IFormFile`

```csharp
[HttpPost("upload")]
public async Task<IActionResult> Upload(
    [FromHeader(Name = "x-device-identifier")]  string? deviceIdentifier,
    [FromHeader(Name = "x-message-identifier")] string? messageIdentifier,
    [FromHeader(Name = "x-channel-identifier")] string? channelIdentifier,
    IFormFile file,
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
        // Extraer primitivos de IFormFile ANTES de pasar al Use Case
        using var stream = file.OpenReadStream();

        var request = new UploadDocumentRequestAdapter
        {
            Data        = stream,
            FileName    = file.FileName,
            ContentType = file.ContentType
        };

        var result = await _uploadDocumentUsecase.ExecuteAsync(request, headers, ct);

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "TraceId=[{Headers}] Validation=[{ValidationErrors}]",
                LoggerMapperHelper.ToString(headers),
                LoggerMapperHelper.ToString(result.ValidationValues.FirstOrDefault()!));

            return StatusCode(result.Status, EasyResponseHelper.WarningResponse(result.ValidationValues));
        }

        return Ok(EasyResponseHelper.SuccessResponse(result.SuccessValue!));
    }
    catch (Exception ex)
    {
        var tracer = Regex.Replace(ex.StackTrace ?? "", @"\sat\s(.*?)\sin\s", string.Empty).Trim();
        _logger.LogError(
            "Message=[{Message}] TraceId=[{Headers}] StackTrace={Trace}",
            ex.Message, LoggerMapperHelper.ToString(headers), tracer);

        return StatusCode(500, EasyResponseHelper.ErrorResponse("11033"));
    }
}
```

---

## 5. Patrón de Download en Use Case y Controller

### Use Case — Download

```csharp
public async Task<EasyResult<DownloadDocumentAdapter>> ExecuteAsync(
    string fileKey,
    TraceIdentifierAdapter header,
    CancellationToken ct = default)
{
    _logger.LogInformation(
        "DownloadDocumentCase started. FileKey={FileKey} MessageId={MessageId}",
        fileKey, header.MessageIdentifier);

    var headerErrors = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
    if (headerErrors.Any())
        return EasyResult<DownloadDocumentAdapter>.Failure(422, headerErrors);

    // DownloadAsync retorna null si el objeto no existe en S3 (404)
    var stream = await _storage.DownloadAsync(fileKey, ct);

    if (stream is null)
    {
        return EasyResult<DownloadDocumentAdapter>.Failure(404, new[]
        {
            new ValidationResultAdapter
            {
                Code    = "21009",
                Message = MessageCatalog.GetErrorByCode(21009),
                Field   = "FileKey"
            }
        });
    }

    _logger.LogInformation(
        "DownloadDocumentCase completed. FileKey={FileKey} MessageId={MessageId}",
        fileKey, header.MessageIdentifier);

    return EasyResult<DownloadDocumentAdapter>.Success(new DownloadDocumentAdapter
    {
        Stream      = stream,
        ContentType = "application/octet-stream",
        FileName    = Path.GetFileName(fileKey)
    });
}
```

### Controller — Streaming de la respuesta

```csharp
[HttpGet("download/{fileKey}")]
public async Task<IActionResult> Download(
    string fileKey,
    [FromHeader(Name = "x-device-identifier")]  string? deviceIdentifier,
    [FromHeader(Name = "x-message-identifier")] string? messageIdentifier,
    [FromHeader(Name = "x-channel-identifier")] string? channelIdentifier,
    CancellationToken ct = default)
{
    // ...headers y try/catch estándar...

    var result = await _downloadDocumentUsecase.ExecuteAsync(fileKey, headers, ct);

    if (!result.IsSuccess)
        return StatusCode(result.Status, EasyResponseHelper.WarningResponse(result.ValidationValues));

    // Retornar el stream directamente — no cargar en memoria
    return File(result.SuccessValue!.Stream, result.SuccessValue.ContentType, result.SuccessValue.FileName);
}
```

---

## 6. Patrón de Delete en Use Case

```csharp
public async Task<EasyResult<DeleteDocumentAdapter>> ExecuteAsync(
    string fileKey,
    TraceIdentifierAdapter header,
    CancellationToken ct = default)
{
    var headerErrors = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
    if (headerErrors.Any())
        return EasyResult<DeleteDocumentAdapter>.Failure(422, headerErrors);

    // DeleteAsync es idempotente — no lanza excepción si el objeto no existe
    await _storage.DeleteAsync(fileKey, ct);

    _logger.LogInformation(
        "DeleteDocumentCase completed. FileKey={FileKey} MessageId={MessageId}",
        fileKey, header.MessageIdentifier);

    // 204 No Content — éxito sin payload
    return EasyResult<DeleteDocumentAdapter>.Empty();
}
```

---

## 7. Convención de Keys (Rutas en S3)

Usar prefijos jerárquicos para organizar objetos y facilitar políticas de lifecycle:

```csharp
// Por fecha y tipo
$"documents/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid()}-{safeFileName}"
// → "documents/2026/02/a1b2c3d4-contrato.pdf"

// Por tenant
$"tenants/{tenantId}/invoices/{Guid.NewGuid()}.pdf"

// Temporal (se puede limpiar con lifecycle rule)
$"temp/{Guid.NewGuid()}-{safeFileName}"

// Siempre sanitizar el nombre de archivo
var safeFileName = Path.GetFileName(request.FileName);  // elimina path traversal
```

---

## 8. Registro DI del Use Case con Storage

```csharp
// ApplicationSetting.cs
services.AddAwsS3Infrastructure(configuration);          // registra IStoragePort + IAmazonS3
services.AddTransient<IUploadDocumentPort, UploadDocumentCase>();
services.AddTransient<IDownloadDocumentPort, DownloadDocumentCase>();
services.AddTransient<IDeleteDocumentPort, DeleteDocumentCase>();
```

---

## 9. Anti-patrones a Evitar

```csharp
// ❌ Usar IAmazonS3 directamente en el Use Case (rompe Hexagonal)
private readonly IAmazonS3 _s3Client;

// ✅ Siempre inyectar IStoragePort
private readonly IStoragePort _storage;

// ❌ Pasar IFormFile al Use Case (acopla ASP.NET Core con Application)
public Task<EasyResult<T>> ExecuteAsync(IFormFile file, ...) { }

// ✅ Extraer primitivos en el Controller, pasar UploadDocumentRequestAdapter
using var stream = file.OpenReadStream();
var request = new UploadDocumentRequestAdapter { Data = stream, ... };

// ❌ No sanitizar el FileName — riesgo de path traversal
var fileKey = $"uploads/{request.FileName}";

// ✅ Siempre usar Path.GetFileName para eliminar rutas relativas
var safeFileName = Path.GetFileName(request.FileName);
var fileKey = $"uploads/{Guid.NewGuid()}-{safeFileName}";

// ❌ Ignorar el retorno de UploadAsync (el fileKey es la referencia al objeto)
await _storage.UploadAsync(stream, key, contentType, ct);

// ✅ Capturar el fileKey retornado para persistirlo
var storedKey = await _storage.UploadAsync(stream, key, contentType, ct);

// ❌ Cargar el stream de descarga en memoria (falla con archivos grandes)
var bytes = await stream.ReadAllBytesAsync();
return Ok(bytes);

// ✅ Retornar el stream directamente al cliente
return File(stream, contentType, fileName);

// ❌ EasyResult.NotFound() — no existe
return EasyResult<T>.NotFound();

// ✅ Usar Failure con código 404
return EasyResult<T>.Failure(404, new[]
{
    new ValidationResultAdapter { Code = "21009", Message = MessageCatalog.GetErrorByCode(21009), Field = "FileKey" }
});
```

---

## 10. Checklist antes de hacer PR

- [ ] El Use Case inyecta `IStoragePort` (no `IAmazonS3` ni `TransferUtility`).
- [ ] El Controller extrae `Stream`, `FileName` y `ContentType` del `IFormFile` antes de llamar al Use Case.
- [ ] El `FileName` se sanitiza con `Path.GetFileName()` antes de usarlo en el `fileKey`.
- [ ] El `fileKey` generado incluye `Guid.NewGuid()` para evitar colisiones.
- [ ] `UploadAsync` retorna el `fileKey` y se persiste como referencia en la respuesta.
- [ ] `DownloadAsync` verifica `stream is null` (→ 404) antes de retornar.
- [ ] La respuesta de descarga usa `return File(stream, ...)` — no carga en memoria.
- [ ] `DeleteAsync` retorna `EasyResult.Empty()` (204), no `EasyResult.Success(null)`.
- [ ] Las credenciales AWS no están hardcodeadas en código ni en `appsettings.json`.
- [ ] Las claves de config son `"Aws:Region"` y `"Aws:S3:BucketName"` (no `"AwsS3:..."`).
- [ ] El `CancellationToken` se propaga en todas las llamadas a `IStoragePort`.
