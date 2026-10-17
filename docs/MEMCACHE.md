# Guía de uso para el equipo de desarrollo — Librería de caché en C#

## 1. Objetivo

Este documento describe **cómo consumir la librería de caché** desde los casos de uso en C#, tomando como referencia los escenarios funcionales principales definidos para el proyecto.

La intención es estandarizar:

- el uso de estrategias de caché,
- la construcción de claves,
- la elección de la infraestrutura de caché,
- y la forma de integrar caché en los casos de uso sin contaminar la lógica de negocio.
- Asi mismo, debe tenerse en cuenta los lineamientos de tablas en Bases de datos en memoria (MEMCACHEDBTABLE.md).

---

## 2. Convenciones generales

### 2.1 Principios de consumo

Al usar la librería, el equipo debe seguir estas reglas:

- Toda operación cacheable debe declarar un **identificador explícita**.
- La clave debe ser **determinística** y hacer referencia a los campos clave de la tabla caheada (Ver lineamientos técnicos para implementación de caché)
- La estrategia debe quedar **visible en el punto de uso**.
- La operación origen debe pasarse como **función diferida**.
- El caso de uso sigue siendo responsable de:
  - validación,
  - orquestación,
  - composición de respuesta,
  - y contrato final (`EasyResult<T>`).

### 2.2 Convención de claves

Se recomienda usar el siguiente formato:

```txt
{microservice}:{method}:{channel|reference|group}:{identificador}
```

Ejemplos:

```txt
payment-management:cfg:11:13260221
payment-management:trx:11:17260228
saving-managamente:get:11:1761DF197400-00115
saving-managamente:post:11:176DF1197400-01016
saving-managamente:put:11:176DGH1197400-0301
payment-management:cfg:11:currencies
get-11-1761197400-001
```

### 2.3 Estrategias disponibles

```csharp
public enum CacheStrategy
{
    CacheOnly,
    CacheThenSource,
    CacheThenSourceAndStore,
    SourceAndStore,
    StoreOnly
}
```

Resumen funcional:

| Estrategia | Lee caché | Ejecuta método origen | Guarda resultado |
|---|---:|---:|---:|
| CacheOnly | Sí | No | No |
| CacheThenSource | Sí | Sí, si no existe | No |
| CacheThenSourceAndStore | Sí | Sí, si no existe | Sí |
| SourceAndStore | No | Sí | Sí |
| StoreOnly | No | No | Sí |

---

## 3. Caso 1 — Leer únicamente desde caché

### Objetivo

Resolver el valor **solo desde caché**.  
Si no existe, **no debe ejecutarse** la operación al método origen.

### Cuándo usarlo

Usar esta estrategia cuando:

- el dato ya fue precargado por otro proceso,
- no se quiere golpear al método origen,
- el caché es la única fuente temporal esperada,
- se trata de catálogos precargados, snapshots o llamadas previas a APIs.
- Ideal para validar idempotencia de las APIs

### Ejemplo

```csharp
public async Task<EasyResult<List<CurrencyAdapter>>> GetCurrenciesAsync(CancellationToken ct = default)
{
    var currencies = await _cache
        .ForKey("payment-management:cfg:11:currencies")
        .UseStrategy(CacheStrategy.CacheOnly)
        .UseProvider(_cacheProvider) // CacheProvider Implements Interface ICacheInfrastructure can be Redis, Dymano, MemoryCache
        .ResolveAsync<List<CurrencyAdapter>>(ct);

    if (currencies is null)
    {
        return EasyResult<List<CurrencyAdapter>>.Failure(404, ["No se encontró el catálogo en caché"]);
    }

    return EasyResult<List<CurrencyAdapter>>.Success(currencies);
}
```

### Comportamiento esperado

- Se busca `payment-management:cfg:11:currencies` en Redis.
- Si existe, se devuelve el valor.
- Si no existe, se devuelve `404` controlado.
- No se ejecuta ninguna consulta adicional.

### Consideraciones

- No usar esta estrategia si el caso de uso requiere fallback obligatorio al método origen.
- Es ideal para datos precargados y de lectura intensiva.
- Si el dato no existe en caché, el consumidor debe manejar explícitamente la ausencia.

---

## 4. Caso 2 — Leer desde caché y, si no existe, ejecutar la operación original

### Objetivo

Intentar resolver desde caché y, en caso de miss, consultar al método origen.  
El resultado obtenido desde origen **no debe persistirse** posteriormente.

### Cuándo usarlo

Usar esta estrategia cuando:

- el dato cambia muy rápido,
- no conviene poblar caché con nuevos resultados,
- pero sí se quiere aprovechar un hit si ya existía.

### Ejemplo

```csharp
public async Task<EasyResult<ProductStockAdapter>> GetProductStockAsync(int productId, CancellationToken ct = default)
{
    var stock = await _cache
        .ForKey($"payment-management:trx:11:{productId}")
        .UseStrategy(CacheStrategy.CacheThenSource)
        .UseProvider(_cacheProvider) // CacheProvider Implements Interface ICacheInfrastructure can be Redis, Dymano, MemoryCache
        .ResolveAsync(
            token => _inventoryQuery.GetStockAsync(productId, token), ct
        );

    if (stock is null)
    {
        return EasyResult<ProductStockAdapter>.Failure(404, ["No se encontró stock"]);
    }

    return EasyResult<ProductStockAdapter>.Success(stock);
}
```

### Comportamiento esperado

- Se intenta leer `payment-management:trx:11:{productId}` desde Redis.
- Si existe, se devuelve el valor cacheado.
- Si no existe, se ejecuta `_inventoryQuery.GetStockAsync(...)`.
- El resultado del método origen **no se almacena** en caché.

### Consideraciones

- Recomendado para datos volátiles.
- Evita crecimiento innecesario del caché.
- No usar esta estrategia cuando el objetivo sea “aprender” y poblar caché en cada miss.

---

## 5. Caso 3 — Leer desde caché, ejecutar la operación original y almacenar el resultado

### Objetivo

Aplicar la estrategia estándar de caché:

1. leer desde caché,
2. si no existe, ejecutar el método origen,
3. almacenar el resultado,
4. devolver el valor.

### Cuándo usarlo

Usar esta estrategia cuando:

- el dato se consulta con frecuencia,
- el método origen tiene costo relevante,
- existe tolerancia a cierta expiración,
- se quiere mejorar tiempos de respuesta en lecturas repetidas.

### Ejemplo

```csharp
public async Task<EasyResult<CustomerAdapter>> GetCustomerAsync(string customerId, CancellationToken ct = default)
{
    var customer = await _cache
        .ForKey($"payment-management:cfg:11:{customerId}")
        .UseStrategy(CacheStrategy.CacheThenSourceAndStore)
        .UseProvider(_cacheProvider) // CacheProvider Implements Interface ICacheInfrastructure can be Redis, Dymano, MemoryCache
        .WithTtl(TimeSpan.FromMinutes(10))
        .ResolveAsync(
            token => _customerQuery.GetByIdAsync(customerId, token), ct
        );

    if (customer is null)
    {
        return EasyResult<CustomerAdapter>.Failure(404, ["Cliente no encontrado"]);
    }

    return EasyResult<CustomerAdapter>.Success(customer);
}
```

### Comportamiento esperado

- Se intenta leer `payment-management:cfg:11:{customerId}` desde Redis.
- Si existe, se devuelve el valor cacheado.
- Si no existe, se ejecuta `_customerQuery.GetByIdAsync(...)`.
- El valor resultante se almacena en caché por 10 minutos.
- Luego se devuelve el dato al consumidor.

### Consideraciones

- Esta debería ser la estrategia más común en consultas reutilizables.
- El TTL debe definirse según la volatilidad del dato.
- No usar TTL arbitrarios; documentar el motivo funcional.

---

## 6. Caso 4 — Ejecutar la operación original y almacenar el resultado directamente en caché

### Objetivo

Forzar la ejecución del origen, omitiendo cualquier lectura previa de caché, y almacenar el resultado obtenido.

### Cuándo usarlo

Usar esta estrategia cuando:

- se requiere refresh forzado,
- se desea recalentar caché,
- se quiere corregir o rehidratar el valor almacenado,
- el valor actual de caché no debe reutilizarse.

### Ejemplo

```csharp
public async Task<EasyResult<CompanySettingsAdapter>> RefreshCompanySettingsAsync(string companyCode, CancellationToken ct = default)
{
    var settings = await _cache
        .ForKey($"payment-management:trx:11:{companyCode}")
        .UseStrategy(CacheStrategy.SourceAndStore)
        .UseProvider(_cacheProvider) // CacheProvider Implements Interface ICacheInfrastructure can be Redis, Dymano, MemoryCache
        .WithTtl(TimeSpan.FromMinutes(30))
        .ResolveAsync(
            token => _settingsQuery.GetCompanySettingsAsync(companyCode, token), ct
        );

    if (settings is null)
    {
        return EasyResult<CompanySettingsAdapter>.Failure(404, ["Configuración no encontrada"]);
    }

    return EasyResult<CompanySettingsAdapter>.Success(settings);
}
```

### Comportamiento esperado

- No se consulta caché previamente.
- Se ejecuta `_settingsQuery.GetCompanySettingsAsync(...)`.
- El resultado se guarda en `payment-management:trx:11:{companyCode}` por 30 minutos.
- Se devuelve el valor recién obtenido.

### Consideraciones

- Recomendado para endpoints de refresh o recarga controlada.
- No debe usarse por defecto si existe posibilidad de reutilizar caché válido.
- Documentar siempre por qué se fuerza la recarga.

---

## 7. Caso 5 — Almacenar el resultado directamente en caché

### Objetivo

Guardar un valor en caché sin hacer lectura previa ni invocar una operación origen.

### Cuándo usarlo

Usar esta estrategia cuando:

- el valor ya fue calculado por otro flujo,
- se quiere precargar caché,
- se desea persistir un agregado ya construido,
- se reciben eventos que requieren poblar caché.

### Ejemplo

```csharp
public async Task CacheDashboardAsync(string customerId, DashboardSummaryAdapter summary, CancellationToken ct = default)
{
    await _cache
        .ForKey($"saving-managament:get:11:{messageId}")
        .UseProvider(_cacheProvider) // CacheProvider Implements Interface ICacheInfrastructure can be Redis, Dymano, MemoryCache
        .WithTtl(TimeSpan.FromMinutes(5))
        .PutAsync(summary, ct);
}
```

### Comportamiento esperado

- Se escribe `saving-managament:get:11:{messageId}` en Redis.
- El valor `messageId` queda disponible por 5 minutos.
- No hay lectura previa.
- No se ejecuta ninguna llamada al origen.

### Consideraciones

- Este patrón sirve para warm-up, precarga o persistencia explícita.
- Es útil cuando el valor ya está disponible y no tiene sentido recalcularlo.
- Debe cuidarse que la clave represente correctamente el contenido almacenado.
- Ideal para hacer idempotente a las APIs

---

## 8. Ejemplo completo — Uso combinado en `ShowExampleAsync`

### Objetivo

Mostrar cómo combinar múltiples llamadas cacheables dentro de un mismo caso de uso, usando estrategias y proveedores distintos.

### Ejemplo

```csharp
public async Task<EasyResult<RetrieveExampleAdapter>> ShowExampleAsync(TraceIdentifierAdapter header, CancellationToken ct = default)
{
    var arrValided = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
    if (arrValided.Any())
    {
        return EasyResult<RetrieveExampleAdapter>.Failure(422, arrValided);
    }

    var titleId = Random.Shared.Next(1, 14);
    var productId = Random.Shared.Next(1, 14);

    var taskApiResult = _cache
        .ForKey($"example:trx:11:{titleId}")
        .UseStrategy(CacheStrategy.CacheThenSourceAndStore)
        .UseProvider(_cacheProvider) // CacheProvider Implements Interface ICacheInfrastructure can be Redis, Dymano, MemoryCache
        .WithTtl(TimeSpan.FromMinutes(5))
        .ResolveAsync(
            token => _fakeApiQuery.GetAsync(titleId, token), ct
        );

    var taskApiTwoResult = _cache
        .ForKey($"example:cfg:11:{productId}")
        .UseStrategy(CacheStrategy.CacheThenSource)
        .UseProvider(CacheProvider.Memory)
        .ResolveAsync(
            token => _fakeApiQuery.GetProductAsync(productId, token),ct
        );

    await Task.WhenAll(taskApiResult, taskApiTwoResult);

    var apiResult = await taskApiResult;
    var apiTwoResult = await taskApiTwoResult;

    var result = new RetrieveExampleAdapter
    {
        Ping = apiTwoResult.Title,
        Pong = apiResult.Title
    };

    return EasyResult<RetrieveExampleAdapter>.Success(result);
}
```

### Lectura funcional del ejemplo

Este caso de uso hace lo siguiente:

- valida el encabezado de entrada,
- obtiene dos identificadores,
- resuelve dos consultas independientes en paralelo,
- aplica una política distinta por consulta,
- compone una respuesta final.

#### Primera consulta

```csharp
_cache
    .ForKey($"example:trx:11:{titleId}")
    .UseStrategy(CacheStrategy.CacheThenSourceAndStore)
    .UseProvider(_cacheProvider) // CacheProvider Implements Interface ICacheInfrastructure can be Redis, Dymano, MemoryCache
```

Comportamiento:

- intenta leer desde Redis,
- si no existe, consulta al origen,
- almacena el resultado por 5 minutos.

#### Segunda consulta

```csharp
_cache
    .ForKey($"example:cfg:v1:{productId}")
    .UseStrategy(CacheStrategy.CacheThenSource)
    .UseProvider(CacheProvider.Memory)
```

Comportamiento:

- intenta leer desde caché en memoria,
- si no existe, consulta al origen,
- no almacena el resultado posteriormente.

### Qué demuestra este ejemplo

- La librería permite definir políticas **por llamada**.
- Un mismo caso de uso puede combinar Redis y MemoryCache.
- La lógica del negocio sigue limpia.
- El equipo puede leer claramente qué hace cada estrategia sin revisar implementación interna.

---

## 9. Recomendaciones para el equipo

### 9.1 Cuándo elegir cada estrategia

**CacheOnly**
- catálogos precargados,
- snapshots,
- datasets que no deben consultar origen.
- idempotencia de servicios

**CacheThenSource**
- datos volátiles,
- escenarios donde solo se aprovecha el hit si ya existe.

**CacheThenSourceAndStore**
- consultas frecuentes,
- integraciones costosas,
- datos con expiración controlada.

**SourceAndStore**
- refresh,
- rehidratación,
- recarga forzada.

**StoreOnly**
- precarga,
- warm-up,
- persistencia explícita de resultados ya construidos.
- responses precalculados

### 9.2 Qué evitar

- No ocultar la estrategia.
- No construir claves ambiguas.
- No usar caché en comandos de escritura por defecto.
- No cachear cualquier cosa “por rendimiento” sin justificar el caso.
- No mezclar datos distintos bajo la misma clave.
- No usar las bases de los linemientos de estructuración de tablas en bases de datos en Memoria

### 9.3 Qué documentar en cada uso

En cada integración con la librería, documentar al menos:

- qué estrategia se eligió,
- qué clave se usará,
- cuál es el proveedor,
- cuál es el TTL,
- por qué esa política tiene sentido para ese caso.

---

## 10. Resumen final

La librería debe ser consumida como una capacidad explícita de la capa de dominio.

Cada desarrollador debe poder identificar rápidamente:

- qué dato se cachea,
- bajo qué clave,
- con qué estrategia,
- en qué proveedor,
- y con qué tiempo de vida.

Los ejemplos de este documento representan los cinco escenarios funcionales base que el equipo debe conocer y aplicar de forma consistente.

---

###### By Arify Labs


## 11. Diseño recomendado para implementación (referencia documento ./MEMCACHEDBTABLE.md)

Para que todo lo descrito en esta guía sea consistente y portable entre `Redis`, `DynamoDB` y `MemoryCache`, la implementación debe dejar una regla innegociable:

- los providers son tontos,
- la librería es la que hace la magia.

Eso significa que los providers no deben tomar decisiones de negocio, no deben aplicar estrategias, no deben interpretar filtros y no deben conocer casos de uso. Su única responsabilidad es almacenar y recuperar información por una clave exacta.

El `builder` usado en los ejemplos (`ForKey(...).UseStrategy(...).UseProvider(...).ResolveAsync<T>(...)`) es genérico en `T`, por lo tanto el contrato principal del proveedor también debe ser genérico en `T` y no depender de una estructura cerrada como `ChannelCacheRecord`.

### 11.1 Regla principal de arquitectura

No mezclar en la misma interfaz:

- CRUD de almacenamiento,
- estrategias de caché,
- composición de claves,
- consultas por atributos de negocio,
- y decisiones funcionales de idempotencia o flujo.

La librería debe exponer un único contrato de almacenamiento para todos los providers:

1. `ICacheInfrastructure`
   - Resuelve exclusivamente almacenamiento por clave exacta.
   - Trabaja con valor genérico `T`.

Todo lo demás lo resuelve la librería.

### 11.2 Contrato del provider para el builder

Este es el contrato mínimo recomendado para soportar todos los casos funcionales definidos arriba:

```csharp
public sealed class CacheMetadataEntity
{
    public string? Method { get; init; }
    public int? ChannelId { get; init; }
    public string? Status { get; init; }
    public string? Microservice { get; init; }
}

public interface ICacheInfrastructure
{
    Task<T?> GetAsync<T>(string messageId, CacheMetadataEntity? filters, CancellationToken cancellationToken);

    Task SetAsync<T>(string messageId, T? value, TimeSpan? ttl, CacheMetadataEntity metadata, CancellationToken cancellationToken);

    Task UpdateAsync<T>(string messageId, T value, CacheMetadataEntity metadata, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string messageId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken);
}
```

Reglas de este contrato:

- `MessageId` es la primary key del registro y debe tratarse como tal.
- `T` representa exclusivamente `CachedData`, es decir, el dato real que se quiere cachear.
- `CacheMetadataEntity` representa información descriptiva adicional del registro.
- `CreatedAt` y `ExpireIn` son campos técnicos para persistencia y expiración.
- `ttl` es el único dato de expiración que la librería envía al provider.
- El provider no recibe decisiones de estrategia.
- No incluir `MergeAsync` como contrato base; su semántica cambia según el tipo de dato y el backend.
- El provider no debe saber si la operación corresponde a `CacheOnly`, `StoreOnly` o `CacheThenSourceAndStore`; eso lo decide la librería.
- En `GetAsync`, `filters` no cambian la búsqueda física: el provider primero busca por `messageId` y luego valida los campos enviados en `filters`.
- Si la validación adicional falla, `GetAsync` debe retornar `null`.
- En `SetAsync` y `UpdateAsync`, el provider persiste `value` junto con `metadata` y calcula internamente `CreatedAt` y `ExpireIn`.

Ejemplo de uso:

```csharp
await _provider.SetAsync(
    "update-11-1761199800-0002",
    payload,
    TimeSpan.FromMinutes(20),
    new CacheMetadataEntity
    {
        Method = "update",
        ChannelId = 11,
        Status = "created",
        Microservice = "ms-pagos"
    },
    cancellationToken);
```

### 11.3 Qué resuelve cada estrategia con este contrato

Con el contrato anterior, el builder puede implementar sin ambigüedad:

- `CacheOnly`: usa `GetAsync<T>`.
- `CacheThenSource`: usa `GetAsync<T>` y, si no existe, ejecuta origen sin persistir.
- `CacheThenSourceAndStore`: usa `GetAsync<T>` y luego `SetAsync<T>`.
- `SourceAndStore`: ejecuta origen y luego `SetAsync<T>`.
- `StoreOnly`: usa `SetAsync<T>` directamente.

### 11.4 Restricción importante sobre wildcard `*`

El patrón `*` no debe formar parte del contrato base de `ICacheInfrastructure`.

Motivo:

- `Redis` puede soportar patrones, pero con costos y restricciones operativas.
- `DynamoDB` no funciona como caché por wildcard; requiere índices y un modelo de consulta explícito.
- `MemoryCache` no tiene búsqueda por patrón de forma nativa.

Por lo tanto:

- `ForKey(...)` debe recibir una clave exacta para el contrato genérico.
- Para registros de canal, la librería puede usar directamente `MessageId` como clave exacta.
- Si un caso requiere además asegurar `method`, `status`, `channelId` o `microservice`, esos valores pueden enviarse en `filters`.
- Esa validación adicional no convierte al provider en un motor de consultas; sigue buscando por `MessageId` y luego compara el registro recuperado.
- Si el equipo decide mantener ejemplos con `*`, deben entenderse como una abstracción funcional, no como un comportamiento obligatorio para todos los providers.

### 11.5 Registros de flujo o idempotencia

Cuando se necesite persistir el ciclo de vida de un mensaje, una transición de estado o una política de idempotencia, debe usarse la misma `ICacheInfrastructure`. No se necesita una segunda interfaz.

```csharp
public sealed class ChannelCacheRecord<T>
{
    public required string MessageId { get; init; }
    public T? CachedData { get; init; }
    public CacheMetadataEntity? Metadata { get; init; }// A criterio, pero puede ser plano de cara al base de datos
    public long CreatedAt { get; init; }
    public long ExpireIn { get; init; }
}
```

Reglas para este caso:

- Se usa la misma `ICacheInfrastructure`.
- `MessageId` identifica de forma única el registro.
- `T` es únicamente el dato funcional que se guarda en `CachedData`.
- `Metadata` agrega contexto descriptivo del registro sin mezclarse con `CachedData`.
- `CreatedAt` y `ExpireIn` representan control técnico para expiración y se calculan a partir del `ttl`.
- La implementación puede vivir sobre `DynamoDB`, `Redis`, `MemoryCache` o cualquier storage apropiado.
- El provider no debe exponer métodos como `GetByMessageIdAsync`, `FindByMethodAsync` o similares.
- Si la librería necesita validar idempotencia adicional por `Method`, `Status`, `ChannelId` o `Microservice`, puede apoyarse en `filters` durante `GetAsync`.

### 11.6 Ejemplo de registro persistido

```json
{
  "messageId": "update-11-1761199800-0002",
  "cachedData": {
    "paymentId": "PAY-783423",
    "idempotencyKey": "client-001-20260223-002",
    "transitionFrom": "PENDING",
    "transitionTo": "CONFIRMED",
    "reason": "Manual approval",
    "updatedAt": 1761199800,
    "updatedBy": "operator-user-123"
  },
  "metadata": {
    "method": "update",
    "channelId": 11,
    "status": "created",
    "microservice": "ms-pagos"
  },
  "createdAt": 1761199800,
  "expireIn": 1761201000
}
```

Lectura con validación adicional:

```csharp
var cachedData = await _provider.GetAsync<PaymentUpdateAdapter>(
    "update-11-1761199800-0002",
    new CacheMetadataEntity
    {
        Method = "update",
        Microservice = "ms-pagos"
    },
    cancellationToken);
```

Comportamiento esperado de esa lectura:

- El provider busca primero por `messageId`.
- Si no encuentra el registro, retorna `null`.
- Si encuentra el registro pero `Method` o `Microservice` no coinciden con `filters`, retorna `null`.
- Si todo coincide, retorna únicamente `CachedData`.

### 11.7 Semántica de TTL

Para evitar ambigüedad, la guía debe usar esta convención:

- En la API del provider: `TimeSpan? ttl`
- En persistencia de registros: `CreatedAt` y `ExpireIn`
- En backends que soportan TTL nativo, el provider traduce internamente el valor recibido en `ttl`
- Si el esquema persistido sigue `MEMCACHEDBTABLE.md`, el provider debe completar `CreatedAt` y `ExpireIn` de forma consistente con ese documento.

No mezclar en el contrato público:

- `TimeSpan`,
- `CreatedAt` en `long`,
- `ExpireIn` en `long`,
- y timestamps con semánticas distintas.

### 11.8 Responsabilidad real de la librería

La librería es la responsable de:

- aplicar la estrategia (`CacheOnly`, `CacheThenSource`, `StoreOnly`, etc.),
- construir la clave final,
- decidir cuándo necesita validación adicional por metadata,
- orquestar lectura, escritura y fallback,
- decidir si la idempotencia es global o por microservicio,
- y decidir qué metadata enviar al provider en lectura o escritura.

Ejemplos de claves que la librería podría construir:

```txt
cache:{microservice}:{method}:{channelId}:{entityId}
get-11-1761197400-001
post-11-1761198605-0001
update-11-1761199800-0002
```

Con este enfoque:

- si el caso corresponde a canal, `MessageId` puede ser la clave exacta del registro;
- si el caso corresponde a caché funcional de dominio, puede mantenerse la convención `{microservice}:{method}:{channel}:{identifier}`;
- si mañana cambian las reglas de validación, cambia la librería y el provider sigue limitado a buscar por PK y comparar metadata.

### 11.9 Implementación esperada por backend

#### Redis

- Implementa muy bien `ICacheInfrastructure`.
- Puede usarse también para registros de flujo e idempotencia usando la misma interfaz.
- Debe limitarse a guardar y recuperar por clave exacta.
- Evitar `KEYS` o patrones costosos en producción.

#### DynamoDB

- Puede implementar `ICacheInfrastructure`.
- Puede soportar caché por clave exacta y también persistencia con TTL.
- `MessageId` puede mapearse directamente como PK.
- Si la librería envía `filters`, el provider puede validar el item recuperado contra esos campos sin convertir eso en una búsqueda compuesta.

#### MemoryCache

- Debe limitarse a `ICacheInfrastructure`.
- No debe prometer búsqueda por patrón ni consultas por atributos de negocio.
- Es ideal para escenarios locales, respuestas efímeras y catálogos pequeños.

### 11.10 Decisión final para el equipo

La implementación debe asumir lo siguiente como norma:

- Los providers son tontos: solo implementan CRUD por clave exacta.
- La librería hace la magia: compone claves, aplica estrategias y orquesta el flujo.
- `ICacheInfrastructure` resuelve almacenamiento por `MessageId` o por clave exacta definida por la librería.
- La misma `ICacheInfrastructure` también sirve para persistencia de flujo e idempotencia.
- `MessageId` es la primary key del registro.
- `T` representa únicamente `CachedData`.
- `CacheMetadataEntity` representa información descriptiva adicional del registro.
- Los ejemplos de negocio deben usar `ForKey(...)` con claves exactas.
- En lectura, `filters` solo validan el registro recuperado por `MessageId`; no cambian la búsqueda principal.
- Las búsquedas por patrón o grupo no forman parte del contrato mínimo obligatorio.

Con esta separación, el diseño queda claro, portable y listo para implementar sin ambigüedades entre proveedores.
