# Estandar unico de cache por canal (Redis / DynamoDB)

## Objetivo

Establecer un esquema unico y obligatorio para almacenar datos temporales originados por canales, eventos o mensajes externos en un cache compartido.

Este contrato cubre escenarios orientados a:

- trazabilidad de mensajes;
- consumo controlado;
- validacion posterior;
- e idempotencia para `post` y `update`.

---

## 1. Esquema de datos obligatorio

```json
{
  "messageId": "get-11-1700000000-001",
  "method": "get|post|update",
  "channelId": 11,
  "status": "started|created|closed",
  "microservice": "consumer-microservice-name",
  "cachedData": null,
  "createdAt": 1700000000,
  "expireIn": 1700000300
}
```

### 1.1 Campos

| Campo | Tipo | Restriccion | Descripcion |
|-------|------|-----------|-----------|
| `messageId` | string | Obligatorio, unico | Identificador canonico del mensaje generado por el canal. |
| `method` | enum | Obligatorio | Valores permitidos: `get`, `post`, `update`. |
| `channelId` | number | Obligatorio, mayor a 0 | Identificador del canal productor. |
| `status` | enum | Obligatorio | Valores permitidos: `started`, `created`, `closed`. |
| `microservice` | string | Obligatorio | Microservicio consumidor que administra el registro. |
| `cachedData` | object \| array | Variable | Debe ser `null` cuando `status = "started"`. |
| `createdAt` | number | Obligatorio | Epoch seconds UTC de creacion. |
| `expireIn` | number | Obligatorio | Epoch seconds UTC de expiracion absoluta. |

### 1.2 Estados

- `started`: reserva creada, todavia sin dato funcional completo.
- `created`: registro completo y listo para consumo.
- `closed`: registro ya consumido o invalidado. No debe reutilizarse para nuevas lecturas.

---

## 2. Tipos de operacion

### 2.1 Tipo `get`

Usar para respuestas de consulta provenientes del canal.

- TTL fijo: 5 minutos.
- Uso tipico: validaciones posteriores o reuso puntual del dato recibido.

### 2.2 Tipo `post`

Usar para operaciones de creacion donde se necesita idempotencia.

- TTL fijo: 20 minutos.
- Si el `messageId` ya existe con `status = "created"`, la operacion debe tratarse como duplicada.

### 2.3 Tipo `update`

Usar para operaciones de actualizacion donde se necesita idempotencia.

- TTL fijo: 20 minutos.
- Si el `messageId` ya existe con `status = "created"`, no debe reejecutarse la actualizacion.

---

## 3. Regla de clave canonica

En almacenamiento compartido, la clave fisica recomendada es `messageId`.

Formato sugerido:

```txt
{method}-{channelId}-{timestamp}-{secuencial}
```

Ejemplos:

```txt
get-11-1761197400-001
post-11-1761198605-0001
update-11-1761199800-0002
```

Si el canal usa UUID, tambien es valido siempre que mantenga unicidad global.

---

## 4. Ejemplos

### 4.1 Ejemplo `get`

```json
{
  "messageId": "get-11-1761197400-001",
  "method": "get",
  "channelId": 11,
  "status": "created",
  "microservice": "ms-clientes",
  "cachedData": {
    "dni": "47476453",
    "customerId": "CUST-0019281",
    "segment": "Retail",
    "active": true,
    "queryTimestamp": 1761197400
  },
  "createdAt": 1761197400,
  "expireIn": 1761197700
}
```

### 4.2 Ejemplo `post`

```json
{
  "messageId": "post-11-1761198605-0001",
  "method": "post",
  "channelId": 11,
  "status": "created",
  "microservice": "ms-pagos",
  "cachedData": {
    "paymentId": "PAY-783423",
    "idempotencyKey": "client-001-20260223-001",
    "amount": 1500.00,
    "state": "ACCEPTED",
    "processedAt": 1761198605
  },
  "createdAt": 1761198605,
  "expireIn": 1761199805
}
```

### 4.3 Ejemplo `update`

```json
{
  "messageId": "update-11-1761199800-0002",
  "method": "update",
  "channelId": 11,
  "status": "created",
  "microservice": "ms-pagos",
  "cachedData": {
    "paymentId": "PAY-783423",
    "transitionFrom": "PENDING",
    "transitionTo": "CONFIRMED",
    "updatedAt": 1761199800,
    "updatedBy": "operator-user-123"
  },
  "createdAt": 1761199800,
  "expireIn": 1761201000
}
```

---

## 5. Implementacion por motor

### 5.1 Redis

```txt
KEY: {messageId}
VALUE: JSON serializado del objeto completo
TTL: expireIn - createdAt
```

Operacion recomendada:

```txt
SET {messageId} {jsonString} EX {ttlSeconds}
```

Reglas minimas:

- `get` debe usar TTL de 300 segundos;
- `post` y `update` deben usar TTL de 1200 segundos;
- no procesar registros con `status = "closed"`.

### 5.2 DynamoDB

Estructura recomendada:

| Atributo | Tipo | Rol |
|----------|------|-----|
| `messageId` | String | Partition Key |
| `method` | String | Dato |
| `channelId` | Number | Dato |
| `status` | String | Dato |
| `microservice` | String | Dato |
| `cachedData` | String o Document | Dato |
| `createdAt` | Number | Dato |
| `expireIn` | Number | TTL |

Lectura recomendada:

- `GetItem` por `messageId`;
- retornar `null` si no existe, expiro o esta `closed`.

---

## 6. Reglas obligatorias

### 6.1 Responsabilidades

El canal productor es responsable de:

- generar `messageId`;
- indicar `method`;
- entregar datos consistentes antes de pasar a `created`.

El microservicio consumidor es responsable de:

- verificar duplicados para `post` y `update`;
- cerrar registros `get` cuando su flujo lo requiera;
- no procesar datos con `status = "closed"`;
- documentar la estructura esperada de `cachedData`.

### 6.2 Ciclos de vida

`get`

1. El canal crea `started` o directamente `created`.
2. El consumidor lee el registro.
3. Si el consumo fue exitoso y el flujo lo exige, actualiza a `closed`.

`post` y `update`

1. El consumidor verifica si existe `messageId`.
2. Si no existe, procesa y registra el resultado.
3. Si existe con `status = "created"`, trata la operacion como duplicada.

### 6.3 Validaciones

| Validacion | Accion |
|-----------|--------|
| `method` en `{get, post, update}` | Rechazar si no cumple |
| `channelId > 0` | Rechazar |
| `status` en `{started, created, closed}` | Rechazar si no cumple |
| `expireIn > createdAt` | Rechazar |
| `cachedData = null` cuando `started` | Requerido |
| `cachedData != null` cuando `created` | Requerido |

### 6.4 Restricciones

- Tamano maximo recomendado por registro: 512 KB serializado.
- Si excede el limite, dividir en multiples mensajes.

---

## 7. Alcance

En alcance:

- cache originado por canales;
- idempotencia basada en `messageId`;
- cierre por consumo en `get`;
- Redis y DynamoDB como almacenamiento compartido.

Fuera de alcance:

- cache funcional generado solo por backend;
- politicas de estrategia como `CacheThenSourceAndStore`;
- garantia de orden o exactly-once.
