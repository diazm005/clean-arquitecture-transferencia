# Sistema de Autorización Arify

## Descripción

El sistema de autorización Arify proporciona una alternativa a los scopes tradicionales de JWT, utilizando headers HTTP personalizados para la validación de permisos. Este sistema es especialmente útil cuando necesitas un control de acceso granular basado en headers específicos.

## Características Principales

- ✅ **Validación basada en headers**: Utiliza el header `axscope` para determinar permisos
- ✅ **Configuración flexible**: Políticas configurables desde `appsettings.json`
- ✅ **Logging de auditoría**: Registro detallado de accesos y fallos de autorización
- ✅ **Extensible**: Fácil agregar nuevas políticas en tiempo de ejecución
- ✅ **Compatibilidad**: Funciona junto con JWT tradicional
- ✅ **Rendimiento**: Validación rápida sin consultas a base de datos

## Configuración

### 1. Configuración en appsettings.json

```json
{
  "ArifyAuthorization": {
    "ScopeHeaderName": "axscope",
    "ScopeSeparator": " ",
    "CaseSensitiveScopes": false,
    "EnableDetailedLogging": true,
    "Policies": {
      "ReadEvaluate": "set:usr/r",
      "WriteEvaluate": "set:usr/w",
      "UpdateEvaluate": "set:usr/u",
      "DeleteEvaluate": "set:usr/d",
      "AdminEvaluate": "set:admin"
    }
  }
}
```

### 2. Configuración en Program.cs

```csharp
// Configurar autenticación Arify
builder.Services.ConfigureArifyAuthentication(builder.Configuration);

// Opcional: Agregar políticas adicionales
builder.Services.AddArifyPolicy("CustomPolicy", "custom:scope");

// Opcional: Middleware de auditoría
app.UseArifyAudit();
```

## Uso en Controladores

### Ejemplo Básico

```csharp
[ApiController]
[Route("api/[controller]")]
public class ExampleController : ControllerBase
{
    [ArifyAuthorize("ReadEvaluate")]
    [HttpGet]
    public IActionResult GetData()
    {
        return Ok("Data retrieved successfully");
    }

    [ArifyAuthorize("WriteEvaluate")]
    [HttpPost]
    public IActionResult CreateData([FromBody] object data)
    {
        return Ok("Data created successfully");
    }
}
```

### Comparación con JWT Tradicional

```csharp
public class ComparisonController : ControllerBase
{
    // Autorización JWT tradicional
    [Authorize(Policy = "ReadScope")]
    [HttpGet("jwt-protected")]
    public IActionResult JwtProtected() => Ok();

    // Autorización Arify (basada en header)
    [ArifyAuthorize("ReadEvaluate")]
    [HttpGet("arify-protected")]
    public IActionResult ArifyProtected() => Ok();
}
```

## Formato del Header

### Estructura del Header `axscope`

```
axscope: set:usr/r set:usr/w set:admin
```

- **Separador**: Espacio (configurable)
- **Formato**: Libre (recomendado: `namespace:resource/action`)
- **Ejemplos**:
  - `set:usr/r` - Lectura de usuarios
  - `set:usr/w` - Escritura de usuarios
  - `set:admin` - Permisos de administrador

### Ejemplos de Solicitudes HTTP

```bash
# Solicitud con permisos de lectura
curl -H "axscope: set:usr/r" \
     -H "Content-Type: application/json" \
     https://api.example.com/api/data

# Solicitud con múltiples permisos
curl -H "axscope: set:usr/r set:usr/w set:admin" \
     -H "Content-Type: application/json" \
     https://api.example.com/api/admin/users
```

## Extensiones Útiles

### Verificación Manual de Scopes

```csharp
public class CustomController : ControllerBase
{
    [HttpGet]
    public IActionResult CheckScopes()
    {
        // Obtener scopes del contexto actual
        var scopes = HttpContext.GetArifyScopes();
        
        // Verificar scope específico
        if (HttpContext.HasArifyScope("set:usr/r"))
        {
            return Ok("Has read permission");
        }
        
        // Verificar múltiples scopes (cualquiera)
        if (HttpContext.HasAnyArifyScope(new[] { "set:admin", "set:usr/w" }))
        {
            return Ok("Has admin or write permission");
        }
        
        return Forbid();
    }
}
```

### Políticas Dinámicas

```csharp
// En Program.cs o durante la configuración
builder.Services.AddArifyPolicies(new Dictionary<string, string>
{
    { "CustomRead", "custom:read" },
    { "CustomWrite", "custom:write" },
    { "SuperAdmin", "super:admin" }
});
```

## Logging y Auditoría

### Configuración de Logging

```json
{
  "Logging": {
    "LogLevel": {
      "SystemAPI.Handlers.Authorizers": "Information"
    }
  }
}
```

### Ejemplos de Logs

```
[Information] Authorization succeeded for policy 'ReadEvaluate'. Path: /api/data
[Warning] Authorization failed for policy 'WriteEvaluate'. IP: 192.168.1.100, UserAgent: Mozilla/5.0...
[Information] Arify Request Started - RequestId: abc123, Method: GET, Path: /api/data, Scopes: [set:usr/r, set:usr/w]
```

## Mejores Prácticas

### 1. Nomenclatura de Scopes

```csharp
// ✅ Recomendado: Estructura jerárquica
"namespace:resource/action"
"set:usr/r"     // Sistema:Usuario/Lectura
"set:usr/w"     // Sistema:Usuario/Escritura
"billing:inv/r" // Facturación:Factura/Lectura

// ❌ Evitar: Nombres genéricos
"read"
"write"
"admin"
```

### 2. Configuración de Políticas

```csharp
// ✅ Políticas específicas y descriptivas
services.AddArifyPolicy("UserRead", "set:usr/r");
services.AddArifyPolicy("UserWrite", "set:usr/w");
services.AddArifyPolicy("BillingAdmin", "billing:admin");

// ❌ Políticas genéricas
services.AddArifyPolicy("Read", "read");
services.AddArifyPolicy("Write", "write");
```

### 3. Manejo de Errores

```csharp
[ArifyAuthorize("ReadEvaluate")]
[HttpGet]
public async Task<IActionResult> GetData()
{
    try
    {
        // Lógica del endpoint
        return Ok(data);
    }
    catch (UnauthorizedAccessException)
    {
        // El sistema Arify ya manejó la autorización
        // Este catch es para lógica de negocio adicional
        return Forbid("Additional business rule failed");
    }
}
```

## Troubleshooting

### Problemas Comunes

1. **403 Forbidden sin logs**
   - Verificar que el header `axscope` esté presente
   - Confirmar que el scope requerido coincida exactamente

2. **Políticas no encontradas**
   - Verificar configuración en `appsettings.json`
   - Confirmar que `ConfigureArifyAuthentication` se llame antes de `AddAuthorization`

3. **Logs no aparecen**
   - Verificar nivel de logging en configuración
   - Habilitar `EnableDetailedLogging` en opciones

### Debugging

```csharp
// Agregar logging temporal para debugging
[HttpGet("debug")]
public IActionResult Debug()
{
    var scopes = HttpContext.GetArifyScopes();
    var hasScope = HttpContext.HasArifyScope("set:usr/r");
    
    return Ok(new { 
        Scopes = scopes, 
        HasReadScope = hasScope,
        Headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())
    });
}
```

## Migración desde JWT

### Paso 1: Configuración Paralela

```csharp
// Mantener ambos sistemas durante la transición
builder.Services.ConfigureJwtAuthentication(builder.Configuration);
builder.Services.ConfigureArifyAuthentication(builder.Configuration);
```

### Paso 2: Migración Gradual

```csharp
public class MigrationController : ControllerBase
{
    // Endpoint con JWT (existente)
    [Authorize(Policy = "ReadScope")]
    [HttpGet("old-endpoint")]
    public IActionResult OldEndpoint() => Ok();

    // Endpoint con Arify (nuevo)
    [ArifyAuthorize("ReadEvaluate")]
    [HttpGet("new-endpoint")]
    public IActionResult NewEndpoint() => Ok();
    
    // Endpoint con ambos (transición)
    [Authorize(Policy = "ReadScope")]
    [ArifyAuthorize("ReadEvaluate")]
    [HttpGet("hybrid-endpoint")]
    public IActionResult HybridEndpoint() => Ok();
}
```

## Consideraciones de Seguridad

1. **Validación del Header**: El sistema valida automáticamente la presencia y formato del header
2. **Case Sensitivity**: Configurable para mayor flexibilidad
3. **Logging de Auditoría**: Registro completo de intentos de acceso
4. **Separación de Responsabilidades**: Autorización separada de autenticación
5. **Fallback Policies**: Políticas por defecto para casos no cubiertos

## Rendimiento

- **Validación O(1)**: Búsqueda directa en array de scopes
- **Sin consultas DB**: Toda la validación es en memoria
- **Caching**: Las políticas se cachean automáticamente
- **Overhead mínimo**: ~1-2ms por solicitud