# Architecture Rules

## Hexagonal (Ports & Adapters) + Clean Architecture

System API 3.0 follows strict hexagonal architecture principles with clear layer separation.

### Layer Structure

```
┌─────────────────────────────────────────────┐
│ Presentation Layer (Controllers)            │
│ - Receive HTTP requests                     │
│ - Call use cases via Port interfaces        │
│ - Return HTTP responses using helpers       │
└──────────────────┬──────────────────────────┘
                   │
                   ↓ Calls Port interface
┌─────────────────────────────────────────────┐
│ Application Layer (Use Cases)               │
│ - Implement business logic                  │
│ - Validate input with FluentValidation      │
│ - Return EasyResult<T> (never throw)        │
│ - Depend only on Port interfaces (Domain)   │
└──────────────────┬──────────────────────────┘
                   │
                   ↓ Uses Port interface
┌─────────────────────────────────────────────┐
│ Domain Layer (Entities, Ports)              │
│ - Pure business logic (NO external deps)    │
│ - Entities and Value Objects                │
│ - Port interfaces (contracts)               │
│ - ZERO imports from Infrastructure          │
└──────────────────┬──────────────────────────┘
                   │
                   ↑ Implements Port
┌─────────────────────────────────────────────┐
│ Infrastructure Layer (Adapters)             │
│ - Implement Port interfaces                 │
│ - Database access (Mongo, SQL Server)       │
│ - External API calls (HTTP, Kafka, S3)      │
│ - Cache access (Redis)                      │
└─────────────────────────────────────────────┘
```

### Dependency Rule

**Dependencies MUST flow inward** — only toward Domain:

```
Infrastructure → Domain
Application → Domain + Infrastructure
Controllers → Application (via Ports)
Domain → NOTHING
```

**Violation Examples:**

❌ Domain importing `MongoDB.Driver`
❌ Domain importing `Dapper`
❌ Application importing `StackExchange.Redis`
❌ Controller directly calling Infrastructure (should use Port)
❌ Infrastructure throwing business exceptions (should use Result)

### Port Definition (Domain Layer)

```csharp
// Domain/Interfaces/IUserPort.cs
namespace Domain.Interfaces;

public interface IUserPort
{
    Task<EasyResult<User>> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<EasyResult<User>> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> ExistsAsync(string email, CancellationToken ct = default);
}
```

**Rules:**
- Define in `Domain/Interfaces/`
- Prefix with `I`
- Parameters: domain entities/values + `CancellationToken ct = default`
- Returns: `EasyResult<T>`, `Task<EasyResult<T>>`, or `Task<T>`
- No implementation details

### Port Implementation (Infrastructure Layer)

```csharp
// MongoDbInfrastructure/Adapters/UserMongoPort.cs
namespace MongoDbInfrastructure.Adapters;

public class UserMongoPort : IUserPort
{
    private readonly IMongoCollection<UserDocument> _users;

    public UserMongoPort(IMongoCollection<UserDocument> users)
    {
        _users = users;
    }

    public async Task<EasyResult<User>> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        try
        {
            var document = new UserDocument { Email = request.Email };
            await _users.InsertOneAsync(document, cancellationToken: ct);
            return EasyResult<User>.Success(document.ToEntity());
        }
        catch (Exception ex)
        {
            return EasyResult<User>.Failure(ex.Message);
        }
    }
}
```

**Rules:**
- Implement in `*Infrastructure/Adapters/`
- Suffix class name with adapter type (e.g., `UserMongoPort`, `UserSqlPort`)
- Handle all exceptions, return `EasyResult<T>`
- Propagate `CancellationToken` to all I/O operations
- Never throw domain exceptions
- Map to/from domain entities

### Use Case (Application Layer)

```csharp
// Application/UseCases/UserRegisterCase.cs
namespace Application.UseCases;

public class UserRegisterCase : IUserRegisterCase
{
    private readonly IUserPort _userPort;
    private readonly ILogger<UserRegisterCase> _logger;

    public UserRegisterCase(IUserPort userPort, ILogger<UserRegisterCase> logger)
    {
        _userPort = userPort;
        _logger = logger;
    }

    public async Task<EasyResult<UserResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken ct = default)
    {
        // Validate
        var validation = new RegisterValidator().Validate(request);
        if (!validation.IsValid)
        {
            return EasyResult<UserResponse>.Failure(validation.Errors.First().ErrorMessage);
        }

        // Business logic
        var exists = await _userPort.ExistsAsync(request.Email, ct);
        if (exists)
        {
            return EasyResult<UserResponse>.Failure("Email already registered");
        }

        // Call port
        var result = await _userPort.CreateAsync(request, ct);
        return result.IsSuccess
            ? EasyResult<UserResponse>.Success(result.Value.ToResponse())
            : EasyResult<UserResponse>.Failure(result.Error);
    }
}
```

**Rules:**
- Return `Task<EasyResult<T>>` (never throw)
- Validate input first
- Call Ports for external access
- Map domain → responses (DTOs)
- Propagate CancellationToken
- Log actions (not sensitive data)

### Controller (Presentation Layer)

```csharp
// SystemAPI/Controllers/UserController.cs
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserRegisterCase _registerCase;

    public UserController(IUserRegisterCase registerCase)
    {
        _registerCase = registerCase;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        CancellationToken ct)
    {
        var result = await _registerCase.RegisterAsync(request, ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(result.Error);
    }
}
```

**Rules:**
- Call use cases via Port interfaces (NOT directly)
- Inject via constructor
- Map HTTP → domain requests
- Map results → HTTP responses
- Use `EasyResponseHelper` or `EasyBianResponseHelper` for consistent responses
- Propagate CancellationToken from HttpContext

### Port Registration (DI)

```csharp
// SystemAPI/Configuration/ApplicationSetting.cs
public static class ApplicationSetting
{
    public static void ConfigureApplicationDependencies(this IServiceCollection services)
    {
        // Register Use Cases
        services.AddScoped<IUserRegisterCase, UserRegisterCase>();

        // Register Ports → Adapters (choose ONE for each port)
        // MongoDB
        services.AddScoped<IUserPort, UserMongoPort>();

        // OR SQL Server
        // services.AddScoped<IUserPort, UserSqlPort>();
    }
}
```

**Rules:**
- One Port → One Adapter binding per environment
- Switch adapters by changing configuration, not code
- Never register multiple adapters for same port (unless intentional)
- Register in ApplicationSetting.cs only

### Common Violations

| Violation | ❌ Wrong | ✅ Correct |
|-----------|---------|-----------|
| Domain layer dependency | `using MongoDB.Driver;` | Zero external imports |
| Direct adapter use | `var user = new UserMongoPort(...);` | Inject `IUserPort` |
| Throwing from adapter | `throw new DomainException(...)` | Return `EasyResult<T>.Failure(...)` |
| Calling adapter from controller | `_mongoPort.GetUser()` | `_registerCase.RegisterAsync(...)` |
| Missing CancellationToken | `await _userPort.GetAsync()` | `await _userPort.GetAsync(ct)` |
| Business logic in controller | Logic in action method | Move to use case |
| No validation | Direct use case call | Validate first, then call |
| Adapter ignoring cancellation | `await collection.Find(...)` | `await collection.Find(..., ct)` |

---

See CLAUDE.md for complete architecture guide.
