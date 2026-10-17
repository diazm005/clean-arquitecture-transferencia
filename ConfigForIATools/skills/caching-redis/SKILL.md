---
name: caching-redis
description: Implementación de caché distribuido con StackExchange.Redis siguiendo Ports & Adapters. Cubre ICachePort, RedisAdapter, patrón Cache-Aside en Use Cases, invalidación de caché e integración con EasyResult. Se activa cuando se menciona Redis, caché, cache, ICachePort, StackExchange, rendimiento o TTL.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "[caso-de-uso-o-entidad-a-cachear]"
---

# Caching Redis Skill — StackExchange.Redis + Cache-Aside

Reduce la latencia y la carga sobre bases de datos mediante un cache distribuido. La lógica de negocio solo conoce `ICachePort` — nunca `StackExchange.Redis` directamente.

---

## 1. Arquitectura

```
Use Case (Application)
    └── ICachePort           ← interfaz definida en Domain
          └── RedisAdapter   ← implementación en RedisInfrastructure
                └── IConnectionMultiplexer (Singleton)
                      └── Redis Server
```

Proyectos involucrados:
- `Domain/Interfaces/ICachePort.cs` — contrato
- `RedisInfrastructure/RedisAdapter.cs` — implementación
- `RedisInfrastructure/RedisSetting.cs` — registro DI
- `SystemAPI/ApplicationSetting.cs` — Composition Root

---

## 2. `ICachePort` — La Interfaz

Ubicación: `Domain/Interfaces/ICachePort.cs`

```csharp
public interface ICachePort
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}
```

| Método | Descripción |
|---|---|
| `GetAsync<T>(key, ct)` | Retorna `null` (default) si la clave no existe o expiró |
| `SetAsync<T>(key, value, expiry?, ct)` | Serializa a JSON y almacena. `expiry = null` → sin TTL (persistente) |
| `RemoveAsync(key, ct)` | Elimina la clave (invalidación de cache) |

> `GetAsync` retorna `default(T)` — para tipos referencia es `null`. Siempre verificar `!= null` antes de usar el valor.

---

## 3. Configuración

### `appsettings.json` — agregar clave Redis

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379,password=tu_password,ssl=false,abortConnect=false"
  }
}
```

> Si no se define la clave `"Redis"`, el adaptador usa `"localhost:6379"` como fallback. Útil en desarrollo sin password.

### `ApplicationSetting.cs` — ya registrado

```csharp
services.AddRedisInfrastructure(configuration);
```

Esto registra:
- `IConnectionMultiplexer` → `Singleton` (una sola conexión reutilizada)
- `ICachePort` → `Singleton` (RedisAdapter, stateless)

### Variables de entorno (Docker / Producción)

```bash
ConnectionStrings__Redis=redis-server:6379,password=secret,ssl=true
```

---

## 4. Patrón Cache-Aside en Use Cases

El Use Case verifica cache primero. Si hay hit, retorna inmediatamente. Si hay miss, ejecuta la lógica, guarda en cache y retorna el resultado.

```csharp
public class GetProductCase : IGetProductPort
{
    private readonly ICachePort _cache;
    private readonly IProductQuery _productQuery;
    private readonly ILogger<GetProductCase> _logger;

    public GetProductCase(ICachePort cache, IProductQuery productQuery, ILogger<GetProductCase> logger)
    {
        _cache        = cache;
        _productQuery = productQuery;
        _logger       = logger;
    }

    public async Task<EasyResult<ProductAdapter>> ExecuteAsync(
        string productId,
        TraceIdentifierAdapter header,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "GetProductCase started. ProductId={ProductId} MessageId={MessageId}",
            productId, header.MessageIdentifier);

        // 1. Validar headers
        var headerErrors = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
        if (headerErrors.Any())
            return EasyResult<ProductAdapter>.Failure(422, headerErrors);

        // 2. Cache hit
        var cacheKey = $"product:{productId}";
        var cached = await _cache.GetAsync<ProductAdapter>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogInformation(
                "GetProductCase cache hit. ProductId={ProductId} MessageId={MessageId}",
                productId, header.MessageIdentifier);
            return EasyResult<ProductAdapter>.Success(cached);
        }

        // 3. Cache miss → fuente de datos
        var product = await _productQuery.GetByIdAsync(productId, ct);
        if (product is null)
        {
            return EasyResult<ProductAdapter>.Failure(404, new[]
            {
                new ValidationResultAdapter
                {
                    Code    = "21009",
                    Message = MessageCatalog.GetErrorByCode(21009),
                    Field   = "ProductId"
                }
            });
        }

        // 4. Guardar en cache (TTL 30 minutos)
        var result = new ProductAdapter { Id = product.Id, Name = product.Name };
        await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(30), ct);

        _logger.LogInformation(
            "GetProductCase completed. ProductId={ProductId} MessageId={MessageId}",
            productId, header.MessageIdentifier);

        return EasyResult<ProductAdapter>.Success(result);
    }
}
```

---

## 5. Invalidación de Cache (`RemoveAsync`)

Usar `RemoveAsync` cuando los datos cambian para evitar servir información obsoleta:

```csharp
public async Task<EasyResult<ProductAdapter>> UpdateProductCase(
    UpdateProductRequestAdapter request,
    TraceIdentifierAdapter header,
    CancellationToken ct = default)
{
    // ... validaciones y lógica de actualización ...

    // Invalidar el cache del producto actualizado
    var cacheKey = $"product:{request.ProductId}";
    await _cache.RemoveAsync(cacheKey, ct);

    _logger.LogInformation(
        "Cache invalidated. Key={CacheKey} MessageId={MessageId}",
        cacheKey, header.MessageIdentifier);

    return EasyResult<ProductAdapter>.Success(result);
}
```

---

## 6. Convención de Claves

Usar prefijos jerárquicos para evitar colisiones y facilitar la limpieza:

```csharp
// Entidad única
$"product:{productId}"          // "product:abc-123"
$"user:{userId}"                // "user:456"

// Colección filtrada
$"products:category:{catId}"    // "products:category:7"

// Configuración global
"config:feature-flags"

// Con versión (útil en deployments)
$"v1:product:{productId}"

// Combinado con tenant
$"tenant:{tenantId}:user:{userId}"
```

> Evitar claves dinámicas muy largas o con datos PII (email, teléfono). Usar solo IDs.

---

## 7. TTL Recomendados por Tipo de Dato

| Tipo de dato | TTL sugerido | Justificación |
|---|---|---|
| Catálogos / configuración | 60 min – 24 h | Cambian poco |
| Detalle de entidad (producto, usuario) | 15 – 30 min | Balance hit/freshness |
| Resultados de búsqueda | 5 – 10 min | Cambian con frecuencia |
| Tokens / sesiones | Duración del token | Igual al tiempo de vida del token |
| Datos de alta volatilidad | No cachear | El beneficio no justifica la complejidad |
| `null` (¿no cachear el miss?) | 1 – 5 min | Evita "cache stampede" en entidades inexistentes |

---

## 8. Serialización

`RedisAdapter` usa `System.Text.Json` con opciones por defecto (PascalCase). Los tipos cacheados deben:

- Tener propiedades públicas con getter/setter
- No usar `[JsonIgnore]` en propiedades requeridas para reconstrucción
- Ser serializables sin constructores con parámetros (o añadir `[JsonConstructor]`)

```csharp
// ✅ Serializable sin configuración extra
public class ProductAdapter
{
    public string Id   { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

// ⚠️ Requiere [JsonConstructor] si el único constructor tiene parámetros
public class ProductAdapter
{
    [JsonConstructor]
    public ProductAdapter(string id, string name) { Id = id; Name = name; }
    public string Id   { get; }
    public string Name { get; }
}
```

---

## 9. Registro DI del Use Case con Cache

```csharp
// ApplicationSetting.cs — ejemplo
services.AddRedisInfrastructure(configuration);   // ya registra ICachePort + IConnectionMultiplexer
services.AddTransient<IGetProductPort, GetProductCase>();
```

> `ICachePort` es **Singleton** — el `IConnectionMultiplexer` de StackExchange.Redis está diseñado para ser reutilizado. No registrar como `Scoped` o `Transient`.

---

## 10. Anti-patrones a Evitar

```csharp
// ❌ Usar StackExchange.Redis directamente en el Use Case (rompe Hexagonal)
private readonly IDatabase _db;
var value = await _db.StringGetAsync(key);

// ✅ Siempre inyectar ICachePort
private readonly ICachePort _cache;
var value = await _cache.GetAsync<ProductAdapter>(key, ct);

// ❌ Cache sin TTL en datos mutables (queda obsoleto para siempre)
await _cache.SetAsync(cacheKey, result);   // sin expiration

// ✅ Siempre definir TTL en datos que pueden cambiar
await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(30), ct);

// ❌ EasyResult.NotFound() — no existe este método
return EasyResult<ProductAdapter>.NotFound();

// ✅ Usar Failure con código 404 y ValidationResultAdapter
return EasyResult<ProductAdapter>.Failure(404, new[]
{
    new ValidationResultAdapter { Code = "21009", Message = MessageCatalog.GetErrorByCode(21009), Field = "ProductId" }
});

// ❌ Cachear PII o datos sensibles sin política de retención
var cacheKey = $"user:{email}";       // email como clave expone PII
await _cache.SetAsync(cacheKey, userWithPassword, TimeSpan.FromHours(1));

// ✅ Usar solo IDs como clave, nunca cachear contraseñas ni tokens
var cacheKey = $"user:{userId}";
await _cache.SetAsync(cacheKey, userPublicAdapter, TimeSpan.FromMinutes(15), ct);

// ❌ No pasar CancellationToken (ignora cancelación del cliente)
var cached = await _cache.GetAsync<ProductAdapter>(cacheKey);

// ✅ Propagar el CancellationToken siempre
var cached = await _cache.GetAsync<ProductAdapter>(cacheKey, ct);
```

---

## 11. Checklist antes de hacer PR

- [ ] El Use Case inyecta `ICachePort` (no `IDatabase` ni `IConnectionMultiplexer`).
- [ ] La clave de cache usa prefijo jerárquico (`entidad:id`) sin PII.
- [ ] `SetAsync` siempre tiene `TimeSpan` de expiración para datos mutables.
- [ ] Se verifica `cached is not null` antes de usar el valor del cache.
- [ ] El `CancellationToken` se propaga en todas las llamadas a `ICachePort`.
- [ ] Los datos actualizados o eliminados invalidan su clave con `RemoveAsync`.
- [ ] Los tipos cacheados son serializables con `System.Text.Json` (propiedades públicas).
- [ ] `ICachePort` está registrado en `ApplicationSetting.cs` vía `AddRedisInfrastructure`.
- [ ] El `appsettings.json` tiene la clave `"ConnectionStrings:Redis"` con el servidor correcto.
- [ ] No se loguea el valor cacheado completo si puede contener datos sensibles.
