# Cursor Commands

Custom commands and keybindings for System API 3.0 development.

## Available Commands

### Build & Test

#### build
- **Keybinding:** `Ctrl+Shift+B`
- **Command:** `dotnet build`
- **Purpose:** Compile entire solution
- **Expected:** 0 errors, 0 warnings

#### test
- **Keybinding:** `Ctrl+Shift+T`
- **Command:** `dotnet test Application.Tests/Application.Tests.csproj`
- **Purpose:** Run all unit tests
- **Expected:** All tests pass

### Code Review

#### code-review
- **Keybinding:** `Ctrl+Alt+R`
- **Command:** Launch `@code-reviewer` agent
- **Purpose:** Architecture & design validation
- **Checks:**
  - Layer boundaries (no crossing)
  - Result Pattern usage
  - Port registration
  - Dependency injection

#### security-check
- **Keybinding:** `Ctrl+Alt+S`
- **Command:** Launch `@security-auditor` agent
- **Purpose:** Security vulnerability detection
- **Checks:**
  - Hardcoded secrets
  - SQL injection risks
  - PII in logs
  - CancellationToken propagation

## Usage

### Via Keybinding
1. Press keybinding (e.g., `Ctrl+Shift+B`)
2. Command executes
3. View results in terminal

### Via Command Palette
1. `Ctrl+Shift+P` → Open command palette
2. Type command name (e.g., "build", "test")
3. Select and execute

### Via Agent
1. Type agent reference (e.g., `@code-reviewer`)
2. Agent analyzes context
3. Provides feedback

## Pre-Commit Workflow

```
1. Implement feature
2. Ctrl+Shift+B     → Ensure build passes
3. Ctrl+Shift+T     → Ensure tests pass
4. Ctrl+Alt+R       → Code review approval
5. Ctrl+Alt+S       → Security check passed
6. git commit
```

## Customizing Commands

Edit `cursor.json` in `.cursor/`:

```json
{
  "keyBindings": {
    "build": "ctrl+shift+b",
    "test": "ctrl+shift+t",
    "code-review": "ctrl+alt+r",
    "security-check": "ctrl+alt+s"
  }
}
```

## Tips

- **Conflict with system keybindings?** Change in cursor.json
- **Want different commands?** Modify cursor.json buildCommand, testCommand, runCommand
- **Agent not responding?** Restart Cursor
- **Keybindings not working?** Verify cursor.json is in `.cursor/` root

---

See context.md for quick reference.
