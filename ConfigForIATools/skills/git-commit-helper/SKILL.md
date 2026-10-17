---
name: git-commit-helper
description: Genera mensajes de commit en formato Conventional Commits analizando el git diff. Se activa cuando el usuario quiere hacer commit, pide un mensaje de commit, menciona cambios staged o pregunta cómo commitear.
allowed-tools: Bash, Read
argument-hint: "[mensaje-adicional-o-contexto]"
---

# Git Commit Helper — Conventional Commits

Analiza los cambios staged, el historial reciente y genera un mensaje de commit semántico listo para ejecutar.

---

## 1. Flujo de Análisis (siempre ejecutar en orden)

```bash
# Paso 1 — Ver archivos modificados
git status

# Paso 2 — Ver qué cambió exactamente
git diff --staged

# Paso 3 — Ver estilo de commits recientes del proyecto
git log --oneline -8
```

Con esa información, generar el mensaje y ejecutar el commit.

---

## 2. Formato Conventional Commits

```
<type>(<scope>): <subject>

<optional body>

<optional footer>
```

### Tipos

| Tipo | Cuándo usarlo |
|---|---|
| `feat` | Nuevo caso de uso, endpoint, feature |
| `fix` | Corrección de bug o comportamiento incorrecto |
| `refactor` | Reestructuración sin cambio de funcionalidad |
| `test` | Añadir o corregir pruebas unitarias |
| `build` | Cambios en `.csproj`, NuGet packages, Docker |
| `docs` | `CLAUDE.md`, `SKILL.md`, `README.md` |
| `chore` | Limpieza, configuración, scaffolding scripts |
| `perf` | Optimización de rendimiento |
| `ci` | Pipelines CI/CD |
| `style` | Formateo, editorconfig, sin cambio de lógica |

### Scopes del Proyecto

| Scope | Qué cubre |
|---|---|
| `usecase` | Cambios en `Application/Usecases/` |
| `domain` | Cambios en `Domain/` |
| `controller` | Cambios en `SystemAPI/Controllers/` |
| `api` | Cambios globales de API, contratos, middlewares de `Program.cs` |
| `infra` | Cambios en `*Infrastructure/` |
| `tests` | Cambios en `Application.Tests/` |
| `scripts` | Cambios en `scripts/*.ps1` |
| `skills` | Cambios en `.claude/skills/` |
| `agents` | Cambios en `.claude/agents/` |
| `config` | `appsettings.json`, `Program.cs`, `ApplicationSetting.cs` |
| `docker` | `Dockerfile`, `docker-compose` |
| `security` | Autenticación, autorización, JWT, headers de seguridad |

### Reglas del Subject

- Máximo 50 caracteres
- Modo imperativo: `add`, `fix`, `move`, `remove` (no `added`, `fixes`)
- Sin punto al final
- Minúsculas después del `type(scope):`

---

## 3. Patrón de Commit (HEREDOC)

Siempre usar HEREDOC para preservar el formato multilínea:

```bash
git commit -m "$(cat <<'EOF'
feat(usecase): add UserRegister with FluentValidation

- Validate headers (422) and request (400)
- Use Task.FromResult to avoid MA0004 warning
- Add ILogger with PII-safe structured logging

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

> El footer `Co-Authored-By` se incluye siempre cuando Claude genera o ayuda a escribir el commit.

---

## 4. Ejemplos por Tipo de Cambio

### Nuevo caso de uso

```
feat(usecase): add UserRegister with validation and logging

- Add UserRegisterCase, Validator, Port, Adapters
- Validate TraceIdentifierAdapter headers (422)
- Validate request fields with MessageCatalog codes

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```

### Corrección de bug

```
fix(infra): handle null response body in HttpClientBuilder

Response body was not checked before deserialization,
causing NullReferenceException on empty 200 responses.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```

### Refactor arquitectónico

```
refactor(config): move ApplicationSetting to SystemAPI (composition root)

Application.csproj referenced Infrastructure projects directly,
violating Hexagonal Architecture boundaries.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```

### Mejora de tests

```
test(usecase): expand UserRegisterCaseTests to 13 cases

Add Theory tests for email/password/username edge cases.
Use NullLogger<T>.Instance instead of ILogger mock setup.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```

### Cambio de skill/documentación

```
docs(skills): refactor result-pattern skill with real API examples

Fix FluentValidationExecutor pattern, add EasyBianResponseHelper
section, status code table, anti-patterns and PR checklist.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```

### Breaking change

```
feat(api): change EasyResult Failure signature

BREAKING CHANGE: Failure() now requires IReadOnlyCollection<ValidationResultAdapter>
instead of IEnumerable. Update all callers accordingly.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```

---

## 5. Reglas de Seguridad (de CLAUDE.md)

| Regla | Descripción |
|---|---|
| **Nunca amend en publicado** | Solo amend si el commit NO fue pushed |
| **Nunca `--no-verify`** | No saltar hooks pre-commit |
| **Nunca `git add .` ciego** | Stagear archivos específicos para evitar `.env`, secretos |
| **Nunca force-push a `main`** | Siempre advertir al usuario |
| **Crear nuevo commit en fallo** | Si el pre-commit hook falla, corregir y crear commit nuevo (no amend) |

---

## 6. Verificación Post-Commit

```bash
git status          # Debe mostrar "nothing to commit"
git log --oneline -3  # Verificar que el commit aparece con el mensaje correcto
```

---

## 7. Anti-patrones a Evitar

```bash
# ❌ Mensaje vago
git commit -m "fix stuff"
git commit -m "changes"
git commit -m "update"

# ✅ Específico y semántico
git commit -m "fix(controller): return 422 when TraceIdentifier headers are missing"

# ❌ Pasado o gerundio
git commit -m "feat(auth): added JWT validation"
git commit -m "feat(auth): adding JWT validation"

# ✅ Imperativo presente
git commit -m "feat(auth): add JWT validation"

# ❌ git add . sin revisar (puede incluir secretos o binarios)
git add .

# ✅ Stagear por nombre o patrón
git add Application/Usecases/UserRegister/
git add SystemAPI/Controllers/UserRegister/

# ❌ Amend en commit ya pusheado
git commit --amend   # después de git push

# ✅ Crear un nuevo commit de corrección
git commit -m "fix(usecase): correct validation order in UserRegisterCase"
```
