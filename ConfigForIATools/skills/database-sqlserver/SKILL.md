---
name: database-sqlserver
description: Acceso a SQL Server con Dapper siguiendo Ports & Adapters. Cubre IXxxRepository, SqlServerAdapter, SqlServerSetting, queries parametrizados, transacciones y anti-patrones de inyección SQL. Se activa cuando se menciona SQL Server, Dapper, SqlConnection, IDbConnection, repositorio SQL, SqlServerInfrastructure o base de datos relacional.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "[entidad-o-tabla-a-implementar]"
---

# Database SQL Server Skill — Dapper + Hexagonal Architecture

Accede a SQL Server con Dapper sin acoplar la lógica de negocio al driver. Application solo conoce interfaces de `Domain` — nunca `SqlConnection` directamente.

---

## 1. Arquitectura

```
Use Case (Application)
    └── IXxxRepository / IXxxQuery   ← interfaz en Domain/Interfaces
          └── XxxSqlAdapter          ← implementación en SqlServerInfrastructure
                └── IDbConnection (SqlConnection)
                      └── SQL Server
```

Proyectos involucrados:
- `Domain/Interfaces/IXxxRepository.cs` — contrato de escritura
- `Domain/Interfaces/IXxxQuery.cs` — contrato de lectura
- `SqlServerInfrastructure/Adapters/XxxSqlAdapter.cs` — implementación con Dapper
- `SqlServerInfrastructure/SqlServerSetting.cs` — registro DI
- `SystemAPI/ApplicationSetting.cs` — Composition Root

---

## 2. NuGet Packages

```xml
<!-- SqlServerInfrastructure/SqlServerInfrastructure.csproj -->
<PackageReference Include="Dapper"                        Version="2.1.66" />
<PackageReference Include="Microsoft.Data.SqlClient"      Version="6.0.1"  />
```

---

## 3. Configuración

### `appsettings.json`

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=localhost,1433;Database=system_api_db;User Id=sa;Password=YourPassword;TrustServerCertificate=true;"
  }
}
```

### Variables de entorno (Docker / Producción)

```bash
CORE_DB_SERVER=sql-server-host
CORE_DB_NAME=system_api_db
CORE_DB_USER=sa
CORE_DB_PASSWD=YourSecretPassword
```

> Referencia del CLAUDE.md: `CORE_DB_SERVER`, `CORE_DB_NAME`, `CORE_DB_USER`, `CORE_DB_PASSWD`.

---

## 4. `SqlServerSetting.cs` — Registro DI

Ubicación: `SqlServerInfrastructure/SqlServerSetting.cs`

```csharp
using Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlServerInfrastructure.Adapters;

namespace SqlServerInfrastructure;

public static class SqlServerSetting
{
    public static void AddSqlServerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = BuildConnectionString(configuration);

        // Registrar factory de conexión — se crea una conexión por operación (Scoped o Transient)
        services.AddTransient<IDbConnectionFactory>(_ => new SqlConnectionFactory(connectionString));

        // Registrar adapters
        services.AddTransient<IProductRepository, ProductSqlAdapter>();
        services.AddTransient<IProductQuery, ProductSqlAdapter>();
    }

    private static string BuildConnectionString(IConfiguration configuration)
    {
        // Primero intentar connection string directa
        var direct = configuration.GetConnectionString("SqlServer");
        if (!string.IsNullOrEmpty(direct)) return direct;

        // Fallback a variables de entorno individuales
        var server   = Environment.GetEnvironmentVariable("CORE_DB_SERVER") ?? "localhost,1433";
        var database = Environment.GetEnvironmentVariable("CORE_DB_NAME")   ?? "system_api_db";
        var user     = Environment.GetEnvironmentVariable("CORE_DB_USER")   ?? "sa";
        var password = Environment.GetEnvironmentVariable("CORE_DB_PASSWD") ?? string.Empty;

        return $"Server={server};Database={database};User Id={user};Password={password};TrustServerCertificate=true;";
    }
}
```

---

## 5. `IDbConnectionFactory` — Abstracción de Conexión

Ubicación: `SqlServerInfrastructure/IDbConnectionFactory.cs`

```csharp
using System.Data;

namespace SqlServerInfrastructure;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

internal sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IDbConnection CreateConnection() =>
        new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
}
```

> Usar `IDbConnectionFactory` en lugar de inyectar `SqlConnection` directamente permite mockear en tests.

---

## 6. Interfaces en Domain

```csharp
// Domain/Interfaces/IProductRepository.cs — escritura
namespace Domain.Interfaces;

public interface IProductRepository
{
    Task<int> SaveAsync(ProductEntity entity, CancellationToken ct = default);
    Task<bool> UpdateAsync(ProductEntity entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

// Domain/Interfaces/IProductQuery.cs — lectura
namespace Domain.Interfaces;

public interface IProductQuery
{
    Task<ProductEntity?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ProductEntity>> GetAllAsync(CancellationToken ct = default);
}
```

---

## 7. Adapter — Implementación Completa

Ubicación: `SqlServerInfrastructure/Adapters/ProductSqlAdapter.cs`

```csharp
using Dapper;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace SqlServerInfrastructure.Adapters;

internal sealed class ProductSqlAdapter : IProductRepository, IProductQuery
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<ProductSqlAdapter> _logger;

    public ProductSqlAdapter(IDbConnectionFactory factory, ILogger<ProductSqlAdapter> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    // ---- Query (lectura) ----

    public async Task<ProductEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id, Name, Price, CreatedAt
            FROM   dbo.Products
            WHERE  Id = @Id
              AND  IsDeleted = 0
            """;

        using var connection = _factory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<ProductRow>(
            sql, new { Id = id }, commandTimeout: 30).ConfigureAwait(false);

        if (row is null)
        {
            _logger.LogWarning("Product not found. Id={ProductId}", id);
            return null;
        }

        return MapToEntity(row);
    }

    public async Task<IReadOnlyList<ProductEntity>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id, Name, Price, CreatedAt
            FROM   dbo.Products
            WHERE  IsDeleted = 0
            ORDER BY CreatedAt DESC
            """;

        using var connection = _factory.CreateConnection();
        var rows = await connection.QueryAsync<ProductRow>(sql, commandTimeout: 30).ConfigureAwait(false);

        return rows.Select(MapToEntity).ToList();
    }

    // ---- Repository (escritura) ----

    public async Task<int> SaveAsync(ProductEntity entity, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dbo.Products (Name, Price, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Name, @Price, @CreatedAt)
            """;

        using var connection = _factory.CreateConnection();
        var newId = await connection.ExecuteScalarAsync<int>(
            sql,
            new { entity.Name, entity.Price, CreatedAt = DateTime.UtcNow },
            commandTimeout: 30).ConfigureAwait(false);

        _logger.LogInformation("Product saved. Id={ProductId}", newId);
        return newId;
    }

    public async Task<bool> UpdateAsync(ProductEntity entity, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.Products
            SET    Name  = @Name,
                   Price = @Price
            WHERE  Id        = @Id
              AND  IsDeleted = 0
            """;

        using var connection = _factory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            sql,
            new { entity.Name, entity.Price, entity.Id },
            commandTimeout: 30).ConfigureAwait(false);

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.Products
            SET    IsDeleted = 1
            WHERE  Id = @Id
            """;

        using var connection = _factory.CreateConnection();
        var affected = await connection.ExecuteAsync(sql, new { Id = id }, commandTimeout: 30).ConfigureAwait(false);
        return affected > 0;
    }

    // ---- Mappers ----

    private static ProductEntity MapToEntity(ProductRow row) =>
        new()
        {
            Id    = row.Id,
            Name  = row.Name,
            Price = row.Price,
        };

    // Row es un tipo interno para el mapeo desde Dapper
    private sealed record ProductRow(int Id, string Name, decimal Price, DateTime CreatedAt);
}
```

---

## 8. Transacciones

Para operaciones que deben ser atómicas:

```csharp
public async Task<bool> TransferAsync(int fromId, int toId, decimal amount, CancellationToken ct = default)
{
    using var connection = _factory.CreateConnection();
    connection.Open();
    using var transaction = connection.BeginTransaction();

    try
    {
        await connection.ExecuteAsync(
            "UPDATE dbo.Accounts SET Balance = Balance - @Amount WHERE Id = @Id",
            new { Amount = amount, Id = fromId },
            transaction: transaction,
            commandTimeout: 30).ConfigureAwait(false);

        await connection.ExecuteAsync(
            "UPDATE dbo.Accounts SET Balance = Balance + @Amount WHERE Id = @Id",
            new { Amount = amount, Id = toId },
            transaction: transaction,
            commandTimeout: 30).ConfigureAwait(false);

        transaction.Commit();
        return true;
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        _logger.LogError(ex, "Transfer failed. FromId={FromId} ToId={ToId}", fromId, toId);
        return false;
    }
}
```

---

## 9. Registro en ApplicationSetting

```csharp
// SystemAPI/ApplicationSetting.cs
services.AddSqlServerInfrastructure(configuration);
services.AddTransient<IGetProductPort, GetProductCase>();
services.AddTransient<ISaveProductPort, SaveProductCase>();
```

---

## 10. Anti-patrones a Evitar

```csharp
// ❌ SQL dinámico con concatenación de strings (inyección SQL)
var sql = $"SELECT * FROM Products WHERE Name = '{name}'";

// ✅ Siempre usar parámetros con @
const string sql = "SELECT * FROM Products WHERE Name = @Name";
await connection.QueryAsync(sql, new { Name = name });

// ❌ Inyectar SqlConnection directamente en el Use Case (rompe Hexagonal)
private readonly SqlConnection _connection;

// ✅ Inyectar la interfaz de Domain
private readonly IProductQuery _productQuery;

// ❌ No cerrar la conexión (memory/connection leak)
var connection = _factory.CreateConnection();
await connection.QueryAsync(sql, params);

// ✅ Siempre usar 'using' para cerrar y devolver al pool
using var connection = _factory.CreateConnection();

// ❌ Sin commandTimeout (query puede colgar indefinidamente)
await connection.QueryAsync(sql, params);

// ✅ Definir commandTimeout siempre
await connection.QueryAsync(sql, params, commandTimeout: 30);

// ❌ Registrar el adapter como Singleton con conexión abierta
services.AddSingleton<IProductRepository, ProductSqlAdapter>();

// ✅ Transient — cada operación abre su propia conexión del pool
services.AddTransient<IProductRepository, ProductSqlAdapter>();
```

---

## 11. Checklist antes de hacer PR

- [ ] Las interfaces (`IXxxRepository`, `IXxxQuery`) están en `Domain/Interfaces`.
- [ ] El adapter es `internal sealed` y vive en `SqlServerInfrastructure/Adapters/`.
- [ ] Todos los SQL usan parámetros `@Param` (nunca concatenación de strings).
- [ ] Todas las conexiones se abren con `using var connection = _factory.CreateConnection()`.
- [ ] Se define `commandTimeout` en todas las queries.
- [ ] El `CancellationToken` se propaga donde Dapper lo soporte.
- [ ] Se usa `.ConfigureAwait(false)` en todos los `await`.
- [ ] Las operaciones atómicas usan transacciones con try/catch/rollback.
- [ ] El adapter está registrado como `Transient` en `SqlServerSetting.cs`.
- [ ] `AddSqlServerInfrastructure` está invocado en `ApplicationSetting.cs`.
- [ ] No hay PII en los logs (solo IDs, nunca nombres, emails o datos sensibles).
