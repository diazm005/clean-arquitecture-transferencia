# Testing Rules

## Test Naming Pattern

**Format:** `[MethodName]_Should[Behavior]_When[Condition]`

```csharp
[Fact]
public async Task RegisterAsync_ShouldReturnSuccess_WhenValidRequestProvided()
{
    // Test implementation
}

[Fact]
public async Task RegisterAsync_ShouldReturnFailure_WhenEmailAlreadyExists()
{
    // Test implementation
}
```

## Test Structure (Arrange/Act/Assert)

```csharp
[Fact]
public async Task RegisterAsync_ShouldReturnSuccess_WhenValidRequestProvided()
{
    // ARRANGE: Setup test data and mocks
    var request = new RegisterRequest { Email = "test@example.com" };
    _mockPort.Setup(x => x.CreateAsync(It.IsAny<RegisterRequest>(), default))
        .ReturnsAsync(EasyResult<User>.Success(new User { Id = "123" }));

    // ACT: Execute the method being tested
    var result = await _useCase.RegisterAsync(request, default);

    // ASSERT: Verify the results
    Assert.True(result.IsSuccess);
    Assert.NotNull(result.Value);
    _mockPort.Verify(x => x.CreateAsync(request, default), Times.Once);
}
```

## Coverage Requirements

### ✅ Must Test
- Happy path (success scenario)
- Each error/failure case
- Validation errors
- Domain logic edge cases
- Mocked dependency calls

### Example: Multiple Test Cases
```csharp
[Fact]
public async Task RegisterAsync_ShouldReturnSuccess_WhenValidRequest() { ... }

[Fact]
public async Task RegisterAsync_ShouldReturnFailure_WhenEmailEmpty() { ... }

[Fact]
public async Task RegisterAsync_ShouldReturnFailure_WhenEmailInvalid() { ... }

[Fact]
public async Task RegisterAsync_ShouldReturnFailure_WhenEmailExists() { ... }

[Fact]
public async Task RegisterAsync_ShouldReturnFailure_WhenPortThrows() { ... }
```

## Mock Verification

```csharp
// Verify called exactly once
_mockPort.Verify(x => x.CreateAsync(It.IsAny<RegisterRequest>(), default),
    Times.Once);

// Verify never called
_mockPort.Verify(x => x.CreateAsync(It.IsAny<RegisterRequest>(), default),
    Times.Never);

// Verify called at least once
_mockPort.Verify(x => x.CreateAsync(It.IsAny<RegisterRequest>(), default),
    Times.AtLeastOnce);
```

## Test Class Structure

```csharp
public class UserRegisterCaseTests : IDisposable
{
    private readonly Mock<IUserPort> _mockPort;
    private readonly UserRegisterCase _useCase;

    public UserRegisterCaseTests()
    {
        _mockPort = new Mock<IUserPort>();
        _useCase = new UserRegisterCase(_mockPort.Object, NullLogger<UserRegisterCase>.Instance);
    }

    // Tests here...

    public void Dispose()
    {
        _mockPort?.Reset();
    }
}
```

## Tools & Framework

- **Framework:** xUnit
- **Mocking:** Moq
- **Assertions:** xUnit Assert methods
- **Logger:** NullLogger<T>.Instance (never mock logging)

## Test Execution

```bash
# Run all tests
dotnet test Application.Tests/Application.Tests.csproj

# Run specific test class
dotnet test --filter "FullyQualifiedName~UserRegisterCaseTests"

# Run specific test
dotnet test --filter "FullyQualifiedName~UserRegisterCaseTests.RegisterAsync_ShouldReturnSuccess_WhenValidRequestProvided"
```

---

See CLAUDE.md for complete testing guide.
