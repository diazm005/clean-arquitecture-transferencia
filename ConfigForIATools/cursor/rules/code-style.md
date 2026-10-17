# Code Style Guidelines

## Naming Conventions

- **Types/Classes:** PascalCase
  ```csharp
  public class UserRegisterCase { }
  public interface IUserPort { }
  ```

- **Methods:** PascalCase
  ```csharp
  public async Task<EasyResult<User>> RegisterAsync(...) { }
  ```

- **Variables/Parameters:** camelCase
  ```csharp
  var userId = "123";
  string email = request.Email;
  ```

- **Interfaces:** I prefix
  ```csharp
  IUserPort, IPaymentPort, ICachePort
  ```

- **Async Methods:** Async suffix
  ```csharp
  RegisterAsync, GetUserAsync, CreateAsync
  ```

- **DTOs/Adapters:** [Feature]Adapter, [Feature]RequestAdapter
  ```csharp
  UserRegisterAdapter, UserRegisterRequestAdapter
  ```

## Formatting

- **Indentation:** 4 spaces (NEVER tabs)
- **Line Length:** Max 120 columns
- **Braces:** Allman style (opening on new line)
  ```csharp
  public class MyClass
  {
      public void MyMethod()
      {
          if (condition)
          {
              // code
          }
      }
  }
  ```

- **Using Statements:** Organized alphabetically
  ```csharp
  using System;
  using System.Collections.Generic;
  using Application.Adapters;
  using Domain.Interfaces;
  ```

## Code Patterns

### Result Pattern (REQUIRED)
```csharp
public async Task<EasyResult<UserDto>> RegisterAsync(
    RegisterRequest req,
    CancellationToken ct = default)
{
    var validation = FluentValidationExecutor.Execute(_validator, req);
    if (validation.IsFailure) return validation;

    var result = await _userPort.CreateAsync(req, ct);
    return result.IsSuccess
        ? EasyResult<UserDto>.Success(result.Value)
        : EasyResult<UserDto>.Failure(result.Error);
}
```

### Async Methods with CancellationToken
```csharp
public async Task<User> GetUserAsync(string id, CancellationToken ct = default)
{
    return await _collection.Find(f => f.Id == id).FirstOrDefaultAsync(ct);
}
```

### FluentValidation
```csharp
public class UserRegisterValidator : AbstractValidator<RegisterRequest>
{
    public UserRegisterValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email required")
            .EmailAddress().WithMessage("Invalid email");
    }
}
```

### Logging (Structured, never interpolation)
```csharp
// ✅ GOOD
_logger.LogInformation("User registered: {UserId} {Email}", userId, email);

// ❌ BAD
_logger.LogInformation($"User registered: {userId} {email}");
```

## Constructor Injection
```csharp
public class UserRegisterCase
{
    private readonly IUserPort _userPort;
    private readonly ILogger<UserRegisterCase> _logger;

    public UserRegisterCase(IUserPort userPort, ILogger<UserRegisterCase> logger)
    {
        _userPort = userPort;
        _logger = logger;
    }
}
```

---

See .cursorrules for build requirements.
