---
name: scaffolding
description: Automatización de la creación de boilerplate para casos de uso, controladores y pruebas unitarias asegurando consistencia técnica y arquitectónica. Se activa cuando el usuario pide crear un nuevo feature, endpoint, usecase o cuando menciona scaffolding. Invocar con /scaffolding <NombreFeature>.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "<UsecaseName>"
---

# Scaffolding Skill

Automatización de la creación de boilerplate para nuevos casos de uso, controladores y pruebas unitarias siguiendo la arquitectura hexagonal. El ecosistema de scripts reduce errores manuales y asegura consistencia en namespaces y patrones de diseño.

**Uso:** `/scaffolding UserRegister` — genera todos los archivos para el feature `UserRegister`.

---

## 1. Scripts Disponibles

| Script | Descripción |
|---|---|
| `scripts/scaffold-usecase.ps1` | Port, RequestAdapter, ResponseAdapter, Validator y Case |
| `scripts/scaffold-controller.ps1` | Controlador API con headers, CancellationToken y manejo de errores |
| `scripts/scaffold-test.ps1` | Suite de tests xUnit con IDisposable, NullLogger y tests base |

---

## 2. Uso de los Scripts

Desde la raíz del proyecto:

### Generar Caso de Uso (completo)

```powershell
./scripts/scaffold-usecase.ps1 -UsecaseName "$ARGUMENTS"
```

### Generar Controlador

```powershell
./scripts/scaffold-controller.ps1 -UsecaseName "$ARGUMENTS" -Route "v1/api/$ARGUMENTS"
```

### Generar Pruebas Unitarias

```powershell
./scripts/scaffold-test.ps1 -UsecaseName "$ARGUMENTS"
```

---

## 3. Componentes Generados

| Componente | Script | Ruta Generada |
|---|---|---|
| **Port** | `usecase` | `Application/Ports/I[Name]Port.cs` |
| **Response Adapter** | `usecase` | `Application/Adapters/[Name]Adapter.cs` |
| **Request Adapter** | `usecase` | `Application/Adapters/[Name]RequestAdapter.cs` |
| **Validator** | `usecase` | `Application/Usecases/[Name]Usecase/[Name]RequestAdapterValidator.cs` |
| **Use Case** | `usecase` | `Application/Usecases/[Name]Usecase/[Name]Case.cs` |
| **Controller** | `controller` | `SystemAPI/Controllers/[Name]/[Name]Controller.cs` |
| **Test Suite** | `test` | `Application.Tests/Usecases/[Name]Usecase/[Name]CaseTests.cs` |

---

## 4. Patrones Implementados

- **ILogger:** Cada `Case` inyecta `ILogger<T>` y emite trazas semánticas al inicio y fin.
- **Header Validation:** El `Case` valida el `TraceIdentifierAdapter` con `FluentValidationExecutor` antes de cualquier lógica (retorna 422).
- **Request Validation:** El `Case` valida el `RequestAdapter` con su validator dedicado (retorna 400).
- **Task.FromResult:** El `Case` usa `Task.FromResult` en lugar de `async` sin `await` para evitar warnings de Meziantou analyzer.
- **CancellationToken:** El `Controller` captura el token de ASP.NET Core y lo propaga al `Port`.
- **Error Response:** El `Controller` usa `EasyResponseHelper.ErrorResponse("11033")` en el `catch`.
- **Header Tracking:** Captura `x-device-identifier`, `x-message-identifier` y `x-channel-identifier`.
- **NullLogger en Tests:** Los tests usan `NullLogger<T>.Instance` para el logger (sin mocks de logging).

---

## 5. Automated AI Workflow (Post-Scaffold)

Cuando un agente AI hace scaffold de un feature, NO debe detenerse después de ejecutar los scripts. Debe proceder automáticamente con:

1. **DI Registration:** Agregar el Port/Case en `SystemAPI/ApplicationSetting.cs`:
   ```csharp
   services.AddTransient<I[Name]Port, [Name]Case>();
   ```
2. **DTO Definition:** Preguntar al usuario los campos del request o inferirlos, y poblar `[Name]RequestAdapter.cs`.
3. **Validator Rules:** Agregar reglas FluentValidation en `[Name]RequestAdapterValidator.cs` usando `MessageCatalog`.
4. **Controller Refinement:** Verificar que el `[FromBody]` use el `RequestAdapter` correcto.
5. **Business Logic:** Implementar la lógica de negocio en `[Name]Case.cs`.
6. **Test Bootstrapping:** Poblar `CreateTestRequest()` con valores válidos y verificar que los tests base pasen con `dotnet test`.

---

## 6. Pasos Manuales (fallback si fallan los scripts)

Si los scripts de PowerShell no están disponibles, crear los archivos en este orden:

1. `Application/Adapters/[Name]Adapter.cs` — response DTO con `Message` como campo base.
2. `Application/Adapters/[Name]RequestAdapter.cs` — request DTO con los campos del feature.
3. `Application/Usecases/[Name]Usecase/[Name]RequestAdapterValidator.cs` — validator con reglas FluentValidation.
4. `Application/Ports/I[Name]Port.cs` — interfaz con `ExecuteAsync(RequestAdapter, TraceIdentifierAdapter, CancellationToken)`.
5. `Application/Usecases/[Name]Usecase/[Name]Case.cs` — implementa el Port, inyecta ILogger, valida header y request.
6. `SystemAPI/Controllers/[Name]/[Name]Controller.cs` — inyecta el Port, captura headers y CancellationToken.
7. `Application.Tests/Usecases/[Name]Usecase/[Name]CaseTests.cs` — IDisposable, NullLogger, tests de header inválido y happy path.
8. Registrar en `SystemAPI/ApplicationSetting.cs`.
