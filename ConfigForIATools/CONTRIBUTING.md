# Contributing to System API 3.0

## Overview

This guide helps maintainers and contributors understand the development workflow, architecture, and how to make changes to the System API 3.0 project.

**Key principle:** This is an ASP.NET Core 10.0 microservice following **Hexagonal Architecture + Clean Architecture** with strict conventions enforced by 7 static code analyzers.

---

## Before You Start

1. Read **CLAUDE.md** — Complete architecture and development guide
2. Read **AGENTS.md** — Available code review agents
3. Ensure build passes: `dotnet build` → **0 errors, 0 warnings**
4. Review the [skills/](./shared-ia/skills/) directory for implementation patterns

---

## Development Workflow

### 1. Build & Test Requirements

Every commit must pass:

```bash
# Build entire solution
dotnet build

# Run tests
dotnet test Application.Tests/Application.Tests.csproj

# Both must produce: ✅ 0 errors, 0 warnings
```

**Why:** `TreatWarningsAsErrors=true` is active globally. No warnings are tolerated.

### 2. Adding a New Feature

Follow the standard flow:

```powershell
# 1. Generate boilerplate
./shared-ia/scripts/scaffold-usecase.ps1 -UsecaseName "MyFeature"
./shared-ia/scripts/scaffold-controller.ps1 -UsecaseName "MyFeature" -Route "v1/api/MyFeature"
./shared-ia/scripts/scaffold-test.ps1 -UsecaseName "MyFeature"

# 2. Implement in layers: Domain → Application → Infrastructure
# 3. Register ports in ApplicationSetting.cs
# 4. Build and test
dotnet build
dotnet test

# 5. Code review (using agents)
@code-reviewer
@security-auditor

# 6. Commit
git commit -m "feat(scope): description"
```

See **CLAUDE.md** → "Adding a New Feature (Typical Flow)" for details.

### 3. Code Review Checklist

Before committing, use the available agents:

| Agent | Command | Purpose |
|-------|---------|---------|
| **code-reviewer** | `@code-reviewer` | Architecture, Result Pattern, hexagonal boundaries |
| **security-auditor** | `@security-auditor` | Secrets, SQL injection, PII in logs |
| **refactor-cleaner** | `@refactor-cleaner` | Dead code, unused ports, DI orphans |
| **dependency-auditor** | `@dependency-auditor` | NuGet versions, CVEs, version drift |

---

## Static Code Analyzers

7 analyzers run automatically with **`TreatWarningsAsErrors=true`**:

| Analyzer | Rule Prefix | What It Checks |
|----------|-------------|----------------|
| Microsoft.CodeAnalysis.NetAnalyzers | `CA*` | Correctness, API usage |
| StyleCop.Analyzers | `SA*` | Code style, naming conventions |
| SonarAnalyzer.CSharp | `S*` | Code smells, bugs |
| SecurityCodeScan.VS2019 | `SCS*` | SQL injection, XSS |
| VSTHRD (Threading) | `VSTHRD*` | Async/await correctness |
| Meziantou.Analyzer | `MA*` | Performance, design |
| ErrorProne.NET | `EPC*` | Common C# pitfalls |

**Suppressions:** Centralized in `.editorconfig` with justification comments. Never use `#pragma` inline.

---

## Architecture & Patterns

### Hexagonal Layers

```
Presentation (SystemAPI/Controllers)
    ↓
Application (Use Cases, Adapters, Validators)
    ↓
Domain (Entities, Port Interfaces)
    ↓
Infrastructure (Concrete Adapters)
```

### Key Rules

1. **Result Pattern:** All use cases return `EasyResult<T>` (never throw)
2. **Ports & Adapters:** Domain defines interfaces, Infrastructure implements
3. **DI Registration:** `ApplicationSetting.cs` wires Port → Adapter → UseCase
4. **No Layer Violations:** Application cannot reference Infrastructure directly

See **CLAUDE.md** → "Architecture" for complete details.

---

## Template System (For Maintainers)

### How It Works

The **single source of truth** for AI guidance is `shared-ia/templates/TEMPLATE.md`. Changes there automatically propagate to:

- `CLAUDE.md` (Claude Code environment)
- `AGENTS.md` (Codex, Cursor, Kiro environments)
- `.cursor/context.md` (Cursor-specific guidance)
- `.kiro/context.md` (Kiro-specific guidance)

### Making Changes to Documentation

#### Scenario: Update a section (e.g., "### Key Patterns")

1. **Edit** `shared-ia/templates/TEMPLATE.md`
2. **Use placeholders** for environment-specific paths:
   ```markdown
   See [Skills]({{DIR}}/skills/) for patterns
   ```
   Expands to: `.claude/skills/`, `.cursor/skills/`, `.kiro/skills/`, etc.

3. **Run the sync script:**
   ```powershell
   # Interactive mode (select environments)
   ./shared-ia/symlink.ps1

   # Or specify:
   ./shared-ia/symlink.ps1 -Claude -Cursor -Kiro -Codex
   ```

#### Scenario: Add a new section to all AI contexts

1. **Edit** `shared-ia/templates/TEMPLATE.md`
2. **Add section with placeholders:**
   ```markdown
   ## My New Section

   Learn more in [my-guide]({{DIR}}/skills/my-guide/SKILL.md).
   ```

3. **Regenerate all environments:**
   ```powershell
   ./shared-ia/symlink.ps1    # Interactive
   ```

### Placeholders Reference

| Placeholder | Expands To (Example) | Purpose |
|---|---|---|
| `{{INIT_FILE}}` | `CLAUDE.md`, `context.md`, `AGENTS.md` | Output filename |
| `{{AI_NAME}}` | `Claude`, `Cursor`, `Kiro`, `Codex` | IDE/tool personalization |
| `{{DIR}}` | `.claude`, `.cursor`, `.kiro` | Relative directory for links |

### Generation Matrix

| Command | Generates | Notes |
|---------|-----------|-------|
| `-Claude` | `CLAUDE.md` | Project root, Claude-specific |
| `-Cursor` | `.cursor/context.md` + `AGENTS.md` | Shared AGENTS.md |
| `-Kiro` | `.kiro/context.md` + `AGENTS.md` | Shared AGENTS.md |
| `-Codex` | `AGENTS.md` | Shared documentation |

**Key:** AGENTS.md is shared by all non-Claude environments. Claude gets its own CLAUDE.md.

### File Structure

```
shared-ia/
├── templates/
│   └── TEMPLATE.md              ← Single source of truth
├── cursor/
│   ├── .cursorrules             (static: AI rules)
│   ├── cursor.json              (static: config)
│   ├── README.md                (static: setup guide)
│   ├── rules/                   (static: architecture, code-style, security, testing)
│   └── commands/                (static: keybindings)
├── kiro/
│   ├── .kirorules               (static: AI rules)
│   ├── kiro.json                (static: config)
│   ├── README.md                (static: setup guide)
│   ├── rules/                   (static: architecture, code-style, security, testing)
│   └── commands/                (static: keybindings)
├── agents/                       (shared: code-reviewer, security-auditor, etc.)
├── skills/                       (shared: Result Pattern, Scaffolding, Testing, etc.)
└── scripts/                      (shared: scaffold-usecase.ps1, etc.)
```

---

## Static Files (Do NOT Generate)

These files are **manually maintained** in `shared-ia/cursor/` and `shared-ia/kiro/`:

- `.cursorrules` — Cursor AI rule set
- `.kirorules` — Kiro AI rule set
- `rules/architecture.md`, `code-style.md`, `security.md`, `testing.md` — Detailed guides
- `commands/README.md` — Keybindings and custom commands

**Why separate from template:** These are environment-specific configurations that don't need to be replicated. Only `context.md` is generated from the template.

---

## Commit Conventions

Use **Conventional Commits**:

```
feat(scope): add new feature
fix(scope): resolve issue
docs(scope): update documentation
refactor(scope): restructure code without behavior change
test(scope): add/update tests
chore(scope): maintenance tasks
```

Example:
```
feat(credit-usage): implement CreditUsage use case with MongoDB adapter
```

---

## Security Checklist

Before committing:

- [ ] **No hardcoded secrets** — Use environment variables
- [ ] **Parameterized SQL** — All Dapper queries use `@Parameter` syntax
- [ ] **No PII in logs** — Never log passwords, emails, tokens
- [ ] **CancellationToken** — Propagated through entire async chain
- [ ] **Input validation** — At system boundaries only (user input, external APIs)
- [ ] Run `@security-auditor` agent

See **CLAUDE.md** → "Security" for details.

---

## Testing Requirements

- **Framework:** xUnit + Moq
- **Naming:** `[Method]_Should[Behavior]_When[Condition]`
- **Structure:** Arrange/Act/Assert
- **Coverage:** All happy paths + edge cases

Example:
```csharp
[Fact]
public async Task RegisterAsync_ShouldReturnSuccess_WhenValidEmailProvided()
{
    // ARRANGE
    var request = new UserRegisterRequestAdapter { Email = "test@example.com" };
    _mockUserPort.Setup(x => x.CreateAsync(It.IsAny<User>(), default))
        .ReturnsAsync(EasyResult<User>.Success(new User { Id = "123" }));

    // ACT
    var result = await _useCase.RegisterAsync(request, default);

    // ASSERT
    Assert.True(result.IsSuccess);
    _mockUserPort.Verify(x => x.CreateAsync(It.IsAny<User>(), default), Times.Once);
}
```

See [skills/unit-testing/SKILL.md](./shared-ia/skills/unit-testing/SKILL.md) for complete guide.

---

## Environment Setup

### Prerequisites

- **.NET 10.0** SDK
- **PowerShell 5.1+** (for scripts)
- **Docker** (optional, for containerized builds)

### Local Development

```bash
# Install dependencies
dotnet restore

# Build
dotnet build

# Run API (development)
dotnet run --project SystemAPI/SystemAPI.csproj

# Run tests
dotnet test Application.Tests/Application.Tests.csproj
```

### Environment Variables (Development)

Set in `appsettings.Development.json`:

```json
{
  "JwtIssuer": "your-issuer",
  "JwtAudience": "your-audience",
  "MongoDbServer": "localhost:27017",
  "CoreDbServer": "localhost:1433"
}
```

See **CLAUDE.md** → "Environment Variables" for production setup.

---

## Useful Resources

- **Full Architecture:** [CLAUDE.md](./CLAUDE.md)
- **Agents & Tools:** [AGENTS.md](./AGENTS.md)
- **Skills & Patterns:** [shared-ia/skills/](./shared-ia/skills/)
- **Build Scripts:** [shared-ia/scripts/](./shared-ia/scripts/)

---

## Questions?

- **Architecture questions:** See `CLAUDE.md` or use `@code-reviewer`
- **Security concerns:** Use `@security-auditor`
- **Code style:** See `.editorconfig` or `@refactor-cleaner`
- **Dependencies:** Use `@dependency-auditor`

---

**Last updated:** February 2026
