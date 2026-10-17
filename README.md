# Clean Architecture: transferencias bancarias

Versión **1.0.0**. API educativa en .NET 10 con reglas de transferencia, adaptadores
simulados en memoria, auditoría y comprobaciones automatizadas. El OTP es de prueba;
los datos se reinician al cerrar la aplicación.

```powershell
dotnet build SystemApi3.0.slnx -m:1
dotnet run --project SystemAPI --launch-profile http --no-build
```

Swagger: http://localhost:5207/swagger.

```powershell
dotnet run --project Tests/Transferencias.Tests.csproj --no-build
```

## Documentación de la plantilla original

> **Taller de transferencias (.NET 10):** consulta [la guía de Clean Architecture](docs/TALLER-TRANSFERENCIAS.md)
> para ejecutar la solución, entender las capas y probar las reglas bancarias.
> El contenido siguiente corresponde a la plantilla original.

in MacOS or Linux dotnet run --project SystemAPI/SystemAPI.csproj
## Descripción

`AggregationPipelineQueryBuilder` es una clase utilitaria que facilita la construcción de pipelines de agregación de MongoDB de manera fluida y legible. Esta clase implementa el patrón Builder para crear consultas de agregación complejas de forma más intuitiva que escribir documentos BSON directamente.

## Características

- **Fluent API**: Permite encadenar métodos para construir pipelines complejos
- **Type Safety**: Utiliza `BsonDocument` para garantizar la corrección de la sintaxis
- **Legibilidad**: Hace que las consultas de agregación sean más fáciles de leer y mantener
- **Flexibilidad**: Soporta todos los operadores principales de agregación de MongoDB

## Health Check Endpoints

El sistema incluye endpoints de health check para balanceadores de carga y monitoreo:

### Endpoints Disponibles

| Endpoint | Método | Descripción | Código de Respuesta |
|----------|--------|-------------|-------------------|
| `/health` | GET | Health check básico con información del servicio | 200 (OK) |
| `/healthcheck` | GET | Alias del health check básico | 200 (OK) |
| `/health/detailed` | GET | Health check detallado con todos los checks registrados | 200 (OK) |
| `/ping` | GET | Endpoint ping/pong para testing de conectividad | 200 (OK) |
| `/health/ready` | GET | Sonda de readiness para Kubernetes/load balancers | 200 (OK) / 503 (Service Unavailable) |
| `/health/live` | GET | Sonda de liveness para Kubernetes/load balancers | 200 (OK) / 503 (Service Unavailable) |

### Ejemplos de Uso

```bash
# Health check básico
curl http://localhost:5000/health

# Health check detallado
curl http://localhost:5000/health/detailed

# Ping test
curl http://localhost:5000/ping

# Sonda de readiness
curl http://localhost:5000/health/ready

# Sonda de liveness
curl http://localhost:5000/health/live
```

### Respuesta de Health Check

```json
{
  "success": true,
  "data": {
    "status": "Healthy",
    "timestamp": "2024-01-15T10:30:00.000Z",
    "uptime": "2024-01-15T08:00:00.000Z",
    "version": "1.0.0",
    "environment": "Development"
  }
}
```

## Validación de Scopes

El sistema soporta dos tipos de validación de scopes:

### 1. Validación por JWT Claims

```csharp
[Authorize(Policy = "ReadScope")] // validate by JWT claim
```

Esta validación utiliza los claims del JWT token para verificar los permisos del usuario.

### 2. Validación por ArixScope Header

```csharp
[ArixAuthorize("WriteSetProf")] //Validate Scope by axscope HEADER
```

Esta validación utiliza el header `axscope` para validar los scopes específicos del usuario.

### Ejemplos de Implementación

```csharp
// Validación por JWT claim
[HttpGet]
[Authorize(Policy = "ReadScope")]
public IActionResult GetData()
{
    return Ok("Data accessible with ReadScope policy");
}

// Validación por ArixScope header
[HttpPost]
[ArixAuthorize("WriteSetProf")]
public IActionResult CreateProfile()
{
    return Ok("Profile created with WriteSetProf scope");
}

// Combinación de validaciones
[HttpPut]
[Authorize(Policy = "AdminScope")]
[ArixAuthorize("UpdateUser")]
public IActionResult UpdateUser()
{
    return Ok("User updated with both validations");
}
```

### Configuración de Scopes

Los scopes deben estar configurados en el sistema de autenticación y pueden incluir:

- **ReadScope**: Permisos de lectura
- **WriteScope**: Permisos de escritura
- **AdminScope**: Permisos administrativos
- **WriteSetProf**: Permisos específicos para perfiles
- **UpdateUser**: Permisos para actualizar usuarios

## Métodos Disponibles

### Match
Filtra documentos que coinciden con los criterios especificados.

```csharp
.Match(new BsonDocument
{
    {"adIdentity", adIdentity },
    {"status", true },
    {"authentication", true }
})
```

### Lookup
Realiza un join con otra colección (equivalente a LEFT JOIN en SQL).

```csharp
.Lookup("CoreProfiles", "_id", "idCoreProfiles", "profiles")
```

**Parámetros:**
- `joinWith`: Nombre de la colección a unir
- `foreignKey`: Campo en la colección destino
- `localKey`: Campo en la colección origen
- `asResult`: Nombre del campo resultante

### Unwind
Descompone un array en documentos individuales.

```csharp
.Unwind("profiles")
```

### Group
Agrupa documentos por un campo específico y aplica operaciones de agregación.

```csharp
.Group(new BsonDocument
{
    { "_id", "$scopes.menu" },
    { "Identity", new BsonDocument("$first", new BsonDocument("$toString", "$scopes._id")) },
    { "Scope", new BsonDocument("$first", "$scopes.scope") },
    { "Module", new BsonDocument("$first", "$module.module") }
})
```

### Sort
Ordena los documentos por campos específicos.

```csharp
.Sort(new BsonDocument("Menu", 1))  // 1 = ascendente, -1 = descendente
```

### Project
Selecciona y renombra campos en el resultado.

```csharp
.Project(new BsonDocument
{
    { "_id", 0 },           // Excluir campo
    { "Identity", 1 },      // Incluir campo
    { "Menu", "$_id" },     // Renombrar campo
    { "Scope", 1 }
})
```

### Skip
Omite un número específico de documentos.

```csharp
.Skip(10)  // Omite los primeros 10 documentos
```

### Limit
Limita el número de documentos en el resultado.

```csharp
.Limit(50)  // Máximo 50 documentos
```

### Count
Cuenta el número total de documentos.

```csharp
.Count("totalCount")
```

### Build
Finaliza la construcción del pipeline y retorna la lista de documentos BSON.

```csharp
.Build()
```

## Ejemplos Prácticos

### Ejemplo 1: Obtener Scopes de Usuario

```csharp
public async Task<IEnumerable<AuthEnvironmentEntity>?> GetScopeAsync(string adIdentity)
{
    var pipeline = new AggregationPipelineQueryBuilder()
        // 1. Filtrar usuarios activos y autenticados
        .Match(new BsonDocument
        {
            {"adIdentity", adIdentity },
            {"status", true },
            {"authentication", true }
        })
        // 2. Unir con perfiles
        .Lookup("CoreProfiles", "_id", "idCoreProfiles", "profiles")
        .Unwind("profiles")
        .Match(new BsonDocument("profiles.status", true))
        // 3. Unir con scopes
        .Lookup("CoreScopes", "idCoreProfiles", "profiles._id", "scopes")
        .Unwind("scopes")
        .Match(new BsonDocument("scopes.status", true))
        // 4. Unir con módulos
        .Lookup("CoreModules", "_id", "scopes.idCoreModule", "module")
        .Unwind("module")
        // 5. Agrupar por menú
        .Group(new BsonDocument
        {
            { "_id", "$scopes.menu" },
            { "Identity", new BsonDocument("$first", new BsonDocument("$toString", "$scopes._id")) },
            { "Scope", new BsonDocument("$first", "$scopes.scope") },
            { "Module", new BsonDocument("$first", "$module.module") },
            { "Name", new BsonDocument("$first", "$module.displayName") },
            { "Version", new BsonDocument("$first", "$scopes.version") },
            { "Display", new BsonDocument("$first", "$scopes.display") },
            { "Description", new BsonDocument("$first", "$scopes.description") },
            { "Uri", new BsonDocument("$first", "$scopes.uri") },
            { "Status", new BsonDocument("$first", "$scopes.status") }
        })
        // 6. Ordenar por menú
        .Sort(new BsonDocument("Menu", 1))
        // 7. Proyectar campos finales
        .Project(new BsonDocument
        {
            { "_id", 0 },
            { "Identity", 1 },
            { "Menu", "$_id" },
            { "Scope", 1 },
            { "Module", 1 },
            { "Name", 1 },
            { "Version", 1 },
            { "Display", 1 },
            { "Description", 1 },
            { "Uri", 1 },
            { "Status", 1 }
        })
        .Build();

    var result = await _collCoreUser.Aggregate<AuthEnvironmentEntity>(pipeline).ToListAsync();
    return result?.Any() == true ? result : null;
}
```

### Ejemplo 2: Obtener Lista de Scopes

```csharp
public async Task<IEnumerable<string>> GetUserScopeAsync(string adIdentity)
{
    var pipeline = new AggregationPipelineQueryBuilder()
        // Filtros iniciales
        .Match(new BsonDocument
        {
            {"adIdentity", adIdentity },
            {"status", true },
            {"authentication", true }
        })
        // Joins con perfiles y scopes
        .Lookup("CoreProfiles", "_id", "idCoreProfiles", "profiles")
        .Unwind("profiles")
        .Match(new BsonDocument("profiles.status", true))
        .Lookup("CoreScopes", "idCoreProfiles", "profiles._id", "scopes")
        .Unwind("scopes")
        .Match(new BsonDocument("scopes.status", true))
        .Lookup("CoreModules", "_id", "scopes.idCoreModule", "module")
        .Unwind("module")
        // Agrupar por menú y obtener scope
        .Group(new BsonDocument
        {
            { "_id", "$scopes.menu" },
            { "Scope", new BsonDocument("$first", "$scopes.scope") },
        })
        // Proyectar solo el scope
        .Project(new BsonDocument
        {
            { "_id", 0 },
            { "Scope", 1 }
        })
        .Build();

    var results = await _collCoreUser.Aggregate<BsonDocument>(pipeline).ToListAsync();
    return results.Select(doc => doc["Scope"].AsString);
}
```

## Uso Básico

```csharp
// Crear una instancia del builder
var pipeline = new AggregationPipelineQueryBuilder();

// Construir el pipeline paso a paso
pipeline
    .Match(new BsonDocument { { "status", true } })
    .Sort(new BsonDocument { { "createdAt", -1 } })
    .Limit(10)
    .Project(new BsonDocument
    {
        { "_id", 1 },
        { "name", 1 },
        { "email", 1 }
    });

// Obtener el pipeline final
var finalPipeline = pipeline.Build();

// Ejecutar la consulta
var results = await collection.Aggregate<YourEntity>(finalPipeline).ToListAsync();
```

## Ventajas del AggregationPipelineQueryBuilder

1. **Legibilidad**: El código es más fácil de leer y entender que los documentos BSON raw
2. **Mantenibilidad**: Cambios en el pipeline son más fáciles de realizar
3. **Reutilización**: Pipelines complejos pueden ser reutilizados
4. **Debugging**: Es más fácil identificar problemas en cada etapa del pipeline
5. **Type Safety**: Menos errores de sintaxis gracias al uso de `BsonDocument`

## Consideraciones

- La clase es `internal`, por lo que solo es accesible dentro del mismo assembly
- Todos los métodos retornan `this` para permitir method chaining
- El método `Build()` debe ser llamado al final para obtener el pipeline final
- Los documentos BSON deben seguir la sintaxis correcta de MongoDB

## Dependencias

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
```

## Notas de Implementación

- La clase mantiene una lista interna de documentos BSON que representan cada etapa del pipeline
- Cada método agrega un nuevo documento BSON a la lista
- El método `Build()` retorna la lista completa para ser utilizada con `IMongoCollection.Aggregate()`
