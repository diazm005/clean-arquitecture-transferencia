# Sistema de Autorización Arify - Versión Optimizada

## Descripción

Sistema de autorización personalizado que valida scopes mediante headers HTTP (`x-scope`), optimizado para el consumo eficiente de recursos y siguiendo las mejores prácticas de ASP.NET Core.

## Características Principales

- **Validación por Header**: Utiliza el header `x-scope` en lugar de claims JWT
- **Configuración Centralizada**: Una sola puerta de entrada mediante `ConfigureArixAuthentication()`
- **Optimización de Recursos**: Servicios registrados como Scoped para mejor gestión de memoria
- **Logging Integrado**: Registro detallado de eventos de autorización
- **Sintaxis Simplificada**: Uso directo del sistema de autorización de ASP.NET Core

## Configuración

### 1. Registro en Program.cs

```csharp
// Configuración única y centralizada
builder.Services.ConfigureArixAuthentication(builder.Configuration);
```

### 2. Uso en Controladores

```csharp
[ApiController]
[Route("api/[controller]")]
public class ExampleController : ControllerBase
{
    // Endpoint protegido con scope de lectura
    [ArifyAuthorize("ReadExample")]
    [HttpGet]
    public IActionResult GetData()
    {
        return Ok("Data retrieved successfully");
    }

    // Endpoint protegido con scope de escritura
    [ArifyAuthorize("WriteExample")]
    [HttpPost]
    public IActionResult CreateData([FromBody] object data)
    {
        return Ok("Data created successfully");
    }
}
```

## Configuración de Mapeo de Políticas

### 1. Configuración por Defecto (Código)

El sistema incluye mapeos por defecto:

```csharp
"ReadExample" → "api/r:ex"
"WriteExample" → "api/w:ex"
```

### 2. Configuración desde appsettings.json (Recomendado)

Puedes sobrescribir los mapeos agregando la sección `ArifyScopes` en tu `appsettings.json`:

```json
{
  "ArifyScopes": {
    "PolicyMappings": {
      "ReadExample": "api/r:ex",
      "WriteExample": "api/w:ex",
      "AdminExample": "api/admin:ex",
      "DeleteExample": "api/d:ex"
    }
  }
}
```

### 3. Uso en Controladores

```csharp
[ApiController]
[Route("api/[controller]")]
public class ExampleController : ControllerBase
{
    // Requiere scope "api/r:ex" en el header x-scope
    [ArifyAuthorize("ReadExample")]
    [HttpGet]
    public IActionResult GetData()
    {
        return Ok("Data retrieved successfully");
    }

    // Requiere scope "api/w:ex" en el header x-scope
    [ArifyAuthorize("WriteExample")]
    [HttpPost]
    public IActionResult CreateData([FromBody] object data)
    {
        return Ok("Data created successfully");
    }
}
```

## Funcionamiento

### 1. Validación de Headers

El sistema busca el header `x-scope` en cada request:

```http
GET /api/example
Headers:
  x-scope: api/r:ex api/w:ex api/admin:ex
```

### 2. Mapeo y Comparación

1. El atributo `[ArifyAuthorize("ReadExample")]` se mapea a `"api/r:ex"`
2. El sistema divide los scopes del header por espacios
3. Compara (case-insensitive) con el scope mapeado
4. Si encuentra coincidencia, autoriza el acceso

### 3. Respuestas

- **Autorizado**: Continúa con la ejecución del endpoint
- **No autorizado**: Retorna 403 Forbidden
- **Header faltante**: Retorna 403 Forbidden
- **Política no mapeada**: Retorna 403 Forbidden (con log de warning)

## Optimizaciones Implementadas

### 1. Gestión de Memoria
- `IAuthorizationHandler` registrado como Scoped (no Singleton)
- Evita retención innecesaria de memoria entre requests

### 2. Performance
- Comparación de strings optimizada con `StringComparer.OrdinalIgnoreCase`
- Split con `StringSplitOptions.RemoveEmptyEntries` para evitar strings vacíos
- Validaciones tempranas para evitar procesamiento innecesario

### 3. Logging Estructurado
- Logs con información contextual para debugging
- Separación entre warnings y errores
- Información detallada sobre fallos de autorización

## Ejemplos de Uso

### Request Exitoso
```http
GET /service-domain-s/v1/example2-behavior-qualifier/validate
Headers:
  x-scope: api/r:ex api/w:ex
  x-device-identifier: device123
  x-message-identifier: msg456
  x-channel-identifier: web
```

### Request Fallido
```http
GET /service-domain-s/v1/example2-behavior-qualifier/validate
Headers:
  x-scope: api/w:ex  # Scope incorrecto (necesita api/r:ex)
  x-device-identifier: device123
```

## Arquitectura del Sistema

```
Request → ArifyAuthorizeAttribute → ArifyPolicy → ArifyScopeRequirementHandler
                                                          ↓
                                                   Validación x-scope
                                                          ↓
                                                   Success/Failure
```

## Ventajas sobre la Implementación Anterior

1. **Menos Código**: Eliminación del TypeFilterAttribute personalizado
2. **Mejor Performance**: Uso nativo del sistema de autorización de ASP.NET Core
3. **Más Mantenible**: Configuración centralizada y código más limpio
4. **Mejor Debugging**: Logging estructurado y detallado
5. **Escalable**: Fácil adición de nuevos scopes sin modificar código base

## Consideraciones de Seguridad

- Los scopes se validan en cada request
- No hay cache de autorización (validación en tiempo real)
- Headers pueden ser inspeccionados en logs para auditoría
- Sistema preparado para integración con sistemas de autenticación externos

## Migración desde Versión Anterior

1. Cambiar `ConfigureArifyAuthorizer()` por `ConfigureArixAuthentication()`
2. Los atributos `[ArifyAuthorize("scope")]` mantienen la misma sintaxis
3. Remover parámetros `x-scope` de los métodos del controlador (ya no necesarios)
4. El sistema maneja automáticamente la validación del header

## Troubleshooting

### Error: "No required scope specified"
- Verificar que el atributo `[ArifyAuthorize("scope")]` tenga un scope válido

### Error: "HttpContext is null"
- Verificar que `AddHttpContextAccessor()` esté registrado

### Error: "Authorization failed"
- Verificar que el header `x-scope` esté presente
- Verificar que el scope en el header coincida con el requerido
- Revisar logs para detalles específicos del fallo