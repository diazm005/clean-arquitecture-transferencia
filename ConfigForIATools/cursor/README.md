# Cursor Configuration — System API 3.0

## Overview

This directory contains Cursor IDE configuration and guidelines for System API 3.0 development.

Cursor is an AI-powered code editor that integrates with your project to provide intelligent code assistance, architecture validation, and security checking.

## What's Inside

### Configuration Files

| File | Purpose |
|------|---------|
| `.cursorrules` | AI guidelines enforcing architecture, code style, security, and testing patterns |
| `cursor.json` | Cursor configuration with keybindings and project metadata |

### Rule Guides

| File | Coverage |
|------|----------|
| `rules/architecture.md` | Hexagonal pattern, port/adapter boundaries, layer violations |
| `rules/code-style.md` | Naming conventions, formatting, C# patterns |
| `rules/security.md` | Secrets, SQL injection, PII in logs, authentication |
| `rules/testing.md` | Test naming, structure (Arrange/Act/Assert), coverage |

### Commands

| File | Purpose |
|------|---------|
| `commands/README.md` | Keybindings and command documentation |

## Setup

1. Copy this `.cursor/` directory to your repository root
2. Open the project in Cursor
3. Cursor will automatically load `.cursorrules` and configuration

## Usage

### Via Keybinding

```
Ctrl+Shift+B  → Build (dotnet build)
Ctrl+Shift+T  → Test (dotnet test)
Ctrl+Alt+R    → Code Review (@code-reviewer)
Ctrl+Alt+S    → Security Check (@security-auditor)
```

### Via Cursor Commands

Reference Cursor agents directly:

```
@code-reviewer      → Validate architecture & design
@security-auditor   → Check security vulnerabilities
@refactor-cleaner   → Find dead code and unused ports
@dependency-auditor → Check NuGet versions and CVEs
```

## Workflow

### Pre-Commit Checklist

```
1. Implement feature
2. Ctrl+Shift+B     → Ensure build passes (0 errors, 0 warnings)
3. Ctrl+Shift+T     → Ensure tests pass
4. Ctrl+Alt+R       → Get code review approval
5. Ctrl+Alt+S       → Get security check passed
6. git commit
```

## Configuration Files

### .cursorrules

Core AI guidelines. Enforces:

- **Architecture:** Domain layer purity, port/adapter boundaries, no layer violations
- **Code Style:** PascalCase types, camelCase variables, Allman braces, 120-char max
- **Result Pattern:** All use cases return `EasyResult<T>`, never throw
- **Security:** No hardcoded secrets, parameterized SQL, no PII in logs, CancellationToken propagation
- **Testing:** xUnit + Moq, "[Method]_Should[Behavior]_When[Condition]" naming, Arrange/Act/Assert structure

### cursor.json

Configuration metadata:

```json
{
  "projectName": "System API 3.0",
  "projectDescription": "ASP.NET Core 10.0 Microservice with Hexagonal Architecture",
  "language": "csharp",
  "framework": "dotnet10",
  "architecture": "hexagonal-clean-architecture",
  "rulesFile": ".cursorrules"
}
```

## Tips

- **Conflict with system keybindings?** Edit `.cursor/commands/README.md` and update the keybinding value
- **Want different commands?** Modify `.cursor/commands/README.md`
- **Agent not responding?** Restart Cursor and check the rules load correctly
- **Guidelines not enforced?** Verify `.cursorrules` is in the repository root or `.cursor/` directory

## Documentation

- **Agent Descriptions:** See `AGENTS.md` in repository root (auto-generated when you run `./shared-ia/symlink.ps1 -Cursor`)
- **Build Commands & Architecture:** See `context.md` in `.cursor/` (auto-generated from `shared-ia/templates/TEMPLATE.md`)
- **Rule Details:** See individual files in `rules/` directory
- **Full Project Guide:** See `CLAUDE.md` if also running Claude environment

---

For full project documentation, see CLAUDE.md or run `./shared-ia/symlink.ps1 -Cursor` to sync configuration.
