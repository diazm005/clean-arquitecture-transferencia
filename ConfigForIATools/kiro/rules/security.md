# Security Rules

Security is not optional. All rules must be followed before commit.

## Secrets & Configuration

### ❌ FORBIDDEN: Hardcoded Secrets

```csharp
// ❌ NEVER
private const string ApiKey = "sk-proj-abc123xyz";
private const string DbPassword = "SecurePassword123";
var connectionString = "mongodb://user:password@localhost:27017";
var jwtSecret = "my-super-secret-key";
```

### ✅ REQUIRED: Environment Variables

```csharp
// ✅ Development: appsettings.Development.json
{
  "Jwt": {
    "Secret": "dev-secret-for-local-testing-only",
    "Issuer": "https://cognito-dev.amazonaws.com"
  },
  "Database": {
    "ConnectionString": "mongodb://localhost:27017"
  }
}

// ✅ Production: Environment Variables
public class JwtOptions
{
    public string Secret { get; set; } = Environment.GetEnvironmentVariable("JWT_SECRET")
        ?? throw new InvalidOperationException("JWT_SECRET not configured");
    public string Issuer { get; set; } = Environment.GetEnvironmentVariable("JWT_ISSUER")
        ?? throw new InvalidOperationException("JWT_ISSUER not configured");
}

// ✅ In appsettings.json with environment binding
{
  "Jwt": {
    "Secret": "${JWT_SECRET}",
    "Issuer": "${JWT_ISSUER}"
  }
}
```

**Rules:**
- Never commit secrets
- Use `appsettings.Development.json` for local testing (git-ignored)
- Use environment variables in production
- Use Azure Key Vault / AWS Secrets Manager for sensitive data
- Add `.env` files to `.gitignore`

## SQL Injection Prevention

### ❌ FORBIDDEN: String Concatenation

```csharp
// ❌ NEVER — SQL Injection vulnerability
public async Task<User> GetUserAsync(string email)
{
    var sql = $"SELECT * FROM Users WHERE Email = '{email}'";
    return await _connection.QuerySingleAsync<User>(sql);
}

// ❌ NEVER — String interpolation
public async Task<User> GetUserAsync(string email)
{
    var sql = $"SELECT * FROM Users WHERE Email = {email}";
    return await _connection.QuerySingleAsync<User>(sql);
}
```

### ✅ REQUIRED: Parameterized Queries

```csharp
// ✅ Dapper with named parameters
public async Task<User> GetUserAsync(string email, CancellationToken ct)
{
    const string sql = "SELECT * FROM Users WHERE Email = @Email";
    return await _connection.QuerySingleAsync<User>(
        sql,
        new { Email = email },
        commandTimeout: 30);
}

// ✅ Dapper with positional parameters
public async Task<List<User>> GetUsersByStatusAsync(string status, CancellationToken ct)
{
    const string sql = "SELECT * FROM Users WHERE Status = @Status ORDER BY CreatedAt DESC";
    return (await _connection.QueryAsync<User>(
        sql,
        new { Status = status },
        commandTimeout: 30)).ToList();
}

// ✅ Entity Framework Core
public async Task<User> GetUserAsync(string email, CancellationToken ct)
{
    return await _context.Users
        .Where(u => u.Email == email)
        .FirstOrDefaultAsync(ct);
}

// ✅ MongoDB
public async Task<User> GetUserAsync(string email, CancellationToken ct)
{
    var filter = Builders<User>.Filter.Eq(u => u.Email, email);
    return await _users.Find(filter).FirstOrDefaultAsync(ct);
}
```

**Rules:**
- Always use parameterized queries
- Use `@ParameterName` syntax with Dapper
- Use LINQ predicates with Entity Framework
- Use filter builders with MongoDB
- Never concatenate user input into SQL
- Never use string interpolation in SQL

## PII (Personally Identifiable Information) in Logs

### ❌ FORBIDDEN: Sensitive Data in Logs

```csharp
// ❌ NEVER — Logs PII
_logger.LogInformation("User registered: {Email} with password {Password}", user.Email, user.Password);
_logger.LogError("Failed to authenticate user {Email}: {Token}", request.Email, jwtToken);
_logger.LogWarning("User {Phone} requested reset", user.PhoneNumber);

public async Task<EasyResult<User>> RegisterAsync(RegisterRequest request, CancellationToken ct)
{
    _logger.LogInformation("Registering user: {Request}", request); // ❌ Logs email
    var result = await _userPort.CreateAsync(request, ct);
    if (result.IsSuccess)
    {
        _logger.LogInformation("User registered: {User}", result.Value); // ❌ Logs all user data
    }
}
```

### ✅ REQUIRED: Log Only Non-Sensitive Data

```csharp
// ✅ Correct — Logs only safe data
_logger.LogInformation("User registration initiated for user ID {UserId}", user.Id);
_logger.LogError("Authentication failed for user ID {UserId}: error code {ErrorCode}", userId, errorCode);
_logger.LogWarning("Multiple login attempts detected for user ID {UserId}", userId);

public async Task<EasyResult<User>> RegisterAsync(RegisterRequest request, CancellationToken ct)
{
    _logger.LogInformation("User registration initiated");
    var result = await _userPort.CreateAsync(request, ct);
    if (result.IsSuccess)
    {
        _logger.LogInformation("User registration succeeded with ID {UserId}", result.Value.Id);
    }
    else
    {
        _logger.LogError("User registration failed: {Error}", result.Error);
    }
}

// ✅ If you must log request data, mask it
var maskedEmail = request.Email?.Substring(0, 3) + "***@example.com";
_logger.LogInformation("Registration for email {MaskedEmail}", maskedEmail);
```

**PII NOT to log:**
- Passwords, passphrases, security questions/answers
- Email addresses, phone numbers
- Social Security Numbers, passport numbers
- Credit card numbers, banking information
- JWT tokens, API keys, session IDs
- IP addresses (context-dependent)
- Full names (log user ID instead)

**OK to log:**
- User IDs (internal identifiers, never real names)
- Action types: "register", "login", "delete"
- Error codes and descriptions
- Request duration/performance metrics
- Timestamps and context

## Authentication & Authorization

### JWT (AWS Cognito)

```csharp
// ✅ Configure JWT
public static void ConfigureJwtAuthentication(this IServiceCollection services)
{
    var jwtOptions = new JwtOptions();
    services.Configure<JwtOptions>(options =>
    {
        options.Issuer = Environment.GetEnvironmentVariable("JWT_ISSUER")
            ?? throw new InvalidOperationException("JWT_ISSUER not configured");
        options.Audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE")
            ?? throw new InvalidOperationException("JWT_AUDIENCE not configured");
    });

    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = jwtOptions.Issuer;
            options.Audience = jwtOptions.Audience;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true
            };
        });

    services.AddAuthorizationBuilder()
        .AddPolicy("ReadScope", policy => policy.RequireClaim("scope", "ReadScope"))
        .AddPolicy("WriteScope", policy => policy.RequireClaim("scope", "WriteScope"));
}

// ✅ Protect endpoints
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    [HttpGet("{id}")]
    [Authorize(Policy = "ReadScope")]
    public async Task<IActionResult> GetAsync(string id, CancellationToken ct)
    {
        // Implementation
    }

    [HttpPost]
    [Authorize(Policy = "WriteScope")]
    public async Task<IActionResult> CreateAsync([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        // Implementation
    }
}
```

### Header Auth (x-scope)

```csharp
// ✅ Arify Header Authentication
[HttpGet("public")]
public IActionResult GetPublic() => Ok("Public endpoint");

[HttpPost("protected")]
[ArifyAuthorize("WriteScope")]
public async Task<IActionResult> CreateProtected([FromBody] CreateRequest request, CancellationToken ct)
{
    // Implementation
}

// ✅ Custom attribute
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ArifyAuthorizeAttribute : Attribute
{
    public string RequiredScope { get; }

    public ArifyAuthorizeAttribute(string requiredScope)
    {
        RequiredScope = requiredScope;
    }
}
```

## CancellationToken Propagation

### ❌ FORBIDDEN: Ignoring CancellationToken

```csharp
// ❌ NEVER — Ignores cancellation
public async Task<User> GetUserAsync(string id)
{
    return await _userPort.GetAsync(id); // Missing CancellationToken
}

public async Task<List<User>> GetAllAsync()
{
    await Task.Delay(5000); // Missing CancellationToken
    return await _userPort.GetAllAsync();
}

// ❌ NEVER — Swallows token
public async Task<User> GetUserAsync(string id, CancellationToken ct)
{
    return await _userPort.GetAsync(id); // Doesn't pass ct
}
```

### ✅ REQUIRED: Propagate CancellationToken Through Chain

```csharp
// ✅ Add CancellationToken to ALL async methods
public interface IUserPort
{
    Task<EasyResult<User>> GetAsync(string id, CancellationToken ct = default);
    Task<List<User>> GetAllAsync(CancellationToken ct = default);
}

public class UserRegisterCase : IUserRegisterCase
{
    // ✅ Add to method signature
    public async Task<EasyResult<User>> RegisterAsync(
        RegisterRequest request,
        CancellationToken ct = default)
    {
        // ✅ Pass to ALL async calls
        var exists = await _userPort.ExistsAsync(request.Email, ct);
        if (exists)
        {
            return EasyResult<User>.Failure("Email exists");
        }

        var user = await _userPort.CreateAsync(request, ct);
        return user;
    }

    private async Task ValidateAsync(RegisterRequest request, CancellationToken ct)
    {
        // ✅ Propagate down
        await _userPort.GetAsync(request.Email, ct);
    }
}

// ✅ Controller passes HttpContext.RequestAborted
[HttpPost("register")]
public async Task<IActionResult> RegisterAsync(
    [FromBody] RegisterRequest request,
    CancellationToken ct) // ASP.NET Core provides this from HttpContext
{
    var result = await _registerCase.RegisterAsync(request, ct);
    return Ok(result);
}

// ✅ In infrastructure adapters
public async Task<EasyResult<User>> CreateAsync(
    CreateUserRequest request,
    CancellationToken ct = default)
{
    try
    {
        // ✅ Pass to I/O operations
        await _connection.ExecuteAsync(sql, parameters, commandTimeout: 30, cancellationToken: ct);
        await _users.InsertOneAsync(document, cancellationToken: ct);
        await _redisCache.SetAsync(key, value, null, ct);
    }
    catch (OperationCanceledException)
    {
        // Handle graceful cancellation
        return EasyResult<User>.Failure("Operation cancelled");
    }
}
```

**Rules:**
- Add `CancellationToken ct = default` to ALL async methods
- Pass `ct` to every `await` expression
- Handle `OperationCanceledException` gracefully
- Never use `.Result` or `.Wait()` (blocks thread)
- Test cancellation with timeout scenarios

## Dependency Injection Security

### ❌ FORBIDDEN: Unsafe Registrations

```csharp
// ❌ NEVER — Creates singleton of transient dependency
services.AddSingleton<IUserPort>(_userPort); // _userPort instance could be shared unsafely

// ❌ NEVER — Service locator anti-pattern
public class UserController
{
    private readonly IServiceProvider _provider;
    public UserController(IServiceProvider provider)
    {
        _provider = provider; // ❌ Circumvents DI safety
        var port = _provider.GetService<IUserPort>();
    }
}

// ❌ NEVER — Static factories (thread safety issues)
public class ServiceLocator
{
    public static IUserPort Port { get; set; }
}
```

### ✅ REQUIRED: Safe Registrations

```csharp
// ✅ Proper lifetimes
services.AddScoped<IUserPort, UserMongoPort>();    // Per-request
services.AddSingleton<IRedisCache, RedisCache>();  // One instance
services.AddTransient<IValidator, UserValidator>(); // New each time

// ✅ Inject dependencies, don't service-locate
public class UserController
{
    private readonly IUserRegisterCase _registerCase;

    public UserController(IUserRegisterCase registerCase)
    {
        _registerCase = registerCase;
    }
}
```

---

See CLAUDE.md for complete security guidelines.
