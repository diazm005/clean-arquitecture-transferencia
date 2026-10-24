# Batchero de fraude: servicio HTTP y ejecución por lotes

Proyecto independiente .NET 10. El flujo principal expone un servicio HTTP para que
la API de transferencias solicite una evaluación. El batchero calcula y responde;
**la API de transferencias registra el seguimiento en BD_Equipo2**. El modo HTTP no
abre una conexión SQL. Los modos opcionales `--once` y `--watch` conservan la ejecución
por lotes de una cola SQL y no forman parte del flujo HTTP.

## Separación de responsabilidades

- `Domain/Services/PoliticaFraude.cs`: regla pura, sin EF ni conexiones.
- `FraudBatch/Http/BatchHttpHost.cs`: endpoint que ejecuta la política y devuelve la decisión.
- `SqlServerInfrastructure/Http/ClienteBatchFraude.cs`: comunicación HTTP desde transferencias.
- `SqlServerInfrastructure/Adapters/AntifraudeHttpAdapter.cs`: obtiene historial y registra intento, respuesta o error en SQL.
- `FraudBatch/EvaluadorPendientes.cs`: modo opcional de procesamiento de una cola SQL.

## Contrato HTTP y arranque

```powershell
dotnet run --project FraudBatch --launch-profile http
# Equivalente:
.\FraudBatch\ejecutar-fraude.ps1 -Modo Serve
```

Escucha en `http://localhost:5210`. `GET /health` comprueba disponibilidad.
`POST /api/fraud/evaluations` recibe los datos calculados por el servicio de transferencias:

```json
{
  "transactionReference": "77777777-7777-4777-8777-777777777777",
  "amount": 100,
  "previousDebitsIn15Minutes": 0,
  "knownDestination": false
}
```

Respuesta HTTP 200:

```json
{
  "transactionReference": "77777777-7777-4777-8777-777777777777",
  "riskLevel": "Bajo",
  "reason": "No se cumplen las condiciones de riesgo medio o alto de la regla del taller.",
  "ruleVersion": "TALLER-1.0"
}
```

Todos los campos de entrada son obligatorios. Una entrada inválida devuelve 400.
El cliente rechaza respuestas con otra referencia, riesgo desconocido, versión distinta
o motivo ausente. En SQL se conserva cada intento, nivel, motivo, versión y fechas.
El timeout HTTP es de dos segundos; un error nunca se interpreta como riesgo bajo.
Los ejemplos están en `FraudBatch.http` y en la carpeta Batchero de la colección Postman existente.
El modo HTTP requiere Development mientras esté pendiente la autenticación entre servicios.

## Reglas ficticias `TALLER-1.0`

Se aplica la primera condición que se cumpla. Los montos son soles, sin comisión.

| Prioridad | Condición | Nivel guardado |
|---|---|---|
| 1 | Monto >= S/ 40 000 | Alto (2) |
| 2 | Al menos 5 débitos previos desde la cuenta origen en los últimos 15 minutos | Alto (2) |
| 3 | Monto >= S/ 10 000 | Medio (1) |
| 4 | Monto >= S/ 5 000 y destino sin historial desde esa cuenta | Medio (1) |
| 5 | Resto de operaciones válidas | Bajo (0) |

El riesgo es una decisión educativa; `Alto` no demuestra fraude. La regla existente
del caso de uso determina si bloquea o exige confirmación. Ningún resultado mueve dinero.

En HTTP el servicio de transferencias calcula el historial al comenzar cada intento;
el batchero recibe sus indicadores. En el modo opcional de cola SQL, el batch obtiene
el historial al procesar la fila. Así se considera la actividad reciente en reintentos OTP.
Solo cuenta movimientos `DEBITO_MONTO` de naturaleza `D`, una vez
por operación. Excluye la transferencia actual, comisiones, solicitudes sin débito y
movimientos u operaciones posteriores a ese instante. La ventana incluye
el instante de quince minutos antes y el de procesamiento. Un destino es conocido si
hubo un débito previo desde el mismo origen al mismo banco y número de cuenta.
Los débitos posteriormente reversados siguen formando parte del historial de actividad;
esta versión no interpreta reversos como eliminación del evento de riesgo.

La columna `VersionRegla` solicitada debe ser exactamente `TALLER-1.0`; otra versión
produce un resultado `Error` sin nivel, para no aplicar reglas distintas en silencio.
El evaluador guarda motivo, proveedor `FraudBatch`, versión y fecha de respuesta UTC.

## Autocomprobaciones y modo opcional de cola SQL

Desde la raíz del repositorio, comprobar las reglas sin conexión:

```powershell
dotnet run --project FraudBatch -- --self-test
# Equivalente mediante el script:
.\FraudBatch\ejecutar-fraude.ps1 -Modo SelfTest
```

Solo para procesar una cola SQL pendiente mediante `--once` o `--watch`, define `ConnectionStrings__Transferencias` en la
sesión del proceso con la conexión autorizada a `BD_Equipo2`. No la agregues a Git.
Ejemplo con autenticación integrada y valores que debes reemplazar:

```powershell
$env:ConnectionStrings__Transferencias = 'Server=SERVIDOR,PUERTO;Database=BD_Equipo2;Integrated Security=True;Encrypt=True;TrustServerCertificate=False'
dotnet run --project FraudBatch -- --once
# O mantener el proceso consultando cada segundo hasta Ctrl+C:
.\FraudBatch\ejecutar-fraude.ps1 -Modo Watch -IntervaloSegundos 1
```

No se ejecuta `EnsureCreated`, `Migrate` ni scripts de creación. El usuario SQL necesita
leer transferencias y movimientos, y leer/actualizar evaluaciones de fraude. Los catálogos
de riesgo deben contener los valores 0, 1 y 2 definidos en el modelo.

`--once` procesa hasta vaciar la cola o alcanzar `--max-per-pass` (1000 por defecto),
muestra cuántas filas atendió y termina. `--watch` repite pasadas y espera el intervalo
configurado entre ellas. El código de salida es 0 para éxito/cancelación, 2 para argumentos
o configuración inválidos y 1 para fallo de procesamiento. Un error SQL termina el proceso
para que el operador lo revise; no hay reintentos ilimitados ocultos.

## Concurrencia y fallos del modo opcional de cola SQL

Cada evaluación se selecciona con `TOP (1)`, `UPDLOCK`, `READPAST` y `ROWLOCK` dentro de
una transacción corta `RepeatableRead`. Otra instancia omite las filas reservadas.
Esta elección admite bases con `READ_COMMITTED_SNAPSHOT` activado. SQL Server todavía
puede escalar bloqueos: la corrección se conserva aunque disminuya la concurrencia.

`Procesando` y el resultado final se guardan en la misma transacción. Si ocurre un error
o cae el proceso antes del commit, se revierte y la fila continúa `Pendiente`; no queda
una reserva permanente. Una repetición procesa solo pendientes, nunca una fila completada.
Un commit confirmado en SQL pero cuya respuesta se pierde tampoco genera una segunda
evaluación de esa misma fila: la próxima selección ya no la encuentra pendiente.

El tiempo máximo por comando es 15 segundos. La prueba `--self-test` valida fronteras y
prioridad de las reglas sin usar SQL; la concurrencia y la conectividad necesitan pruebas
de integración sobre una base autorizada. El historial solo refleja movimientos ya
confirmados al consultar: no cuenta solicitudes simultáneas aún sin débito. El batch
no sustituye controles transaccionales de saldo/límite ni la conciliación financiera.
