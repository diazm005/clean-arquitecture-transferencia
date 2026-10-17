# Security Rules

## Secrets Management

### ❌ FORBIDDEN
```csharp
// Hardcoded in code
string password = "MyPassword123";

// Hardcoded in config
{
  "Database": {
    "Password": "SecretPassword"
  }
}
```

### ✅ REQUIRED
```csharp
// Use environment variables
var password = Environment.GetEnvironmentVariable("DB_PASSWORD");

// Or secure vault
var secret = await keyVault.GetSecretAsync("db-password");
```

## SQL Injection Prevention

### ❌ FORBIDDEN
```csharp
var sql = $"SELECT * FROM Users WHERE Email = '{email}'";
var user = connection.QueryFirstOrDefault<User>(sql);
```

### ✅ REQUIRED
```csharp
var sql = "SELECT * FROM Users WHERE Email = @Email";
var user = connection.QueryFirstOrDefault<User>(sql, new { Email = email });
```

## PII in Logs

### ❌ FORBIDDEN
```csharp
_logger.LogInformation("User login: {Email} {Password}", email, password);
_logger.LogError("DB error: {ConnectionString}", connString);
```

### ✅ REQUIRED
```csharp
_logger.LogInformation("User login attempt: {UserId}", userId);
_logger.LogError("DB connection failed for host {Host}", hostname);
```

## Authentication

### JWT (AWS Cognito)
```csharp
[Authorize(Policy = "ReadScope")]
[HttpGet]
public async Task<IActionResult> GetUser(string id) { ... }
```

### Header Auth (x-scope)
```csharp
[ArifyAuthorize("WriteScope")]
[HttpPost]
public async Task<IActionResult> CreateUser(...) { ... }
```

## CancellationToken Propagation

### ❌ WRONG
```csharp
public async Task<User> GetUserAsync(string id)
{
    return await _collection.Find(f => f.Id == id).FirstOrDefaultAsync();
}
```

### ✅ CORRECT
```csharp
public async Task<User> GetUserAsync(string id, CancellationToken ct = default)
{
    return await _collection.Find(f => f.Id == id).FirstOrDefaultAsync(ct);
}
```

## Critical Checks

- [ ] No hardcoded secrets in code or config
- [ ] All SQL parameterized with @Parameters
- [ ] No passwords/emails/tokens in logs
- [ ] CancellationToken in all async I/O
- [ ] Authentication on protected endpoints
- [ ] Input validation on all controllers
- [ ] Error responses don't leak internals

---

See .cursorrules for complete security guidelines.
