# Code Style Rules

All code MUST follow these strict style rules. Build system enforces with `TreatWarningsAsErrors=true`.

## Naming Conventions

### Types (PascalCase)

```csharp
public class UserRegisterCase { }
public record UserResponse { }
public interface IUserPort { }
public enum UserStatus { Active, Inactive }
public delegate void UserHandler(User user);
```

✅ **Rule:** PascalCase for all type names (class, interface, record, enum, delegate, struct)

### Methods (PascalCase)

```csharp
public async Task<EasyResult<User>> RegisterAsync(RegisterRequest request, CancellationToken ct)
{
    await ValidateAsync(request, ct);
}

private async Task ValidateAsync(RegisterRequest request, CancellationToken ct) { }
```

✅ **Rule:** PascalCase for all methods
✅ **Rule:** Suffix async methods with `Async`
✅ **Rule:** Start read-only methods with `Get`, `Fetch`, `Query`
✅ **Rule:** Start write methods with `Create`, `Update`, `Delete`, `Register`

### Variables & Parameters (camelCase)

```csharp
public async Task<EasyResult<User>> RegisterAsync(RegisterRequest request, CancellationToken ct)
{
    var userEmail = request.Email;
    const int maxRetries = 3;
    var result = await _userPort.CreateAsync(request, ct);

    foreach (var error in result.Errors)
    {
        ProcessError(error);
    }
}
```

✅ **Rule:** camelCase for all variables, parameters, properties (except constants)
✅ **Rule:** Use descriptive names: `userEmail` not `ue`, `maxRetries` not `mr`
✅ **Rule:** CONSTANT_CASE for compile-time constants

### Interfaces (I Prefix)

```csharp
public interface IUserPort { }
public interface IUserRegisterCase { }
public interface ILogger { }

// NOT:
public interface UserPort { }     // ❌ Wrong
public interface UserPortInterface { }  // ❌ Wrong
```

✅ **Rule:** ALL interfaces start with `I`
✅ **Rule:** No `Interface` suffix (it's redundant)

### Private Fields (_ Prefix)

```csharp
public class UserRegisterCase
{
    private readonly IUserPort _userPort;
    private readonly ILogger<UserRegisterCase> _logger;
    private int _retryCount;
}
```

✅ **Rule:** Private fields prefixed with `_`
✅ **Rule:** Use `_` for readonly dependencies

## Formatting Rules

### Indentation

```csharp
namespace Application.UseCases
{
    public class UserRegisterCase
    {
        private readonly IUserPort _userPort;

        public async Task<EasyResult<User>> RegisterAsync(
            RegisterRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
            {
                return EasyResult<User>.Failure("Request required");
            }
        }
    }
}
```

✅ **Rule:** 4 spaces per indentation level (never tabs)
✅ **Rule:** Align continuation lines at opening delimiter

### Line Length

```csharp
// ✅ Preferred (120 chars max)
public async Task<EasyResult<User>> RegisterAsync(
    RegisterRequest request,
    CancellationToken ct = default)
{
    var result = await _userPort.CreateAsync(request, ct);
}

// ❌ Avoid (exceeds 120 chars)
public async Task<EasyResult<User>> RegisterAsync(RegisterRequest request, CancellationToken ct = default) { var result = await _userPort.CreateAsync(request, ct); }
```

✅ **Rule:** Maximum 120 characters per line
✅ **Rule:** Break long signatures across multiple lines

### Braces (Allman Style)

```csharp
// ✅ Correct
public class UserRegisterCase
{
    public async Task<EasyResult<User>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (request == null)
        {
            return EasyResult<User>.Failure("Invalid request");
        }

        try
        {
            var result = await _userPort.CreateAsync(request, ct);
            return result;
        }
        catch (Exception ex)
        {
            return EasyResult<User>.Failure(ex.Message);
        }
    }
}

// ❌ Wrong (K&R style)
public class UserRegisterCase {
    public async Task<EasyResult<User>> RegisterAsync(RegisterRequest request, CancellationToken ct) {
        if (request == null) {
            return EasyResult<User>.Failure("Invalid request");
        }
    }
}
```

✅ **Rule:** Opening brace on new line (Allman style)
✅ **Rule:** Always use braces, even for single statements

### Spacing

```csharp
// ✅ Correct
public class Example
{
    private int value;

    public void Method(int parameter)
    {
        var result = value + parameter;
        var items = new List<int> { 1, 2, 3 };

        for (int i = 0; i < 10; i++)
        {
            DoSomething();
        }
    }
}

// ❌ Wrong
public class Example{
    private int value;
    public void Method(int parameter){
        var result=value+parameter;
        var items=new List<int>{1,2,3};
        for(int i=0;i<10;i++){
            DoSomething();
        }
    }
}
```

✅ **Rule:** Space around operators: `a + b`, not `a+b`
✅ **Rule:** Space after keywords: `if (condition)`, not `if(condition)`
✅ **Rule:** Space after comma: `Method(a, b)`, not `Method(a,b)`
✅ **Rule:** No space before parenthesis in method calls: `Method()`, not `Method ()`
✅ **Rule:** Space around generic brackets: `List<int>`, not `List<int >`

### Blank Lines

```csharp
public class UserRegisterCase : IUserRegisterCase
{
    private readonly IUserPort _userPort;
    private readonly ILogger<UserRegisterCase> _logger;

    public UserRegisterCase(IUserPort userPort, ILogger<UserRegisterCase> logger)
    {
        _userPort = userPort;
        _logger = logger;
    }

    public async Task<EasyResult<User>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (request == null)
        {
            return EasyResult<User>.Failure("Invalid request");
        }

        var exists = await _userPort.ExistsAsync(request.Email, ct);
        if (exists)
        {
            return EasyResult<User>.Failure("Email already registered");
        }

        var result = await _userPort.CreateAsync(request, ct);
        return result;
    }
}
```

✅ **Rule:** One blank line between class members
✅ **Rule:** One blank line between logical sections in methods
✅ **Rule:** No blank lines at start/end of blocks

## C# Patterns

### Using Declarations

```csharp
// ✅ Prefer (C# 8.0+)
public async Task<User> GetUserAsync(int id, CancellationToken ct)
{
    using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync(ct);
    return await connection.QuerySingleAsync<User>("SELECT * FROM Users WHERE Id = @Id", new { Id = id });
}

// ❌ Legacy
using (var connection = new SqlConnection(_connectionString))
{
    await connection.OpenAsync(ct);
    return await connection.QuerySingleAsync<User>("SELECT * FROM Users WHERE Id = @Id", new { Id = id });
}
```

✅ **Rule:** Use `using var` for single-line resource cleanup
✅ **Rule:** Use `using (...)` only for complex scopes

### Expression Bodied Members

```csharp
// ✅ Preferred for simple members
public string Email => _email;
public bool IsValid => _email?.Length > 0;

// For complex logic, use block body
public string GetFullName()
{
    if (string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(LastName))
    {
        return string.Empty;
    }
    return $"{FirstName} {LastName}";
}
```

✅ **Rule:** Use expression body (`=>`) for simple property getters and one-liners
✅ **Rule:** Use block body for any logic more complex than a single expression

### Null Coalescing & Conditionals

```csharp
// ✅ Modern C#
public string? GetName() => _name ?? "Unknown";
public User? GetUser(int id) => _users.FirstOrDefault(u => u.Id == id);
public void Process(string? input)
{
    var value = input ?? "default";
    if (input is not null)
    {
        Process(input);
    }
}

// ❌ Avoid
public string GetName() => _name != null ? _name : "Unknown";
public void Process(string input)
{
    if (input == null)
    {
        return;
    }
}
```

✅ **Rule:** Use `??` (null coalescing) not ternary for nulls
✅ **Rule:** Use `is not null` instead of `!= null`
✅ **Rule:** Use nullable reference types: `string?`

### LINQ Style

```csharp
// ✅ Method chain style
var activeUsers = _users
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .Select(u => u.ToResponse())
    .ToList();

// ✅ Query expression for complex logic
var userEmails = from user in _users
                 where user.IsActive
                 select user.Email;

// ❌ Mixed styles
var users = _users.Where(u=>u.IsActive).OrderBy(u=>u.Name).Select(u=>u.ToResponse()).ToList();
```

✅ **Rule:** Use fluent method chain style for simple queries
✅ **Rule:** Use query expression syntax for complex operations
✅ **Rule:** Space around lambda: `u => u.IsActive`, not `u=>u.IsActive`

## Type Declarations

```csharp
// ✅ Prefer modern syntax
public record UserResponse(string Id, string Email, string Name);

public class User
{
    public string Id { get; init; }
    public string Email { get; init; }
    public string Name { get; init; }
}

public readonly struct Point
{
    public int X { get; init; }
    public int Y { get; init; }
}

// ❌ Avoid legacy patterns
public class User
{
    public string Id { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
}
```

✅ **Rule:** Use `record` for immutable DTOs
✅ **Rule:** Use `init` properties in classes for immutability
✅ **Rule:** Use `readonly struct` for value types

---

See CLAUDE.md for complete style guidelines.
