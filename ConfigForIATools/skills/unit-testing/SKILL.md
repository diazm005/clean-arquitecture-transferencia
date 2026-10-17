---
name: unit-testing
description: Guía completa para generar y escribir pruebas unitarias usando xUnit, Moq y el Result Pattern (EasyResult). Se activa automáticamente cuando se crea código nuevo, se mencionan tests, se solicita cobertura o se revisan pruebas. Cubre estructura IDisposable, mocking con Moq, aserciones sobre EasyResult, Theory/InlineData, naming conventions y checklist de calidad.
allowed-tools: Read, Write, Edit, Bash
argument-hint: "<NombreUsecase>"
---

# Unit Testing Skill

Guía de referencia para escribir pruebas unitarias aisladas, rápidas y mantenibles siguiendo el estándar Arify en la capa `Application.Tests`, siguiendo los estándares del proyecto.

---

## 1. Stack de Testing

| Herramienta            | Rol                                               |
| ---------------------- | ------------------------------------------------- |
| **xUnit**              | Framework de ejecución de tests                   |
| **Moq**                | Mocking de interfaces/ports                       |
| **Helper Methods**     | Centralización de creación de data de prueba      |
| **Theory/InlineData**  | Tests parametrizados para múltiples escenarios    |
| **MemberData**         | Tests parametrizados con datos complejos          |
| **Naming Conventions** | Nombres descriptivos para tests y helpers         |
| **IDisposable**        | Limpieza de SUT y mocks entre tests               |
| **FluentValidation**   | Validación de requests (ejecutada dentro del SUT) |
| **EasyResult\<T\>**    | Tipo de retorno de todos los casos de uso         |

---

## 2. Estructura de Clase de Test

Cada archivo de test sigue este esqueleto invariable:

```csharp
using Application.Adapters;
using Application.Internals.Adapters;
using Application.Ports;
using Application.Usecases.MyFeatureUsecase;
using Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Application.Tests.Usecases.MyFeatureUsecase;

public class MyFeatureCaseTests : IDisposable
{
    // ── Fields ───────────────────────────────────────────────
    private MyFeatureCase? _sut;
    private Mock<IMyFeatureQuery>? _mockQuery;
    private Mock<IMyFeatureCommand>? _mockCommand;

    #region Setup/Teardown
    public MyFeatureCaseTests()
    {
        _mockQuery   = new Mock<IMyFeatureQuery>();
        _mockCommand = new Mock<IMyFeatureCommand>();
        _sut = new MyFeatureCase(_mockQuery.Object, _mockCommand.Object);
    }

    public void Dispose()
    {
        _sut         = null;
        _mockQuery   = null;
        _mockCommand = null;
    }
    #endregion

    #region Helper Methods
    private TraceIdentifierAdapter CreateTestHeader(string suffix = "test") =>
        new()
        {
            DeviceIdentifier  = $"device-{suffix}",
            MessageIdentifier = $"message-{suffix}",
            ChannelIdentifier = $"channel-{suffix}",
        };

    private MyFeatureRequestAdapter CreateTestRequest() =>
        new()
        {
            // populate with valid default values
        };
    #endregion

    #region Constructor Tests
    [Fact]
    public void Constructor_ShouldInitialize_Successfully()
    {
        Assert.NotNull(_sut);
    }
    #endregion

    #region Method Tests - ExecuteAsync
    // ... tests here
    #endregion
}
```

### Reglas de Estructura

- La clase hereda `IDisposable`; el `Dispose` anula **todos** los campos para liberar mocks entre tests.
- Las regions son: `Setup/Teardown`, `Helper Methods`, `Constructor Tests`, `Method Tests - <NombreMetodo>`.
- Una region `Method Tests` por cada método público del caso de uso.
- Los helpers `CreateTest*` centralizan la creación de datos válidos y se reutilizan en `Arrange`.

---

## 3. Patrones de Assert sobre `EasyResult<T>`

El tipo de retorno de todo caso de uso es `EasyResult<T>`. Usa las siguientes aserciones según el escenario:

### 3.1 Resultado de Éxito

```csharp
Assert.NotNull(result);
Assert.True(result.IsSuccess);
Assert.NotNull(result.SuccessValue);
Assert.Equal("expected value", result.SuccessValue!.SomeField);
```

### 3.2 Resultado de Fallo (validación / negocio)

```csharp
Assert.NotNull(result);
Assert.False(result.IsSuccess);
Assert.Equal(422, result.Status);   // o 400, 404, 403 según el caso
Assert.NotEmpty(result.ValidationValues);
```

### 3.3 Verificar mensaje de error específico

```csharp
var error = result.ValidationValues.FirstOrDefault();
Assert.NotNull(error);
Assert.Equal("FIELD_REQUIRED", error!.Code);
Assert.Contains("required", error.Message, StringComparison.OrdinalIgnoreCase);
```

### 3.4 Tabla de códigos de estado esperados

| Escenario                                       | Status                   |
| ----------------------------------------------- | ------------------------ |
| Headers `TraceIdentifier` inválidos o faltantes | `422`                    |
| Request body inválido / campo requerido vacío   | `400`                    |
| Recurso no encontrado                           | `404`                    |
| Sin permisos                                    | `403`                    |
| Éxito con payload                               | `200` (IsSuccess = true) |
| Éxito sin contenido                             | `204` (IsSuccess = true) |

---

## 4. Patrones de Moq

### 4.1 Setup básico — retornar un valor

```csharp
_mockQuery!
    .Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new MyEntity { Id = 1, Name = "Test" });
```

### 4.2 Setup con argumento específico

```csharp
_mockQuery!
    .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
    .ReturnsAsync(new MyEntity { Id = 42 });
```

### 4.3 Setup para retornar `null` (entidad no encontrada)

```csharp
_mockQuery!
    .Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync((MyEntity?)null);
```

### 4.4 Setup para simular excepción de infraestructura

```csharp
_mockQuery!
    .Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
    .ThrowsAsync(new InvalidOperationException("DB unavailable"));
```

### 4.5 Setup para comandos void / Task

```csharp
_mockCommand!
    .Setup(x => x.SaveAsync(It.IsAny<MyEntity>(), It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
```

### 4.6 Verify — confirmar llamadas al mock

```csharp
// Se llamó exactamente una vez
_mockQuery!.Verify(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once());

// Nunca se llamó (camino corto-circuitado por validación)
_mockQuery!.Verify(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());

// Se llamó N veces exactas
_mockCommand!.Verify(x => x.SaveAsync(It.IsAny<MyEntity>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
```

> **Regla:** Siempre verifica las llamadas a mocks en el path feliz. En el path de error (validación falla antes), verifica `Times.Never()` para asegurar que la infraestructura no fue tocada.

---

## 5. Tests con `Theory` e `InlineData`

Usa `[Theory]` cuando el mismo comportamiento debe validarse contra múltiples valores de entrada:

```csharp
#region Method Tests - ExecuteAsync (Parametrized)
[Theory]
[InlineData("")]
[InlineData("   ")]
[InlineData(null)]
public async Task ExecuteAsync_ShouldReturnFailure_WhenEmailIsNullOrEmpty(string? email)
{
    // Arrange
    var header  = CreateTestHeader();
    var request = CreateTestRequest();
    request.Email = email!;

    // Act
    var result = await _sut!.ExecuteAsync(request, header);

    // Assert
    Assert.False(result.IsSuccess);
    Assert.Equal(400, result.Status);
}
#endregion
```

Para datos complejos usa `[MemberData]`:

```csharp
public static IEnumerable<object[]> InvalidRequests()
{
    yield return new object[] { "", "valid@mail.com" };   // username vacío
    yield return new object[] { "user", "" };              // email vacío
}

[Theory]
[MemberData(nameof(InvalidRequests))]
public async Task ExecuteAsync_ShouldFail_ForInvalidCombinations(string username, string email)
{
    var header  = CreateTestHeader();
    var request = new MyFeatureRequestAdapter { Username = username, Email = email };

    var result = await _sut!.ExecuteAsync(request, header);

    Assert.False(result.IsSuccess);
}
```

---

## 6. Ejemplo Completo: Caso con Dependencias

Basado en el patrón real de `ExampleCaseTests`:

```csharp
public class ProductCaseTests : IDisposable
{
    private ProductCase? _sut;
    private Mock<IProductQuery>?   _mockProductQuery;
    private Mock<IInventoryQuery>? _mockInventoryQuery;

    #region Setup/Teardown
    public ProductCaseTests()
    {
        _mockProductQuery   = new Mock<IProductQuery>();
        _mockInventoryQuery = new Mock<IInventoryQuery>();
        _sut = new ProductCase(_mockProductQuery.Object, _mockInventoryQuery.Object);
    }

    public void Dispose()
    {
        _sut                = null;
        _mockProductQuery   = null;
        _mockInventoryQuery = null;
    }
    #endregion

    #region Helper Methods
    private TraceIdentifierAdapter CreateTestHeader(string suffix = "test") =>
        new()
        {
            DeviceIdentifier  = $"device-{suffix}",
            MessageIdentifier = $"message-{suffix}",
            ChannelIdentifier = $"channel-{suffix}",
        };

    private ProductEntity CreateTestProduct(int id = 1, string name = "Widget") =>
        new() { Id = id, Name = name, Price = 9.99m };

    private InventoryEntity CreateTestInventory(int stock = 10) =>
        new() { Stock = stock };
    #endregion

    #region Constructor Tests
    [Fact]
    public void Constructor_ShouldInitialize_Successfully()
    {
        Assert.NotNull(_sut);
    }
    #endregion

    #region Method Tests - GetProductAsync
    [Fact]
    public async Task GetProductAsync_ShouldReturnSuccess_WhenProductExists()
    {
        // Arrange
        var header    = CreateTestHeader();
        var product   = CreateTestProduct();
        var inventory = CreateTestInventory();

        _mockProductQuery!
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockInventoryQuery!
            .Setup(x => x.GetStockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inventory);

        // Act
        var result = await _sut!.GetProductAsync(1, header);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SuccessValue);
        Assert.Equal("Widget", result.SuccessValue!.Name);
        _mockProductQuery.Verify(
            x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task GetProductAsync_ShouldReturnNotFound_WhenProductDoesNotExist()
    {
        // Arrange
        var header = CreateTestHeader();

        _mockProductQuery!
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductEntity?)null);

        // Act
        var result = await _sut!.GetProductAsync(99, header);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Status);
        _mockInventoryQuery!.Verify(
            x => x.GetStockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task GetProductAsync_ShouldReturnFailure_WhenHeaderIsInvalid()
    {
        // Arrange
        var header = new TraceIdentifierAdapter(); // campos vacíos → inválido

        // Act
        var result = await _sut!.GetProductAsync(1, header);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.Status);
        _mockProductQuery!.Verify(
            x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
    #endregion
}
```

---

## 7. Caso sin Dependencias (Solo Logger)

Para casos de uso que no inyectan ports (solo lógica interna + ILogger):

```csharp
using Microsoft.Extensions.Logging.Abstractions;

public class UserRegisterCaseTests : IDisposable
{
    private UserRegisterCase? _sut;

    #region Setup/Teardown
    public UserRegisterCaseTests()
    {
        // NullLogger<T>.Instance — no requiere Mock<ILogger<T>>
        _sut = new UserRegisterCase(NullLogger<UserRegisterCase>.Instance);
    }

    public void Dispose()
    {
        _sut = null;
    }
    #endregion

    // ... helpers y tests
}
```

> `NullLogger<T>.Instance` descarta todos los logs sin hacer nada — es la forma correcta de satisfacer la inyección de `ILogger<T>` en tests. Evita el setup verboso de `Mock<ILogger<T>>`.

---

## 8. Convenciones de Nomenclatura

### Método de Test

```
[Método]_Should[Comportamiento]_When[Condición]
```

| Ejemplo                                                    | Descripción         |
| ---------------------------------------------------------- | ------------------- |
| `ExecuteAsync_ShouldReturnSuccess_WhenRequestIsValid`      | Path feliz          |
| `ExecuteAsync_ShouldReturnFailure_WhenEmailIsEmpty`        | Validación de campo |
| `ExecuteAsync_ShouldReturnNotFound_WhenEntityDoesNotExist` | Entidad inexistente |
| `ExecuteAsync_ShouldReturnFailure_WhenHeaderIsInvalid`     | Header inválido     |
| `Constructor_ShouldInitialize_Successfully`                | Test de constructor |

### Sufijos para helpers

| Helper                 | Propósito                                    |
| ---------------------- | -------------------------------------------- |
| `CreateTestHeader()`   | `TraceIdentifierAdapter` con valores válidos |
| `CreateTest[Entity]()` | Entidad de dominio con valores válidos       |
| `CreateTestRequest()`  | DTO de entrada con valores válidos           |

---

## 9. Comandos de Ejecución

```bash
# Ejecutar todos los tests
dotnet test Application.Tests/Application.Tests.csproj

# Ejecutar tests de un namespace específico
dotnet test Application.Tests/Application.Tests.csproj --filter "FullyQualifiedName~UserRegister"

# Ejecutar con salida verbose
dotnet test Application.Tests/Application.Tests.csproj --logger "console;verbosity=detailed"

# Ver cobertura (requiere coverlet)
dotnet test Application.Tests/Application.Tests.csproj --collect:"XPlat Code Coverage"
```

---

## 10. Checklist antes de Hacer Commit

- [ ] Existe un test de **constructor** (`Constructor_ShouldInitialize_Successfully`).
- [ ] Existe al menos un test de **path feliz** por método público.
- [ ] Existe al menos un test de **path de error** por cada rama de fallo en el caso de uso.
- [ ] Los mocks están correctamente verificados con `Times.Once()` / `Times.Never()`.
- [ ] `Dispose()` anula todos los campos del test (SUT y mocks).
- [ ] Los helpers `CreateTest*` generan datos válidos por defecto; los tests de error mutan solo el campo relevante.
- [ ] Los tests pasan con `dotnet test` sin warnings del analizador.
