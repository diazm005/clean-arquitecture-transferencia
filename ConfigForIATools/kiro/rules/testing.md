# Testing Rules

All features must have comprehensive test coverage. Use xUnit + Moq following these rules.

## Test Naming Pattern

**Format:** `[MethodName]_Should[Behavior]_When[Condition]`

```csharp
[Fact]
public async Task RegisterAsync_ShouldReturnSuccess_WhenValidRequestProvided()
{
    // Happy path
}

[Fact]
public async Task RegisterAsync_ShouldReturnFailure_WhenEmailAlreadyExists()
{
    // Error case
}

[Fact]
public async Task RegisterAsync_ShouldReturnFailure_WhenEmailInvalid()
{
    // Validation case
}

[Fact]
public async Task RegisterAsync_ShouldReturnFailure_WhenPortThrows()
{
    // Exception handling
}

[Theory]
[InlineData("", false)]
[InlineData("invalid", false)]
[InlineData("test@example.com", true)]
public void IsValidEmail_ShouldValidateCorrectly_WhenEmailProvided(string email, bool expected)
{
    var result = UserValidator.IsValidEmail(email);
    Assert.Equal(expected, result);
}
```

**Rules:**
- Method name: What is being tested
- Should: What the expected behavior is
- When: Under what conditions
- Use `[Fact]` for single test case
- Use `[Theory]` with `[InlineData]` for parameterized tests
- One assertion concept per test (multiple assertions OK if testing same concept)

## Test Structure (Arrange/Act/Assert)

```csharp
[Fact]
public async Task RegisterAsync_ShouldReturnSuccess_WhenValidRequestProvided()
{
    // ===== ARRANGE =====
    // 1. Setup test data
    var request = new RegisterRequest
    {
        Email = "test@example.com",
        Password = "SecurePassword123",
        Name = "Test User"
    };

    // 2. Create and configure mocks
    var mockUserPort = new Mock<IUserPort>();
    mockUserPort
        .Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(false); // User doesn't exist

    mockUserPort
        .Setup(x => x.CreateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(EasyResult<User>.Success(new User
        {
            Id = "user-123",
            Email = request.Email,
            Name = request.Name
        }));

    // 3. Create SUT (System Under Test)
    var useCase = new UserRegisterCase(
        mockUserPort.Object,
        NullLogger<UserRegisterCase>.Instance);

    // ===== ACT =====
    // Execute the method being tested
    var result = await useCase.RegisterAsync(request, CancellationToken.None);

    // ===== ASSERT =====
    // 1. Verify result
    Assert.NotNull(result);
    Assert.True(result.IsSuccess);
    Assert.NotNull(result.Value);
    Assert.Equal("user-123", result.Value.Id);
    Assert.Equal("test@example.com", result.Value.Email);

    // 2. Verify mock interactions
    mockUserPort.Verify(
        x => x.ExistsAsync("test@example.com", It.IsAny<CancellationToken>()),
        Times.Once,
        "Should check if email exists");

    mockUserPort.Verify(
        x => x.CreateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()),
        Times.Once,
        "Should call CreateAsync exactly once");
}
```

**Rules:**
- **ARRANGE:** Setup all data and mocks before acting
- **ACT:** Call the method exactly once
- **ASSERT:** Verify results and mock interactions separately
- Use `NullLogger<T>.Instance` (never mock logger)
- Add explanation message to Verify calls
- Keep tests focused (one behavior per test)

## Test Coverage Requirements

### ✅ Must Test

For every use case, test:

1. **Happy Path (Success)**
   ```csharp
   [Fact]
   public async Task RegisterAsync_ShouldReturnSuccess_WhenValidRequestProvided()
   {
       // Valid input → Success result
   }
   ```

2. **Each Validation Error**
   ```csharp
   [Fact]
   public async Task RegisterAsync_ShouldReturnFailure_WhenEmailEmpty()
   {
       // Email validation error
   }

   [Fact]
   public async Task RegisterAsync_ShouldReturnFailure_WhenEmailInvalid()
   {
       // Email format error
   }

   [Fact]
   public async Task RegisterAsync_ShouldReturnFailure_WhenPasswordTooShort()
   {
       // Password length validation
   }
   ```

3. **Business Logic Errors**
   ```csharp
   [Fact]
   public async Task RegisterAsync_ShouldReturnFailure_WhenEmailAlreadyExists()
   {
       // Email duplication check
   }

   [Fact]
   public async Task RegisterAsync_ShouldReturnFailure_WhenQuotaExceeded()
   {
       // Business rule violation
   }
   ```

4. **Port/Adapter Failures**
   ```csharp
   [Fact]
   public async Task RegisterAsync_ShouldReturnFailure_WhenPortReturnsFailure()
   {
       // Port returns EasyResult.Failure
       mockUserPort
           .Setup(x => x.CreateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(EasyResult<User>.Failure("Database error"));
   }

   [Fact]
   public async Task RegisterAsync_ShouldReturnFailure_WhenPortThrows()
   {
       // Port throws unexpected exception
       mockUserPort
           .Setup(x => x.CreateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new IOException("Connection failed"));
   }
   ```

5. **Edge Cases**
   ```csharp
   [Fact]
   public async Task RegisterAsync_ShouldHandleNull_WhenRequestIsNull()
   {
       // Null parameter handling
   }

   [Fact]
   public async Task RegisterAsync_ShouldHandleCancellation_WhenTokenCancelled()
   {
       // CancellationToken propagation
   }
   ```

## Mock Verification Patterns

```csharp
// Verify called exactly once
mockPort.Verify(
    x => x.CreateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()),
    Times.Once);

// Verify called specific number of times
mockPort.Verify(
    x => x.LogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
    Times.Exactly(3));

// Verify never called
mockPort.Verify(
    x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
    Times.Never);

// Verify called at least once
mockPort.Verify(
    x => x.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
    Times.AtLeastOnce);

// Verify with specific parameters
mockPort.Verify(
    x => x.CreateAsync(
        It.Is<CreateUserRequest>(r => r.Email == "test@example.com"),
        It.IsAny<CancellationToken>()),
    Times.Once);

// Verify no other calls made
mockPort.VerifyNoOtherCalls();

// Verify setup was not called with certain parameters
mockPort.Verify(
    x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
    Times.Never,
    "Should not delete during registration");
```

## Test Class Structure

```csharp
public class UserRegisterCaseTests : IDisposable
{
    // ===== Fields (readonly) =====
    private readonly Mock<IUserPort> _mockUserPort;
    private readonly Mock<IAuditPort> _mockAuditPort;
    private readonly UserRegisterCase _useCase;

    // ===== Constructor =====
    public UserRegisterCaseTests()
    {
        _mockUserPort = new Mock<IUserPort>();
        _mockAuditPort = new Mock<IAuditPort>();

        _useCase = new UserRegisterCase(
            _mockUserPort.Object,
            _mockAuditPort.Object,
            NullLogger<UserRegisterCase>.Instance);
    }

    // ===== Tests =====
    [Fact]
    public async Task RegisterAsync_ShouldReturnSuccess_WhenValidRequestProvided()
    {
        // Arrange
        var request = new RegisterRequest { Email = "test@example.com" };
        _mockUserPort
            .Setup(x => x.CreateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EasyResult<User>.Success(new User { Id = "123" }));

        // Act
        var result = await _useCase.RegisterAsync(request, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RegisterAsync_ShouldReturnFailure_WhenEmailAlreadyExists()
    {
        // Test implementation
    }

    // ===== Cleanup =====
    public void Dispose()
    {
        _mockUserPort?.Reset();
        _mockAuditPort?.Reset();
    }
}
```

**Rules:**
- Implement `IDisposable` for mock cleanup
- Use `readonly` for mock fields
- Create mocks in constructor
- Initialize SUT (System Under Test) in constructor
- Reset mocks in `Dispose()`
- One test class per use case class
- Namespace: `Application.Tests.UseCases`

## Tools & Framework

| Tool | Purpose |
|------|---------|
| **xUnit** | Test framework |
| **Moq** | Mock objects |
| **Assert** | Assertions (xUnit built-in) |
| **It.IsAny\<T\>()** | Match any parameter |
| **It.Is\<T\>(predicate)** | Match parameter by condition |
| **Times.Once/Never/AtLeastOnce** | Verification counts |

### Assert Examples

```csharp
// Basic assertions
Assert.NotNull(result);
Assert.Null(result);
Assert.True(condition);
Assert.False(condition);

// Equality
Assert.Equal(expected, actual);
Assert.NotEqual(expected, actual);
Assert.StrictEqual(expected, actual); // Type-safe equality

// Collections
Assert.Empty(list);
Assert.NotEmpty(list);
Assert.Single(list);
Assert.Contains(item, list);
Assert.DoesNotContain(item, list);

// Exceptions
var ex = Assert.Throws<ArgumentNullException>(() => Method(null));
Assert.Equal("parameter", ex.ParamName);

var ex = await Assert.ThrowsAsync<InvalidOperationException>(
    () => MethodAsync(CancellationToken.None));
```

## Test Execution

```bash
# Run all tests
dotnet test Application.Tests/Application.Tests.csproj

# Run specific test class
dotnet test Application.Tests/Application.Tests.csproj \
    --filter "FullyQualifiedName~UserRegisterCaseTests"

# Run specific test method
dotnet test Application.Tests/Application.Tests.csproj \
    --filter "FullyQualifiedName~UserRegisterCaseTests.RegisterAsync_ShouldReturnSuccess_WhenValidRequestProvided"

# Run with verbosity
dotnet test Application.Tests/Application.Tests.csproj --verbosity detailed

# Run with coverage (requires coverlet)
dotnet test Application.Tests/Application.Tests.csproj /p:CollectCoverage=true
```

## Common Pitfalls

| ❌ Avoid | ✅ Do Instead |
|---------|-------------|
| Mocking logger | Use `NullLogger<T>.Instance` |
| Testing implementation details | Test behavior and contracts |
| Sharing state between tests | Each test fully independent |
| Non-deterministic tests | Use fixed data, not random |
| Skipping error cases | Test every error path |
| Ignoring CancellationToken | Always pass `CancellationToken` |
| Incomplete setup | Arrange before Act |
| Testing external systems | Mock dependencies |
| Multiple assertions on different concepts | One concept per test |

---

See CLAUDE.md for complete testing guide.
