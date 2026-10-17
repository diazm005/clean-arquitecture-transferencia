---
name: database-mongodb
description: Acceso a MongoDB siguiendo Ports & Adapters con MongoDB.Driver. Cubre IXxxRepository, MongoAdapter, MongodbSetting, proyecciones, filtros tipados y anti-patrones. Se activa cuando se menciona MongoDB, Mongo, IMongoCollection, MongoClient, repositorio, base de datos documental o MongodbInfrastructure.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "[entidad-o-coleccion-a-implementar]"
---

# Database MongoDB Skill — MongoDB.Driver + Hexagonal Architecture

Accede a MongoDB sin acoplar la lógica de negocio al driver. La capa Application solo conoce las interfaces de `Domain` — nunca `MongoDB.Driver` directamente.

---

## 1. Arquitectura

```
Use Case (Application)
    └── IXxxRepository / IXxxQuery   ← interfaz en Domain/Interfaces
          └── XxxMongoAdapter        ← implementación en MongodbInfrastructure
                └── IMongoCollection<XxxDocument>
                      └── MongoDB
```

Proyectos involucrados:
- `Domain/Interfaces/IXxxRepository.cs` — contrato de escritura
- `Domain/Interfaces/IXxxQuery.cs` — contrato de lectura (CQRS ligero)
- `MongodbInfrastructure/Documents/XxxDocument.cs` — documento Mongo (no sale de infra)
- `MongodbInfrastructure/Adapters/XxxMongoAdapter.cs` — implementación
- `MongodbInfrastructure/MongodbSetting.cs` — registro DI
- `SystemAPI/ApplicationSetting.cs` — Composition Root

---

## 2. NuGet Packages

```xml
<!-- MongodbInfrastructure/MongodbInfrastructure.csproj -->
<PackageReference Include="MongoDB.Driver" Version="3.4.0" />
```

---

## 3. Separación Domain / Infrastructure

| Clase | Proyecto | Descripción |
|---|---|---|
| `ProductEntity` | `Domain/Entities/` | Entidad pura del dominio |
| `IProductRepository` | `Domain/Interfaces/` | Contrato de escritura |
| `IProductQuery` | `Domain/Interfaces/` | Contrato de lectura |
| `ProductDocument` | `MongodbInfrastructure/Documents/` | Documento Mongo (internal) |
| `ProductMongoAdapter` | `MongodbInfrastructure/Adapters/` | Implementación con driver |

> `ProductDocument` nunca sale de `MongodbInfrastructure`. El adapter mapea `ProductDocument ↔ ProductEntity`.

---

## 4. Configuración

### `appsettings.json`

```json
{
  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "system_api_db"
  }
}
```

### Variables de entorno (Docker / Producción)

```bash
MONGO_DB_SERVER=mongodb://user:password@mongo-server:27017
MONGO_DB_NAME=system_api_db
```

> Referencia del CLAUDE.md: `MONGO_DB_SERVER`, `MONGO_DB_NAME`, `MONGO_DB_USER`, `MONGO_DB_PASSWD`.

---

## 5. `MongodbSetting.cs` — Registro DI

Ubicación: `MongodbInfrastructure/MongodbSetting.cs`

```csharp
using Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongodbInfrastructure.Adapters;

namespace MongodbInfrastructure;

public static class MongodbSetting
{
    public static void AddMongodbInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["MongoDB:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("MONGO_DB_SERVER")
            ?? "mongodb://localhost:27017";

        var databaseName = configuration["MongoDB:DatabaseName"]
            ?? Environment.GetEnvironmentVariable("MONGO_DB_NAME")
            ?? "system_api_db";

        services.AddSingleton<IMongoClient>(new MongoClient(connectionString));
        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

        // Registrar adapters aquí
        services.AddSingleton<IProductRepository, ProductMongoAdapter>();
        services.AddSingleton<IProductQuery, ProductMongoAdapter>();
    }
}
```

> `IMongoClient` e `IMongoDatabase` son `Singleton` — el driver gestiona el pool de conexiones internamente.

---

## 6. Document (solo en Infrastructure)

Ubicación: `MongodbInfrastructure/Documents/ProductDocument.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongodbInfrastructure.Documents;

internal sealed class ProductDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("price")]
    public decimal Price { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

> Usar `[BsonElement("snake_case")]` para alinear con convenciones de MongoDB. `internal sealed` — nunca expuesto fuera de infra.

---

## 7. Adapter — Implementación Completa

Ubicación: `MongodbInfrastructure/Adapters/ProductMongoAdapter.cs`

```csharp
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongodbInfrastructure.Documents;

namespace MongodbInfrastructure.Adapters;

internal sealed class ProductMongoAdapter : IProductRepository, IProductQuery
{
    private readonly IMongoCollection<ProductDocument> _collection;
    private readonly ILogger<ProductMongoAdapter> _logger;

    public ProductMongoAdapter(IMongoDatabase database, ILogger<ProductMongoAdapter> logger)
    {
        _collection = database.GetCollection<ProductDocument>("products");
        _logger     = logger;
    }

    // ---- Query (lectura) ----

    public async Task<ProductEntity?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var filter = Builders<ProductDocument>.Filter.Eq(d => d.Id, id);
        var doc    = await _collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (doc is null)
        {
            _logger.LogWarning("Product not found. Id={ProductId}", id);
            return null;
        }

        return MapToEntity(doc);
    }

    public async Task<IReadOnlyList<ProductEntity>> GetAllAsync(CancellationToken ct = default)
    {
        var docs = await _collection.Find(FilterDefinition<ProductDocument>.Empty)
            .ToListAsync(ct).ConfigureAwait(false);

        return docs.Select(MapToEntity).ToList();
    }

    // ---- Repository (escritura) ----

    public async Task<string> SaveAsync(ProductEntity entity, CancellationToken ct = default)
    {
        var doc = MapToDocument(entity);
        await _collection.InsertOneAsync(doc, cancellationToken: ct).ConfigureAwait(false);

        _logger.LogInformation("Product saved. Id={ProductId}", doc.Id);
        return doc.Id;
    }

    public async Task<bool> UpdateAsync(ProductEntity entity, CancellationToken ct = default)
    {
        var filter = Builders<ProductDocument>.Filter.Eq(d => d.Id, entity.Id);
        var update = Builders<ProductDocument>.Update
            .Set(d => d.Name,  entity.Name)
            .Set(d => d.Price, entity.Price);

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct).ConfigureAwait(false);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var filter = Builders<ProductDocument>.Filter.Eq(d => d.Id, id);
        var result = await _collection.DeleteOneAsync(filter, ct).ConfigureAwait(false);
        return result.DeletedCount > 0;
    }

    // ---- Mappers ----

    private static ProductEntity MapToEntity(ProductDocument doc) =>
        new()
        {
            Id    = doc.Id,
            Name  = doc.Name,
            Price = doc.Price,
        };

    private static ProductDocument MapToDocument(ProductEntity entity) =>
        new()
        {
            Id    = string.IsNullOrEmpty(entity.Id) ? MongoDB.Bson.ObjectId.GenerateNewId().ToString() : entity.Id,
            Name  = entity.Name,
            Price = entity.Price,
        };
}
```

---

## 8. Interfaces en Domain

```csharp
// Domain/Interfaces/IProductRepository.cs — escritura
namespace Domain.Interfaces;

public interface IProductRepository
{
    Task<string> SaveAsync(ProductEntity entity, CancellationToken ct = default);
    Task<bool> UpdateAsync(ProductEntity entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

// Domain/Interfaces/IProductQuery.cs — lectura
namespace Domain.Interfaces;

public interface IProductQuery
{
    Task<ProductEntity?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<ProductEntity>> GetAllAsync(CancellationToken ct = default);
}
```

> Separar Query (lectura) de Repository (escritura) aplica CQRS ligero — permite mocks independientes en tests.

---

## 9. Uso en Use Case

```csharp
public class GetProductCase : IGetProductPort
{
    private readonly IProductQuery _productQuery;
    private readonly ILogger<GetProductCase> _logger;

    public GetProductCase(IProductQuery productQuery, ILogger<GetProductCase> logger)
    {
        _productQuery = productQuery;
        _logger       = logger;
    }

    public async Task<EasyResult<ProductAdapter>> ExecuteAsync(
        string productId,
        TraceIdentifierAdapter header,
        CancellationToken ct = default)
    {
        var headerErrors = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
        if (headerErrors.Any())
            return EasyResult<ProductAdapter>.Failure(422, headerErrors);

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

        return EasyResult<ProductAdapter>.Success(new ProductAdapter
        {
            Id   = product.Id,
            Name = product.Name
        });
    }
}
```

---

## 10. Índices (Performance)

Crear índices en el startup para evitar collection scans:

```csharp
// En MongodbSetting.cs o un inicializador separado
public static async Task EnsureIndexesAsync(IMongoDatabase database)
{
    var collection = database.GetCollection<ProductDocument>("products");

    var indexModel = new CreateIndexModel<ProductDocument>(
        Builders<ProductDocument>.IndexKeys.Ascending(d => d.Name),
        new CreateIndexOptions { Unique = false, Name = "idx_name" });

    await collection.Indexes.CreateOneAsync(indexModel).ConfigureAwait(false);
}
```

---

## 11. Anti-patrones a Evitar

```csharp
// ❌ Inyectar IMongoCollection directamente en el Use Case (rompe Hexagonal)
private readonly IMongoCollection<ProductDocument> _collection;

// ✅ Inyectar la interfaz de Domain
private readonly IProductQuery _productQuery;

// ❌ Exponer ProductDocument fuera de MongodbInfrastructure
public class GetProductCase { public ProductDocument Execute() {...} }

// ✅ Mapear a entidad de Domain dentro del adapter
private static ProductEntity MapToEntity(ProductDocument doc) => new() { ... };

// ❌ Find sin filtro tipado (string magic)
var filter = "{ name: 'foo' }";

// ✅ Siempre usar Builders<T>.Filter tipados
var filter = Builders<ProductDocument>.Filter.Eq(d => d.Name, "foo");

// ❌ Registrar MongoClient como Transient (una conexión por request)
services.AddTransient<IMongoClient>(...);

// ✅ MongoClient es Singleton — el driver gestiona el pool internamente
services.AddSingleton<IMongoClient>(...);

// ❌ No propagar CancellationToken
var doc = await _collection.Find(filter).FirstOrDefaultAsync();

// ✅ Propagar siempre el CancellationToken
var doc = await _collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
```

---

## 12. Checklist antes de hacer PR

- [ ] Las interfaces (`IXxxRepository`, `IXxxQuery`) están en `Domain/Interfaces`.
- [ ] `XxxDocument` es `internal sealed` y vive solo en `MongodbInfrastructure/Documents/`.
- [ ] El adapter mapea `Document ↔ Entity` con métodos privados estáticos.
- [ ] `IMongoClient` e `IMongoDatabase` están registrados como `Singleton`.
- [ ] Se usan `Builders<T>.Filter` tipados (no strings magic).
- [ ] El `CancellationToken` se propaga en todas las operaciones async.
- [ ] Se usa `.ConfigureAwait(false)` en todos los `await`.
- [ ] Los índices necesarios están creados en `MongodbSetting.cs` o inicializador.
- [ ] `AddMongodbInfrastructure` está invocado en `ApplicationSetting.cs`.
- [ ] El Use Case inyecta la interfaz de Domain, no el adapter directamente.
