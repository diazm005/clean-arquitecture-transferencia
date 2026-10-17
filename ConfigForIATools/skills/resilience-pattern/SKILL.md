---
name: resilience-pattern
description: Guía completa de resiliencia para llamadas HTTP salientes. Cubre HttpClientBuilder (fluent API), política de reintentos Polly, SocketsHttpHandler (connection pool / Keep-Alive), HttpResponseResult<T>, trazabilidad con MicroserviceCallMemoryQueue y patrones de consumo en adaptadores de infraestructura. Se activa cuando se menciona HttpClient, Polly, retry, resiliencia, llamadas HTTP, InternalHttpClient, timeout o connection pool.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "[adaptador-o-servicio-externo]"
---

# Resilience Skill — HttpClientBuilder + Polly

Todas las llamadas HTTP salientes pasan por `HttpClientBuilder` (fluent builder interno) registrado sobre `IHttpClientFactory` con política de reintentos Polly y connection pool optimizado.

---

## 1. Arquitectura de la Infraestructura HTTP

```
InfraAdapter (implements Domain.IPort)
    └── HttpClientBuilder          ← fluent builder
          └── IHttpClientFactory   ← "ArifyClient"
                └── SocketsHttpHandler  ← connection pool / Keep-Alive
                      └── Polly Policy ← reintentos en GET/HEAD/OPTIONS
```

Proyectos involucrados:

- `InternalHttpClientInfrastructure/` — `HttpClientBuilder`, `HttpResponseResult<T>`, `InternalHttpClientSetting`
- `FakeApiInfrastructure/` — ejemplo de adaptador consumidor
- `SystemAPI/ApplicationSetting.cs` — registro de DI

---

## 2. Configuración en `ApplicationSetting.cs`

```csharp
// Registrar el cliente HTTP con Polly (una sola vez en el Composition Root)
services.AddInternalHttpClientConnector();
```

`AddInternalHttpClientConnector` (en `InternalHttpClientSetting.cs`) configura:

| Parámetro                     | Valor          | Propósito                  |
| ----------------------------- | -------------- | -------------------------- |
| `Timeout`                     | 6 segundos     | Timeout global del cliente |
| `MaxConnectionsPerServer`     | 20             | Pool de conexiones         |
| `PooledConnectionIdleTimeout` | 60 s           | Cierra conexiones idle     |
| `PooledConnectionLifetime`    | 5 min          | Rotación DNS/TLS           |
| `ConnectTimeout`              | 2 s            | Evita cuelgues al conectar |
| `AutomaticDecompression`      | GZip + Deflate | Reducción de payload       |
| `DefaultRequestVersion`       | HTTP/1.1       | Compatibilidad máxima      |

---

## 3. Política de Reintentos Polly

Los reintentos están configurados **solo para métodos idempotentes**:

```csharp
.AddPolicyHandler((request) =>
{
    // GET, HEAD, OPTIONS → 2 reintentos con backoff exponencial
    if (request.Method == HttpMethod.Get
        || request.Method == HttpMethod.Head
        || request.Method == HttpMethod.Options)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()            // 5xx + red errors
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)  // 429
            .WaitAndRetryAsync(2, retry => TimeSpan.FromMilliseconds(200 * retry));
        //  retry=1 → 200ms, retry=2 → 400ms
    }

    // POST/PUT/PATCH/DELETE → sin reintentos (no idempotentes)
    return Policy.NoOpAsync<HttpResponseMessage>();
});
```

| Condición de reintento  | Descripción                              |
| ----------------------- | ---------------------------------------- |
| `HttpRequestException`  | Error de red / conexión                  |
| `5xx`                   | Error de servidor transitorio            |
| `429 TooManyRequests`   | Rate limiting                            |
| `POST/PUT/PATCH/DELETE` | **Sin reintento** — riesgo de duplicidad |

---

## 4. `HttpResponseResult<T>` — Resultado de Llamada HTTP

Ubicación: `InternalHttpClientInfrastructure/Collections/HttpResponseResult.cs`

```csharp
public sealed record HttpResponseResult<T>(
    int  StatusCode,   // Código HTTP real (0 si hubo excepción de red)
    bool IsSuccess,    // true solo si StatusCode == 200 y deserialización OK
    T?   Content,      // Payload deserializado (null si IsSuccess = false)
    string Url         // URL de la petición para logging/debug
);
```

> `IsSuccess = true` únicamente cuando `StatusCode == 200` **y** el contenido se deserializó correctamente a `T`. Ante cualquier fallo (red, timeout, non-200, JSON inválido), devuelve `IsSuccess = false` y nunca propaga excepciones.

---

## 5. `HttpClientBuilder` — Fluent API

Ubicación: `InternalHttpClientInfrastructure/HttpClientBuilder.cs`

### Métodos de configuración (chainables)

| Método                                             | Descripción                                              |
| -------------------------------------------------- | -------------------------------------------------------- |
| `.WithClient(string name)`                         | Nombre del cliente registrado (default: `"ArifyClient"`) |
| `.WithBaseUrl(string url)`                         | URL base sin `/` final                                   |
| `.WithEndpoint(string path)`                       | Ruta sin `/` inicial                                     |
| `.WithHeader(string key, string value)`            | Header personalizado                                     |
| `.WithBearerToken(string token)`                   | `Authorization: Bearer <token>`                          |
| `.WithAuthorization(string scheme, string token)`  | `Authorization: <scheme> <token>`                        |
| `.WithQuery(string key, string value)`             | Query string param                                       |
| `.WithTimeout(TimeSpan timeout)`                   | Override del timeout del cliente                         |
| `.WithMemoryQueue(queue, operationName, keyword?)` | Habilita trazabilidad automática                         |

### Métodos de ejecución

| Método                     | HTTP   | Retorno                 |
| -------------------------- | ------ | ----------------------- |
| `.GetAsync<T>(ct)`         | GET    | `HttpResponseResult<T>` |
| `.PostAsync<T>(body?, ct)` | POST   | `HttpResponseResult<T>` |
| `.PutAsync<T>(body?, ct)`  | PUT    | `HttpResponseResult<T>` |
| `.DeleteAsync<T>(ct)`      | DELETE | `HttpResponseResult<T>` |

---

## 6. Patrón de Adaptador de Infraestructura

Basado en `FakeApiInfrastructure/Queries/FakeApiQueryInfra.cs`:

```csharp
public class MyExternalApiInfra : IMyExternalQuery
{
    private readonly ILogger<MyExternalApiInfra> _logger;
    private readonly MicroserviceCallMemoryQueue _memQueue;
    private readonly IHttpClientFactory _httpClientFactory;

    public MyExternalApiInfra(
        IHttpClientFactory factory,
        ILogger<MyExternalApiInfra> logger,
        MicroserviceCallMemoryQueue memQueue)
    {
        _logger          = logger;
        _memQueue        = memQueue;
        _httpClientFactory = factory;
    }

    public async Task<MyDomainEntity> GetAsync(string id, CancellationToken ct = default)
    {
        var httpClient = new HttpClientBuilder(_httpClientFactory, _logger);

        var response = await httpClient
            .WithBaseUrl("https://api.external-service.com")
            .WithEndpoint($"resources/{id}")
            .WithBearerToken("my-token")                           // si requiere auth
            .WithMemoryQueue(_memQueue, "MyExternalApiInfra.GetAsync", id)  // trazabilidad
            .GetAsync<MyExternalApiCollection>(ct);

        // ✅ Siempre verificar IsSuccess antes de acceder a Content
        if (!response.IsSuccess)
            return new MyDomainEntity();   // fallback vacío — nunca lanzar excepción

        return new MyDomainEntity
        {
            Id    = response.Content!.Id,
            Name  = response.Content!.Name
        };
    }

    public async Task<MyDomainEntity> CreateAsync(MyRequestBody body, CancellationToken ct = default)
    {
        var httpClient = new HttpClientBuilder(_httpClientFactory, _logger);

        // POST — sin reintentos automáticos (no idempotente)
        var response = await httpClient
            .WithBaseUrl("https://api.external-service.com")
            .WithEndpoint("resources")
            .WithMemoryQueue(_memQueue, "MyExternalApiInfra.CreateAsync")
            .PostAsync<MyExternalApiCollection>(body, ct);

        if (!response.IsSuccess)
            return new MyDomainEntity();

        return new MyDomainEntity
        {
            Id   = response.Content!.Id,
            Name = response.Content!.Name
        };
    }
}
```

---

## 7. Trazabilidad con `MicroserviceCallMemoryQueue`

Cuando se llama a `.WithMemoryQueue(queue, operationName, keyword?)`, el builder registra automáticamente:

| Campo trazado        | Origen                                                 |
| -------------------- | ------------------------------------------------------ |
| `OperationName`      | Nombre del método (ej. `"FakeApiQueryInfra.GetAsync"`) |
| `Keyword`            | Identificador del recurso (ej. `userId`)               |
| `RequestUrl`         | URL completa construida                                |
| `RequestPayload`     | Body serializado (POST/PUT)                            |
| `RequestDatetime`    | Timestamp de inicio                                    |
| `ResponseStatusCode` | Código HTTP real (0 si fallo de red)                   |
| `ResponsePayload`    | Body raw de respuesta o mensaje de error               |
| `ResponseDatetime`   | Timestamp de fin                                       |

La cola es **no bloqueante** (`TryPush`) — si la cola está llena, loguea un `LogWarning` sin afectar el flujo.

### Registro de DI (en `ApplicationSetting.cs`)

```csharp
// La cola ya debe estar registrada (singleton) antes de los adaptadores
services.AddSingleton<MicroserviceCallMemoryQueue>();
services.AddScoped<IMyExternalQuery, MyExternalApiInfra>();
```

---

## 8. Colecciones de Respuesta (Mapeo HTTP → Dominio)

Cada adaptador define su propia colección interna que mapea el JSON externo:

```csharp
// MyExternalApiInfra/Collections/MyExternalApiCollection.cs
public sealed class MyExternalApiCollection
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
```

Estas colecciones **no salen del proyecto de infraestructura**. El adaptador convierte `MyExternalApiCollection` → `MyDomainEntity`.

---

## 9. Manejo de Errores — Comportamiento del Builder

El builder captura todas las excepciones internamente y nunca las propaga al adaptador:

| Excepción                             | `StatusCode` | `IsSuccess` | Acción                             |
| ------------------------------------- | ------------ | ----------- | ---------------------------------- |
| `HttpRequestException`                | `0`          | `false`     | `LogError` + trace con mensaje     |
| `TaskCanceledException` (timeout)     | `0`          | `false`     | `LogError` + trace con "Timeout"   |
| `TaskCanceledException` (cancelación) | `0`          | `false`     | `LogError` + trace con "Cancelled" |
| `JsonException` (deserialización)     | HTTP real    | `false`     | `LogError` con body raw            |
| HTTP non-200                          | HTTP real    | `false`     | `LogWarning` con headers + body    |
| `Exception` genérica                  | `0`          | `false`     | `LogError` con tipo + mensaje      |

---

## 10. Timeout — Dos Niveles

| Nivel                    | Dónde                               | Valor        | Tipo                              |
| ------------------------ | ----------------------------------- | ------------ | --------------------------------- |
| **Conexión**             | `SocketsHttpHandler.ConnectTimeout` | 2 s          | Solo apertura de socket           |
| **Request global**       | `HttpClient.Timeout`                | 6 s          | Request completo (sin reintentos) |
| **Override por request** | `.WithTimeout(TimeSpan)`            | configurable | Override en instancia concreta    |

> El timeout del `HttpClient` (6 s) aplica por intento. Con 2 reintentos (backoff 200ms + 400ms) el tiempo máximo total puede ser `6s + 200ms + 6s + 400ms + 6s ≈ 19s`. Considerar este acumulado al diseñar el timeout del Controller (por defecto 7 s).

---

## 11. Anti-patrones a Evitar

```csharp
// ❌ Crear HttpClient directamente — ignora pool y Polly
var client = new HttpClient();

// ✅ Siempre usar HttpClientBuilder sobre IHttpClientFactory
var httpClient = new HttpClientBuilder(_httpClientFactory, _logger);

// ❌ Propagar excepción cuando el HTTP falla (rompe el flujo de negocio)
var response = await client.GetAsync(url);
response.EnsureSuccessStatusCode();   // lanza excepción si non-2xx

// ✅ Verificar IsSuccess y retornar fallback
if (!response.IsSuccess) return new MyDomainEntity();

// ❌ Reintentar POST manualmente (riesgo de duplicidad)
for (int i = 0; i < 3; i++) { await PostAsync(...); }

// ✅ Polly solo reintenta GET/HEAD/OPTIONS automáticamente; POST usa NoOp

// ❌ Acceder a Content sin verificar IsSuccess (puede ser null)
var name = response.Content!.Name;

// ✅ Verificar IsSuccess primero
if (!response.IsSuccess) return new MyDomainEntity();
var name = response.Content!.Name;

// ❌ No registrar trazabilidad en llamadas externas importantes
var response = await httpClient.WithBaseUrl(...).GetAsync<T>(ct);

// ✅ Añadir WithMemoryQueue para trazabilidad end-to-end
var response = await httpClient
    .WithBaseUrl(...)
    .WithMemoryQueue(_memQueue, "ClassName.MethodName", keyword)
    .GetAsync<T>(ct);
```

---

## 12. Checklist antes de hacer PR

- [ ] El adaptador inyecta `IHttpClientFactory`, `ILogger<T>` y `MicroserviceCallMemoryQueue`.
- [ ] Se usa `HttpClientBuilder` (nunca `new HttpClient()` directo).
- [ ] Se verifica `response.IsSuccess` antes de acceder a `response.Content`.
- [ ] En caso de fallo HTTP, se retorna un fallback vacío (nunca se lanza excepción).
- [ ] Las colecciones de mapeo están en el proyecto de infraestructura y no se exponen al dominio.
- [ ] El adaptador convierte `Collection → DomainEntity` en el mismo método.
- [ ] Se llama a `.WithMemoryQueue(...)` con un `operationName` descriptivo en llamadas críticas.
- [ ] Los métodos POST/PUT/DELETE **no** esperan reintentos automáticos de Polly.
- [ ] El `CancellationToken` se pasa a `GetAsync<T>(ct)` / `PostAsync<T>(body, ct)`.
- [ ] El adaptador está registrado en `ApplicationSetting.cs` como `Scoped`.
