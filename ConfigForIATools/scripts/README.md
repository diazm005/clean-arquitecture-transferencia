# Scripts de Scaffolding

Este directorio contiene scripts de PowerShell para la generación automatizada de código siguiendo los estándares de arquitectura hexagonal del proyecto.

## 📋 Scripts Disponibles

| Script | Propósito | Archivos Generados |
|--------|-----------|-------------------|
| `scaffold-usecase.ps1` | Genera la lógica de negocio completa | Port, Adapter (DTO), UseCase |
| `scaffold-controller.ps1` | Genera el controlador de API | Controller con headers y manejo de errores |
| `scaffold-test.ps1` | Genera la estructura de pruebas | Test con xUnit + Moq (IDisposable + #region) |

---

## Guía Paso a Paso

### Ejemplo: Crear el Feature "UserRegister"

#### **Paso 1: Generar el Caso de Uso (Lógica de Negocio)**

```powershell
# Desde la raíz del proyecto
./scripts/scaffold-usecase.ps1 -UsecaseName "UserRegister"
```

**Resultado:**

```
Created Adapter: Application/Adapters/UserRegisterAdapter.cs
Created Use Case Class: Application/Usecases/UserRegisterUsecase/UserRegisterCase.cs
Created Port Interface: Application/Ports/IUserRegisterPort.cs
```

**Archivos Creados:**

- `Application/Ports/IUserRegisterPort.cs` → Interfaz del contrato
- `Application/Adapters/UserRegisterAdapter.cs` → DTO de respuesta
- `Application/Usecases/UserRegisterUsecase/UserRegisterCase.cs` → Lógica de negocio

---

#### **Paso 2: Registrar en Inyección de Dependencias**

Abre `Application/ApplicationSetting.cs` y registra el nuevo puerto:

```csharp
// Dependency injection        
services.AddTransient<IExamplePort, ExampleCase>();
services.AddTransient<IUserRegisterPort, UserRegisterCase>(); // ← Añadir esta línea
```

---

#### **Paso 3: Implementar la Lógica de Negocio**

Edita `Application/Usecases/UserRegisterUsecase/UserRegisterCase.cs`:

```csharp
public async Task<EasyResult<UserRegisterAdapter>> ExecuteAsync(
    UserRegisterRequestAdapter request,
    TraceIdentifierAdapter header, 
    CancellationToken ct = default)
{
    // TODO: Implementar lógica de registro
    var result = new UserRegisterAdapter { Message = "User registered successfully" };
    return EasyResult<UserRegisterAdapter>.Success(result);
}
```

---

#### **Paso 4: Generar el Controlador de API**

```powershell
./scripts/scaffold-controller.ps1 -UsecaseName "UserRegister" -Route "v1/api/UserRegister"
```

**Resultado:**

```
Created Controller: SystemAPI/Controllers/UserRegister/UserRegisterController.cs
```

**Archivo Creado:**

- `SystemAPI/Controllers/UserRegister/UserRegisterController.cs` → Endpoint HTTP

El controlador incluye automáticamente:

- Captura de headers de trazabilidad (`x-device-identifier`, etc.)
- Manejo de errores con `EasyResponseHelper`
- Logging estructurado con Serilog

---

#### **Paso 5: Generar los Tests Unitarios**

```powershell
./scripts/scaffold-test.ps1 -UsecaseName "UserRegister"
```

**Resultado:**

```
Created Test: Application.Tests/Usecases/UserRegisterUsecase/UserRegisterCaseTests.cs
```

**Archivo Creado:**

- `Application.Tests/Usecases/UserRegisterUsecase/UserRegisterCaseTests.cs` → Suite de tests

La estructura del test incluye:

- `#region Setup/Teardown` con `IDisposable`
- `#region Helper Methods` para datos de prueba
- `#region Constructor Tests`
- `#region Method Tests - ExecuteAsync`

---

#### **Paso 6: Implementar y Ejecutar Tests**

Edita el archivo de test generado y añade casos específicos:

```csharp
[Fact]
public async Task ExecuteAsync_ShouldReturnSuccess_WhenUserIsValid()
{
    // Arrange
    var header = CreateTestHeader();
    var request = CreateTestRequest();

    // Act
    var result = await _sut!.ExecuteAsync(request, header);

    // Assert
    Assert.True(result.IsSuccess);
    Assert.NotNull(result.SuccessValue);
}
```

Ejecuta los tests:

```powershell
dotnet test Application.Tests/Application.Tests.csproj
```

---

## 📝 Plantilla de Workflow Completo

```powershell
# 1. Generar toda la estructura
./scripts/scaffold-usecase.ps1 -UsecaseName "OrderProcessing"
./scripts/scaffold-controller.ps1 -UsecaseName "OrderProcessing" -Route "v1/api/orders/process"
./scripts/scaffold-test.ps1 -UsecaseName "OrderProcessing"

# 2. Registrar DI manualmente en ApplicationSetting.cs
# services.AddTransient<IOrderProcessingPort, OrderProcessingCase>();

# 3. Implementar lógica en OrderProcessingCase.cs

# 4. Ejecutar tests
dotnet test
```

---

## 🛠️ Parámetros de los Scripts

### `scaffold-usecase.ps1`

- **`-UsecaseName`** (Requerido): Nombre del caso de uso sin sufijos

### `scaffold-controller.ps1`

- **`-UsecaseName`** (Requerido): Nombre del caso de uso
- **`-Route`** (Opcional): Ruta del endpoint (default: `v1/api/[controller]`)

### `scaffold-test.ps1`

- **`-UsecaseName`** (Requerido): Nombre del caso de uso a testear

---

## ⚠️ Troubleshooting

### Error: "Usecase folder already exists"

```powershell
# Los scripts NO sobrescriben archivos existentes
# Para regenerar, elimina primero los archivos manualmente
```

### Error: "Test file already exists"

```powershell
# El script generate-tests.ps1 preguntará antes de sobrescribir
# scaffold-test.ps1 requiere eliminación manual
```

### No se encuentra el Port en el Controller

```powershell
# Verifica que el Port esté registrado en ApplicationSetting.cs
# Recuerda hacer rebuild después de añadir el registro
dotnet build
```

---

## 📚 Recursos Relacionados

- [CLAUDE.md](../CLAUDE.md) → Guía de arquitectura
- [.claude/skills/](./.claude/skills/) → Definiciones de capacidades para IA
- [.claude/agents/](./.claude/agents/) → Agentes especializados (test-engineer, code-reviewer, etc.)
