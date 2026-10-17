# {{INIT_FILE}}

This file provides guidance to {{AI_NAME}} when working with code in this repository.

## Build & Run Commands

```bash
# Run the API (development)
dotnet run --project SystemAPI/SystemAPI.csproj

# Build — single project
dotnet build SystemAPI/SystemAPI.csproj

# Build — entire solution (all 10 projects)
dotnet build

# Publish for production
dotnet publish SystemAPI/SystemAPI.csproj -c Release

# Run Tests
dotnet test Application.Tests/Application.Tests.csproj

# Docker build & run
docker build -t img-dash-manager .
docker run -p 5701:5001 -e ASPNETCORE_URLS=http://+:5001 img-dash-manager
```

> **Important:** `TreatWarningsAsErrors=true` is active globally. The build must always produce **0 errors, 0 warnings** across all projects before committing.

## Scripts (Scaffolding)

Scaffolding scripts live in `/scripts` and are copied into `{{DIR}}/scripts` for convenience.

```bash
# Generate a new use case
./scripts/scaffold-usecase.ps1 -UsecaseName "MyFeature"

# Generate a controller
./scripts/scaffold-controller.ps1 -UsecaseName "MyFeature" -Route "v1/api/MyFeature"

# Generate tests
./scripts/scaffold-test.ps1 -UsecaseName "MyFeature"
```

## Architecture

This is an ASP.NET Core 10.0 microservice template following **Hexagonal (Ports & Adapters) + Clean Architecture**.

Detailed capabilities and implementation guides are organized by category in the [{{DIR}}/skills/]({{DIR}}/skills/) directory. Automated agent roles are defined in [{{DIR}}/agents/]({{DIR}}/agents/).

### Skills Index

| Skill | Category | Trigger |
|---|---|---|
| [**Result Pattern**]({{DIR}}/skills/result-pattern/SKILL.md) | Architecture | `EasyResult`, `FluentValidation`, HTTP responses |
| [**Scaffolding**]({{DIR}}/skills/scaffolding/SKILL.md) | Development | New feature, endpoint, use case |
| [**Git Commit Helper**]({{DIR}}/skills/git-commit-helper/SKILL.md) | Development | Commit, Conventional Commits |
| [**Unit Testing**]({{DIR}}/skills/unit-testing/SKILL.md) | Testing | xUnit, Moq, test coverage |
| [**Resilience Pattern**]({{DIR}}/skills/resilience-pattern/SKILL.md) | Infrastructure | `HttpClient`, Polly, retry, timeout |
| [**Caching Redis**]({{DIR}}/skills/caching-redis/SKILL.md) | Infrastructure | Redis, `ICachePort`, TTL |
| [**Storage S3**]({{DIR}}/skills/storage-s3/SKILL.md) | Infrastructure | S3, AWS, upload, download |
| [**Database MongoDB**]({{DIR}}/skills/database-mongodb/SKILL.md) | Infrastructure | MongoDB, `IMongoCollection`, repository |
| [**Database SQL Server**]({{DIR}}/skills/database-sqlserver/SKILL.md) | Infrastructure | SQL Server, Dapper, `IDbConnection` |
| [**Messaging Kafka**]({{DIR}}/skills/messaging-kafka/SKILL.md) | Infrastructure | Kafka, producer, consumer, topic |
| [**Event Listener**]({{DIR}}/skills/event-listener/SKILL.md) | Infrastructure | `MemoryQueue`, `Channel`, `BackgroundService` |
| [**Serilog & OTEL**]({{DIR}}/skills/serilog-otel/SKILL.md) | Observability | Logging, tracing, Serilog, OpenTelemetry |

### Agents Index

| Agent | Purpose | Invoke |
|---|---|---|
| `code-reviewer` | Code quality, hexagonal boundaries, Result Pattern | `@code-reviewer` |
| `refactor-cleaner` | Dead code, stubs, Port/Adapter gaps, DI orphans | `@refactor-cleaner` |
| `security-auditor` | JWT, secrets in config, PII in logs, SQL injection | `@security-auditor` |
| `dependency-auditor` | NuGet version drift, CVEs, wrong-layer deps | `@dependency-auditor` |
| `changelog-generator` | CHANGELOG.md from git history (Keep a Changelog) | `@changelog-generator` |

### Layers

| Project | Role |
|---|---|
| `SystemAPI` | Presentation — controllers, auth middleware, startup |
| `Application` | Use cases, adapters (DTOs), validators, result pattern |
| `Domain` | Pure entities, port interfaces (no external deps) |
| `EventListener` | Background hosted services for in-memory event queues |
| `InternalHttpClientInfrastructure` | Resilient `HttpClient` with Polly — internal microservice calls |
| `*Infrastructure` | Adapters for external systems (Mongo, SQL, Kafka, AWS, HTTP) |

### Request Flow

```
Controller → IPort interface → Use Case (Application) → Domain → Infrastructure adapter
```

### Key Patterns

**Ports & Adapters:** Domain defines interfaces (`IExamplePort`, `IExampleTitleQuery`). Infrastructure projects implement them. `ApplicationSetting.cs` wires DI.

**Result Pattern:** Use cases return `EasyResult<T>` — a discriminated union of success/failure. Controllers use `EasyResponseHelper` (basic) or `EasyBianResponseHelper` (BIAN standard) to map results to HTTP responses. See the [result-pattern skill]({{DIR}}/skills/result-pattern/SKILL.md).

**Distributed Tracing:** Every request carries headers `x-device-identifier`, `x-message-identifier`, `x-channel-identifier`. These are captured in `TraceIdentifierAdapter` and flow through the full call chain.

**In-Memory Event Queuing:** `MicroserviceCallMemoryQueue` and `MicroserviceErrorMemoryQueue` use `System.Threading.Channels` (bounded, non-blocking) to decouple tracing writes from request processing. `EventListener` project consumes these as `BackgroundService` hosted services.

**HTTP Resilience:** `InternalHttpClientInfrastructure` wraps `HttpClient` via `HttpClientBuilder` (fluent API) with Polly retry policies, connection pooling, and per-request tracing via `MicroserviceCallMemoryQueue`. See the [resilience-pattern skill]({{DIR}}/skills/resilience-pattern/SKILL.md).

### Authentication & Authorization

Two independent auth mechanisms coexist:

1. **JWT (AWS Cognito):** Configured via `ConfigureJwtAuthentication()`. Validates `iss`, `aud`, scopes from JWT claims (`ReadScope`, `UpdateScope`, `WriteScope` in appsettings). Decorate controllers with `[Authorize(Policy = "ReadScope")]`.
2. **Arify Header Auth:** Custom scheme validating scope via `x-scope` request header. Policy-to-scope mappings defined in `ArifyScopeOptions`. Decorate actions with `[ArifyAuthorize("WriteExample")]`.

### Health Check Endpoints

| Endpoint | Purpose |
|---|---|
| `GET /health` | Basic health status |
| `GET /health/detailed` | Detailed component health |
| `GET /health/ready` | Readiness probe |
| `GET /health/live` | Liveness probe |
| `GET /ping` | Simple connectivity check |

## Static Code Analyzers

Seven analyzers run at build time with **`TreatWarningsAsErrors=true`** — every violation is a build error:

| Analyzer | Rules |
|---|---|
| **Microsoft.CodeAnalysis.NetAnalyzers** `10.0.100` | CA* — official .NET correctness rules |
| **StyleCop.Analyzers** `1.1.118` | SA* — code style (excluded from test projects) |
| **SonarAnalyzer.CSharp** `10.5.x` | S* — bugs and code smells |
| **SecurityCodeScan.VS2019** `5.6.x` | SCS* — SQL injection, XSS (excluded from test projects) |
| **Microsoft.VisualStudio.Threading.Analyzers** | VSTHRD* — async/await correctness |
| **Meziantou.Analyzer** | MA* — performance and design rules |
| **ErrorProne.NET.CoreAnalyzers** `0.6.1` | EPC* — common C# pitfalls |

### Analyzer Conventions

- Suppressions are centralized in `.editorconfig` — never use `#pragma warning disable` or `[SuppressMessage]` inline unless unavoidable.
- Each suppression has a comment explaining the justification (design decision, project convention, third-party API limitation).
- Test projects (`Application.Tests`) have their own `.editorconfig` that relaxes CA1707 (underscore test method names are xUnit convention) and SA1124 (regions).
- XML documentation (SA1600) is **not required** — suppressed project-wide.

## Environment Variables (Docker / Production)

| Variable | Purpose |
|---|---|
| `JWT_ISSUER` | Cognito JWKS endpoint |
| `JWT_AUDIENCE` | Token audience |
| `MONGO_DB_SERVER`, `MONGO_DB_NAME`, `MONGO_DB_USER`, `MONGO_DB_PASSWD` | MongoDB |
| `CORE_DB_SERVER`, `CORE_DB_NAME`, `CORE_DB_USER`, `CORE_DB_PASSWD` | SQL Server |
| `ASPNETCORE_ENVIRONMENT` | `Development` / `Production` |
| `ASPNETCORE_URLS` | Binding URL inside container |

In development, `appsettings.Development.json` overrides are used instead of environment variables.

## Adding a New Feature (Typical Flow)

1. **Scaffold:** Use the PowerShell scripts to generate boilerplate.
	- `./scripts/scaffold-usecase.ps1 -UsecaseName "MyFeature"`
	- `./scripts/scaffold-controller.ps1 -UsecaseName "MyFeature"`
	- `./scripts/scaffold-test.ps1 -UsecaseName "MyFeature"`
2. **Domain:** Define entity/value objects in `Domain/Entities/` and port interface in `Domain/Interfaces/` if needed.
3. **Application:** Implement business logic in the generated `*Case.cs`, returning `EasyResult<T>`.
4. **Infrastructure:** Implement the adapter in the appropriate `*Infrastructure` project if the feature requires external access (DB, Redis, S3, HTTP).
5. **Registration:** Register the new Port → Adapter and Port → UseCase bindings in `ApplicationSetting.cs`.
6. **Tests:** Add test cases in the generated `*CaseTests.cs` following the `Method_Should_When` naming convention.

> **Before writing code**, always consult the [skills/]({{DIR}}/skills/) directory for the specific implementation pattern. Each skill contains canonical examples, anti-patterns, and a PR checklist.
