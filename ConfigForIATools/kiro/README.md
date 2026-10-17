# Kiro Configuration — System API 3.0

## Overview

This directory contains Kiro AI configuration and guidelines for System API 3.0 development.

Kiro is a custom AI environment that works with your project to provide intelligent code assistance, architecture validation, and security checking.

## What's Inside

### Configuration Files

| File | Purpose |
|------|---------|
| `.kirorules` | AI guidelines enforcing architecture, code style, security, and testing patterns |
| `kiro.json` | Kiro configuration with keybindings and project metadata |
| `context.md` | Quick reference guide for developers |

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

1. Copy this `.kiro/` directory to your repository root
2. Open the project in Kiro
3. Kiro will automatically load `.kirorules` and configuration

## Usage

### Via Keybinding

```
Ctrl+Shift+B  → Build (dotnet build)
Ctrl+Shift+T  → Test (dotnet test)
Ctrl+Alt+R    → Code Review (@code-reviewer)
Ctrl+Alt+S    → Security Check (@security-auditor)
```

### Via Kiro Commands

Reference Kiro agents directly:

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

### .kirorules

Core AI guidelines. Enforces:

- **Architecture:** Domain layer purity, port/adapter boundaries, no layer violations
- **Code Style:** PascalCase types, camelCase variables, Allman braces, 120-char max
- **Result Pattern:** All use cases return `EasyResult<T>`, never throw
- **Security:** No hardcoded secrets, parameterized SQL, no PII in logs, CancellationToken propagation
- **Testing:** xUnit + Moq, "[Method]_Should[Behavior]_When[Condition]" naming, Arrange/Act/Assert structure

### kiro.json

Configuration metadata:

```json
{
  "projectName": "System API 3.0",
  "projectDescription": "ASP.NET Core 10.0 Microservice with Hexagonal Architecture",
  "language": "csharp",
  "framework": "dotnet10",
  "architecture": "hexagonal-clean-architecture",
  "rulesFile": ".kirorules",
  "aiModel": "gpt-4o",
  "keyBindings": {
    "build": "ctrl+shift+b",
    "test": "ctrl+shift+t",
    "code-review": "ctrl+alt+r",
    "security-check": "ctrl+alt+s"
  }
}
```

## Tips

- **Conflict with system keybindings?** Edit `kiro.json` and update the keybinding value
- **Want different commands?** Modify the `buildCommand`, `testCommand`, `runCommand` in `kiro.json`
- **Agent not responding?** Restart Kiro and check the rules load correctly
- **Guidelines not enforced?** Verify `.kirorules` is in the repository root or `.kiro/` directory

## Documentation

- **Agent Descriptions:** See `AGENTS.md` in repository root (auto-generated when you run `./shared-ia/symlink.ps1 -Kiro`)
- **Build Commands & Architecture:** See `context.md` (auto-generated from `shared-ia/templates/TEMPLATE.md`)
- **Rule Details:** See individual files in `rules/` directory
- **Full Project Guide:** See `CLAUDE.md` if also running Claude environment

---

For full project documentation, see CLAUDE.md
