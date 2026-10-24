# JWT y autorización por cliente

La API ahora valida JWT en los tres controladores de negocio. JWT identifica al
cliente y sus permisos; las reglas de saldo, comisión, OTP y riesgo siguen siendo
responsabilidad del dominio y los casos de uso.

## Ejecutar y obtener un token

Desde la raíz, genera el token **antes de iniciar la API por primera vez**:

```powershell
# Ana: lectura y transferencias. Vigencia de 15 minutos.
$token = ./scripts/nuevo-token.ps1 -Dni 12345678
dotnet run --project SystemAPI --launch-profile http
```

La clave se crea en User Secrets del usuario de Windows, fuera de Git. Cada desarrollador
genera la suya en su equipo. `appsettings.Development.json` contiene únicamente el
emisor `clean-transferencias-dev` y la audiencia `clean-transferencias-api`.
El servicio falla al iniciar si no tiene configuración y clave de firma válidas.

En Swagger (`http://localhost:5207/swagger`), pulsa **Authorize** y pega únicamente
el token. En Postman importa `SystemAPI/Transferencias.postman_collection.json` y
establece `accessToken` como variable local de tu entorno, sin publicarla.

```powershell
# En una consola separada si la API está ejecutándose en la primera:
$token = ./scripts/nuevo-token.ps1 -Dni 12345678
Invoke-RestMethod http://localhost:5207/api/account/001 -Headers @{ Authorization = "Bearer $token" }

# Segundo cliente del simulador o token con permiso limitado:
$tokenLuis = ./scripts/nuevo-token.ps1 -Dni 87654321
$tokenLectura = ./scripts/nuevo-token.ps1 -Dni 12345678 -SoloLectura
```

Estos tokens provienen de `dotnet user-jwts`, una herramienta de desarrollo. El script
puede representar un DNI elegido por el desarrollador: **no es un endpoint de login**.
No se creó una contraseña de cliente ni se reutiliza la contraseña de SQL Server.
En SQL, el DNI del token debe corresponder a un cliente existente en `BD_Equipo2`.

## Endpoints y permisos

| Endpoint | Scope | Regla de titularidad |
|---|---|---|
| GET `/api/client/{nationalId}` | `transferencias.read` | DNI solicitado igual al claim `national_id` |
| GET `/api/account/{accountNumber}` | `transferencias.read` | La cuenta pertenece al DNI del token |
| GET `/api/transactions/{transactionReference}` | `transferencias.read` | La operación pertenece al DNI del token |
| POST `/api/transactions` | `transferencias.write` | DNI del cuerpo y titular de la cuenta origen iguales al DNI del token |

No se exige ser titular de la cuenta destino: transferir a terceros es parte del caso de uso.
`/ping` y `/health/live` son públicos. Los demás endpoints de diagnóstico requieren
autenticación; Swagger continúa disponible en esta demo de desarrollo.

- **401:** token ausente, vencido, sin firma, manipulado o con emisor, audiencia o DNI inválidos.
- **403:** falta el scope o se intenta enviar una transferencia con otro DNI/cuenta origen.
- **404:** recurso inexistente o ajeno durante consultas; también referencia de transferencia ajena al enviar.

Los errores conservan el formato BIAN. No se acepta el token desde la URL. Se valida
la firma HS256, la expiración y la fecha de inicio; se toleran 30 segundos de diferencia
entre relojes. Los scopes son exactos y distinguen mayúsculas. Los intentos de
transferencia no autorizados se detienen antes de auditoría financiera, fraude o débito.

## Ubicación por capas

- **SystemAPI/Handlers/JwtHandler.cs:** autentica, valida claims y define políticas de scopes.
- **SystemAPI/Security/ClienteActualHttp.cs:** adapta la identidad HTTP al puerto `IClienteActual`.
- **Application/Ports/IClienteActual.cs:** contrato independiente de JWT y ASP.NET.
- **Application/Handlers/AccesoCliente.cs:** decorador de autorización por titular; llama a los
  handlers existentes solo después de validar acceso. Los controladores reciben estos
  casos de uso mediante interfaces registradas en `ApplicationSetting`.
- **Dominio e infraestructura SQL:** conservan las reglas y repositorios existentes.

## Validación reproducible

```powershell
dotnet build SystemApi3.0.slnx -m:1
dotnet run --project Tests.Api --no-build
dotnet run --project Tests --no-build
dotnet run --project Tests.Infrastructure --no-build
dotnet run --project FraudBatch --no-build -- --self-test
```

`Tests.Api` usa el pipeline real de ASP.NET y claves aleatorias propias de cada ejecución,
sin depender de User Secrets ni SQL. Prueba firma, algoritmo, vigencia, emisor, audiencia,
DNI, scopes, acceso entre clientes, errores BIAN, Swagger, transferencia válida y reintento
sin doble débito. Los movimientos de esas pruebas solo afectan memoria descartable.

Validación del 23/10/2026: compilación sin errores ni advertencias; 90 comprobaciones
JWT/API, 41 de negocio, 191 de modelo EF, 16 de cliente HTTP y 15 del batch aprobadas.
Además se probó la API en un puerto local con un token real de `dotnet user-jwts`:
cuenta propia 200, cuenta ajena 404 y solicitud sin token 401. La revisión SQL fue
exclusivamente de lectura en `BD_Equipo2`: coinciden 11 tablas, 84 columnas y 16 relaciones.
Estas pruebas no equivalen a una transferencia financiera completa contra SQL Server.

## Alcance y siguientes pasos

La autenticación JWT y la autorización por cliente quedan implementadas para el taller.
Para desplegar, falta integrar un proveedor de identidad real que autentique a los
clientes y emita sus tokens; se deberá adaptar el validador a su emisor, audiencia,
algoritmos y claves públicas. La restricción a `Development` sigue activa porque
OTP y cámara aún son simulados.

La comunicación API → FraudBatch sigue siendo una integración interna de desarrollo
sin autenticación entre servicios. El JWT del cliente **no se reenvía** al batchero.
Ese canal requiere identidad propia de servicio y transporte seguro antes de exponerlo
fuera del entorno local. También siguen pendientes OTP real, conciliación de cámara
y entrega efectiva de notificaciones.
