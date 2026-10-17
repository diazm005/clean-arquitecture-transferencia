# Estandar unico de cache desde backend (Redis / DynamoDB)

## Objetivo

Establecer un esquema unico y obligatorio para almacenar datos temporales en cache compartido cuando el backend genera, valida y administra el dato cacheado.

Este contrato cubre escenarios orientados a:

- performance;
- reutilizacion de datos;
- catalogos;
- consultas costosas;
- y contexto transaccional generado por el propio backend.

---

## 1. Esquema de datos obligatorio

```json
{
  "messageId": "ms-pagos-customer-47476453",
  "method": "cfg|trx",
  "referenceId": "source-entity-or-table",
  "status": "started|created",
  "microservice": "owner-microservice-name",
  "cachedData": null,
  "createdAt": 1700000000,
  "expireIn": 1700000300
}
```

### 1.1 Campos

| Campo | Tipo | Restriccion | Descripcion |
|-------|------|-----------|-----------|
| `messageId` | string | Obligatorio, unico | Identificador canonico del registro. Lo genera el backend. |
| `method` | enum | Obligatorio | Valores permitidos: `cfg`, `trx`. |
| `referenceId` | string | Obligatorio | Tabla, entidad, agregado o referencia funcional de origen. |
| `status` | enum | Obligatorio | Valores permitidos: `started`, `created`. |
| `microservice` | string | Obligatorio | Microservicio propietario del dato. |
| `cachedData` | object \| array | Variable | Contenido cacheado. Debe ser `null` cuando `status = "started"`. |
| `createdAt` | number | Obligatorio | Epoch seconds UTC de creacion del registro. |
| `expireIn` | number | Obligatorio | Epoch seconds UTC de expiracion absoluta. |

### 1.2 Estados

- `started`: reserva de clave creada, todavia sin dato util.
- `created`: registro completo, valido y listo para lectura.

---

## 2. Tipos de cache

### 2.1 Tipo `cfg`

Usar `cfg` para configuracion, catalogos y datos que cambian poco.

- TTL recomendado: hasta 24 horas.
- Ejemplos: monedas, catalogos de productos, reglas parametrizadas.

### 2.2 Tipo `trx`

Usar `trx` para datos transaccionales o de contexto reutilizable.

- TTL recomendado: definido segun la volatilidad del caso.
- Ejemplos: stock consultado, cliente enriquecido, resumen de validacion.

Nota: el backend puede usar TTL menor a 24 horas cuando el caso de uso lo requiera. El valor exacto debe quedar documentado en el punto de integracion.

---

## 3. Regla de clave canonica

En almacenamiento compartido, la clave fisica recomendada es `messageId`.

Formato sugerido:

```txt
{microservice}-{scope}-{identifier}
```

Ejemplos:

```txt
ms-productos-cfg-catalogo-v20260314
ms-pagos-trx-customer-47476453
ms-riesgos-trx-1376453
```

La API de la libreria puede exponer una clave funcional distinta en `ForKey(...)`, pero el mapping hacia `messageId` debe ser deterministico.

---

## 4. Ejemplos

### 4.1 Ejemplo `cfg`

```json
{
  "messageId": "ms-productos-cfg-catalogo-20260314",
  "method": "cfg",
  "referenceId": "SI_FINProductoConfigs",
  "status": "created",
  "microservice": "ms-productos",
  "cachedData": [
    {
      "productId": "PRD-001",
      "name": "Cuenta Ahorro",
      "enabled": true,
      "maxBalance": 999999.99
    }
  ],
  "createdAt": 1761150000,
  "expireIn": 1761236400
}
```

### 4.2 Ejemplo `trx`

```json
{
  "messageId": "ms-riesgos-trx-customer-1346453",
  "method": "trx",
  "referenceId": "SI_FINCuentaAhorros",
  "status": "created",
  "microservice": "ms-riesgos",
  "cachedData": {
    "idCli": "1346453",
    "dni": "47476453",
    "saldoDisponible": 15000.50,
    "movimientosUltimos30Dias": 45,
    "validadoEn": 1761153600
  },
  "createdAt": 1761153600,
  "expireIn": 1761157200
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

Validaciones minimas:

- `ttlSeconds > 0`
- `expireIn > createdAt`
- `status` valido para el contrato

### 5.2 DynamoDB

Estructura recomendada:

| Atributo | Tipo | Rol |
|----------|------|-----|
| `messageId` | String | Partition Key |
| `method` | String | Dato |
| `referenceId` | String | Dato |
| `status` | String | Dato |
| `microservice` | String | Dato |
| `cachedData` | String o Document | Dato |
| `createdAt` | Number | Dato |
| `expireIn` | Number | TTL |

Lectura recomendada:

- `GetItem` por `messageId`
- retornar `null` si no existe o si ya expiro

---

## 6. Reglas obligatorias

### 6.1 Responsabilidades

El backend propietario es responsable de:

- generar `messageId`;
- validar `cachedData`;
- definir TTL segun el caso;
- documentar la estructura del dato;
- no almacenar datos sensibles sin cifrado.

### 6.2 Validaciones

| Validacion | Accion |
|-----------|--------|
| `messageId` no vacio | Rechazar |
| `method` en `{cfg, trx}` | Rechazar si no cumple |
| `status` en `{started, created}` | Rechazar si no cumple |
| `expireIn > createdAt` | Rechazar |
| `cachedData = null` cuando `started` | Requerido |
| `cachedData != null` cuando `created` | Requerido |

### 6.3 Restricciones

- Tamano maximo recomendado por registro: 1 MB serializado.
- Si excede el limite, dividir el contenido en multiples registros.

---

## 7. Alcance

En alcance:

- cache de configuracion;
- cache transaccional generado por backend;
- Redis y DynamoDB como almacenamiento compartido.

Fuera de alcance:

- idempotencia basada en eventos de canal;
- cierre de registros por consumo;
- garantia de orden o exactly-once.
