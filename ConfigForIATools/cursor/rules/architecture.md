# Architecture Rules

## Hexagonal (Ports & Adapters) + Clean Architecture

### Layer Structure

```
SystemAPI (Presentation)
    ↓
Application (Business Logic)
    ↓
Domain (Pure Business)
    ↓
Infrastructure (External Systems)
```

### Domain Layer
- **Contains:** Entities, Value Objects, Port Interfaces
- **Must NOT:** External dependencies, Infrastructure code
- **Example:** `IUserPort`, `User` entity

### Application Layer
- **Contains:** Use Cases, Validators, DTOs, Result Pattern
- **Must NOT:** Direct DB calls, HTTP clients, business logic
- **Example:** `UserRegisterCase`, `UserRegisterValidator`

### Infrastructure Layer
- **Contains:** Repositories, DB adapters, HTTP clients, Cache
- **Must NOT:** Business logic, validation rules
- **Example:** `MongoUserRepository implements IUserPort`

### Controllers
- **Contains:** HTTP routing, header extraction, DI
- **Must NOT:** Business logic, direct DB calls
- **Example:** Call use case via IPort interface

## Port & Adapter Pattern

1. **Domain defines port interface:**
   ```csharp
   public interface IUserPort
   {
       Task<EasyResult<User>> CreateAsync(CreateUserRequest req, CancellationToken ct);
   }
   ```

2. **Infrastructure implements port:**
   ```csharp
   public class MongoUserRepository : IUserPort { ... }
   ```

3. **DI registers binding:**
   ```csharp
   services.AddScoped<IUserPort, MongoUserRepository>();
   ```

## Critical Rules

- ❌ Never import Infrastructure in Domain
- ❌ Never use concrete implementations in Application
- ❌ Never call repositories directly from Controllers
- ❌ Never implement business logic in Controllers
- ✅ Always use Result Pattern (EasyResult<T>)
- ✅ Always propagate CancellationToken
- ✅ Always register ports in ApplicationSetting.cs

---

See CLAUDE.md for complete architecture guide.
