#Requires -Version 5.1
<#
.SYNOPSIS
    Copy shared agent skills and agents to Claude and/or Codex directories.
.DESCRIPTION
    Interactive script to copy shared-agent-skills to .claude and .codex folders.
    Automatically updates the 'model' frontmatter property for Codex agents.
    Generates CLAUDE.md/CODEX.md and AGENTS.md from templates.
.PARAMETER Claude
    Link only to Claude directory (model: sonnet)
.PARAMETER Codex
    Link only to Codex directory (model: gpt-5.2-codex)
.PARAMETER Kiro
    Link only to Kiro directory
#>

param(
    [switch]$Claude,
    [switch]$Codex,
    [switch]$Cursor,
    [switch]$Kiro
)

# Colors
$Colors = @{
    'Success' = 'Green'
    'Error'   = 'Red'
    'Warning' = 'Yellow'
    'Info'    = 'Cyan'
}

function Write-ColorOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [Parameter(Mandatory = $false)]
        [ValidateSet('Success', 'Error', 'Warning', 'Info')]
        [string]$Type = 'Info'
    )
    if ($Colors.ContainsKey($Type)) {
        Write-Host $Message -ForegroundColor $Colors[$Type]
    }
    else {
        Write-Host $Message
    }
}

function Show-Header {
    Write-Host ""
    Write-Host "============================" -ForegroundColor Cyan
    Write-Host "  AI Context Manager" -ForegroundColor Cyan
    Write-Host "============================" -ForegroundColor Cyan
    Write-Host ""
}

function Copy-Content {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Target,

        [Parameter(Mandatory = $false)]
        [string]$ReplaceModel
    )

    # Convert to absolute paths
    $sourcePath = (Resolve-Path $Source).Path
    $targetPath = $Target

    # Create parent directory if it doesn't exist
    $targetParent = Split-Path $targetPath
    if (-not (Test-Path $targetParent)) {
        New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
    }

    # If source is a directory, use Recurse
    if (Test-Path $sourcePath -PathType Container) {
        # Remove target if it exists (prevents nested duplicates on re-run)
        if (Test-Path $targetPath) {
            Remove-Item $targetPath -Recurse -Force
        }

        # Copy directory content
        Copy-Item -Path $sourcePath -Destination $targetPath -Recurse -Force | Out-Null
    }
    else {
        # Copy file logic
        $content = Get-Content -Path $sourcePath -Raw
        if ($ReplaceModel) {
            # Regex to find "model: something" in YAML frontmatter
            if ($content -match 'model:\s*([a-zA-Z0-9\-\.]+)') {
                $content = [regex]::Replace($content, 'model:\s*[a-zA-Z0-9\-\.]+', "model: $ReplaceModel")
            }
        }
        Set-Content -Path $targetPath -Value $content -Force
    }
}

function Process-Template {
    param(
        [string]$TemplatePath,
        [string]$OutputPath,
        [hashtable]$Replacements
    )

    if (-not (Test-Path $TemplatePath)) {
        Write-ColorOutput "Template not found: $TemplatePath" Warning
        return
    }

    $content = Get-Content -Path $TemplatePath -Raw
    foreach ($key in $Replacements.Keys) {
        $val = $Replacements[$key]
        $content = $content.Replace("{{$key}}", $val)
    }
    Set-Content -Path $OutputPath -Value $content -Force
    Write-ColorOutput "  - Generated: $OutputPath" Success
}

# Main script
Show-Header

# Determine paths relative to the script location
# If this script is in /shared-ia, then $PSScriptRoot IS the shared base.
# But we also need the repo root which is one level up.
$scriptPath = $PSScriptRoot
$repoRoot = (Resolve-Path "$scriptPath/..").Path

# Since the script is inside shared-ia, sharedBase is just scriptPath
$sharedBase = $scriptPath 
$templatesDir = Join-Path $sharedBase "templates"
$scriptsSource = Join-Path $sharedBase "scripts"

# Verify shared-ia content exists (e.g. skills folder)
if (-not (Test-Path "$sharedBase/skills")) {
    Write-ColorOutput "Error: Skills directory not found in $sharedBase" Error
    exit 1
}

# Define target base directories
$claudeBase = Join-Path $repoRoot ".claude"
$codexBase = Join-Path $repoRoot ".codex"
$cursorBase = Join-Path $repoRoot ".cursor"
$kiroBase = Join-Path $repoRoot ".kiro"

# Determine target(s)
$targetClaude = $false
$targetCodex = $false
$targetCursor = $false
$targetKiro = $false

if ($Claude) {
    $targetClaude = $true
}
elseif ($Codex) {
    $targetCodex = $true
}
elseif ($Cursor) {
    $targetCursor = $true
}
elseif ($Kiro) {
    $targetKiro = $true
}
else {
    Write-ColorOutput "Select target AI environment(s):" Warning
    Write-Host "1. Claude (.claude)"
    Write-Host "2. Codex (.codex)"
    Write-Host "3. Cursor (.cursor)"
    Write-Host "4. Kiro (.kiro)"
    Write-Host ""

    $choice = Read-Host "> "

    switch ($choice) {
        '1' { $targetClaude = $true }
        '2' { $targetCodex = $true }
        '3' { $targetCursor = $true }
        '4' { $targetKiro = $true }
        default {
            Write-ColorOutput "Invalid choice" Error
            exit 1
        }
    }
}

# Get available skills
$skills = @()
if (Test-Path "$sharedBase/skills") {
    $skills = @(Get-ChildItem -Path "$sharedBase/skills" -Directory | Select-Object -ExpandProperty Name | Sort-Object)
}

# Get available agents
$agentsPath = Join-Path $sharedBase "agents"
if (Test-Path $agentsPath) {
    $agents = @(Get-ChildItem -Path $agentsPath | Where-Object { $_.PSIsContainer -eq $false } | Select-Object -ExpandProperty Name | Sort-Object)
}

Write-Host ""
Write-ColorOutput "Copying skills, agents, and scripts..." Info
Write-Host ""

# ---------------------------------------------------------
# CLAUDE PROCESSING (model: sonnet)
# ---------------------------------------------------------
if ($targetClaude) {
    $targetName = "Claude"
    $targetBase = $claudeBase
    $dirName = ".claude"
    $initFileName = "CLAUDE.md"
    
    # Init Directory
    if (-not (Test-Path $targetBase)) { New-Item -ItemType Directory -Path $targetBase -Force | Out-Null }

    # Copy Skills
    $targetSkillsDir = Join-Path $targetBase "skills"
    if (-not (Test-Path $targetSkillsDir)) { New-Item -ItemType Directory -Path $targetSkillsDir -Force | Out-Null }

    Write-ColorOutput "Processing $targetName Skills..." Info
    foreach ($skill in $skills) {
        $sourcePath = Join-Path "$sharedBase/skills" $skill
        $targetPath = Join-Path $targetSkillsDir $skill
        Copy-Content -Source $sourcePath -Target $targetPath
    }

    # Copy Agents
    $targetAgentsDir = Join-Path $targetBase "agents"
    if (-not (Test-Path $targetAgentsDir)) { New-Item -ItemType Directory -Path $targetAgentsDir -Force | Out-Null }
    
    Write-ColorOutput "Processing $targetName Agents (model: sonnet)..." Info
    foreach ($agent in $agents) {
        $sourcePath = Join-Path $agentsPath $agent
        $targetPath = Join-Path $targetAgentsDir $agent
        Copy-Content -Source $sourcePath -Target $targetPath -ReplaceModel "sonnet"
    }

    # Copy Scripts
    if (Test-Path $scriptsSource) {
        $targetScriptsDir = Join-Path $targetBase "scripts"
        Write-ColorOutput "Processing $targetName Scripts..." Info
        Copy-Content -Source $scriptsSource -Target $targetScriptsDir
    }

    # Generate Templates
    Write-ColorOutput "Generating Templates for $targetName..." Info

    # CLAUDE.md
    Process-Template -TemplatePath (Join-Path $templatesDir "TEMPLATE.md") `
        -OutputPath (Join-Path $repoRoot $initFileName) `
        -Replacements @{ 
        "INIT_FILE" = $initFileName; 
        "AI_NAME"   = "Claude Code"; 
        "DIR"       = $dirName 
    }
}

# ---------------------------------------------------------
# CODEX PROCESSING (model: gpt-5.2-codex)
# ---------------------------------------------------------
if ($targetCodex) {
    $targetName = "Codex"
    $targetBase = $codexBase
    $dirName = ".codex"

    # Init Directory
    if (-not (Test-Path $targetBase)) { New-Item -ItemType Directory -Path $targetBase -Force | Out-Null }

    # Copy Skills
    $targetSkillsDir = Join-Path $targetBase "skills"
    if (-not (Test-Path $targetSkillsDir)) { New-Item -ItemType Directory -Path $targetSkillsDir -Force | Out-Null }

    Write-ColorOutput "Processing $targetName Skills..." Info
    foreach ($skill in $skills) {
        $sourcePath = Join-Path "$sharedBase/skills" $skill
        $targetPath = Join-Path $targetSkillsDir $skill
        Copy-Content -Source $sourcePath -Target $targetPath
    }

    # Copy Agents
    $targetAgentsDir = Join-Path $targetBase "agents"
    if (-not (Test-Path $targetAgentsDir)) { New-Item -ItemType Directory -Path $targetAgentsDir -Force | Out-Null }
    
    Write-ColorOutput "Processing $targetName Agents (model: gpt-5.2-codex)..." Info
    foreach ($agent in $agents) {
        $sourcePath = Join-Path $agentsPath $agent
        $targetPath = Join-Path $targetAgentsDir $agent
        Copy-Content -Source $sourcePath -Target $targetPath -ReplaceModel "gpt-5.2-codex"
    }

    # Copy Scripts
    if (Test-Path $scriptsSource) {
        $targetScriptsDir = Join-Path $targetBase "scripts"
        Write-ColorOutput "Processing $targetName Scripts..." Info
        Copy-Content -Source $scriptsSource -Target $targetScriptsDir
    }

    # Generate Templates
    Write-ColorOutput "Generating Templates for $targetName..." Info

    # AGENTS.md
    Process-Template -TemplatePath (Join-Path $templatesDir "TEMPLATE.md") `
        -OutputPath (Join-Path $repoRoot "AGENTS.md") `
        -Replacements @{ 
        "INIT_FILE" = "AGENTS.md"; 
        "AI_NAME"   = "Codex";
        "DIR"       = $dirName
    }
}

# ---------------------------------------------------------
# CURSOR PROCESSING
# ---------------------------------------------------------
if ($targetCursor) {
    $targetName = "Cursor"
    $targetBase = $cursorBase
    $dirName = ".cursor"
    $cursorSource = Join-Path $sharedBase "cursor"

    # Init Directory
    if (-not (Test-Path $targetBase)) { New-Item -ItemType Directory -Path $targetBase -Force | Out-Null }

    # Copy Cursor Configuration Files
    Write-ColorOutput "Processing $targetName Configuration..." Info

    # Copy .cursorrules
    if (Test-Path "$cursorSource/.cursorrules") {
        Copy-Content -Source "$cursorSource/.cursorrules" -Target "$targetBase/.cursorrules"
        Write-ColorOutput "  - Copied .cursorrules" Success
    }

    # Copy cursor.json
    if (Test-Path "$cursorSource/cursor.json") {
        Copy-Content -Source "$cursorSource/cursor.json" -Target "$targetBase/cursor.json"
        Write-ColorOutput "  - Copied cursor.json" Success
    }

    # Generate context.md from template
    Write-ColorOutput "Generating Cursor context.md from template..." Info
    Process-Template -TemplatePath (Join-Path $templatesDir "TEMPLATE.md") `
        -OutputPath (Join-Path $targetBase "context.md") `
        -Replacements @{
        "INIT_FILE" = "context.md";
        "AI_NAME"   = "Cursor";
        "DIR"       = $dirName
    }

    # Copy README.md
    if (Test-Path "$cursorSource/README.md") {
        Copy-Content -Source "$cursorSource/README.md" -Target "$targetBase/README.md"
        Write-ColorOutput "  - Copied README.md" Success
    }

    # Copy Rules Directory
    if (Test-Path "$cursorSource/rules") {
        $targetRulesDir = Join-Path $targetBase "rules"
        Copy-Content -Source "$cursorSource/rules" -Target $targetRulesDir
        Write-ColorOutput "  - Copied rules/ (architecture, code-style, security, testing)" Success
    }

    # Copy Commands Directory
    if (Test-Path "$cursorSource/commands") {
        $targetCommandsDir = Join-Path $targetBase "commands"
        Copy-Content -Source "$cursorSource/commands" -Target $targetCommandsDir
        Write-ColorOutput "  - Copied commands/ (README with keybindings)" Success
    }

    # Copy Skills
    $targetSkillsDir = Join-Path $targetBase "skills"
    if (-not (Test-Path $targetSkillsDir)) { New-Item -ItemType Directory -Path $targetSkillsDir -Force | Out-Null }

    Write-ColorOutput "Processing $targetName Skills..." Info
    foreach ($skill in $skills) {
        $sourcePath = Join-Path "$sharedBase/skills" $skill
        $targetPath = Join-Path $targetSkillsDir $skill
        Copy-Content -Source $sourcePath -Target $targetPath
    }

    # Copy Agents
    $targetAgentsDir = Join-Path $targetBase "agents"
    if (-not (Test-Path $targetAgentsDir)) { New-Item -ItemType Directory -Path $targetAgentsDir -Force | Out-Null }

    Write-ColorOutput "Processing $targetName Agents..." Info
    foreach ($agent in $agents) {
        $sourcePath = Join-Path $agentsPath $agent
        $targetPath = Join-Path $targetAgentsDir $agent
        Copy-Content -Source $sourcePath -Target $targetPath -ReplaceModel "claude-3.5-sonnet"
    }

    # Copy Scripts
    if (Test-Path $scriptsSource) {
        $targetScriptsDir = Join-Path $targetBase "scripts"
        Write-ColorOutput "Processing $targetName Scripts..." Info
        Copy-Content -Source $scriptsSource -Target $targetScriptsDir
    }

    # Generate AGENTS.md (shared resource for all non-Claude AIs)
    Write-ColorOutput "Generating AGENTS.md (shared agents documentation)..." Info
    Process-Template -TemplatePath (Join-Path $templatesDir "TEMPLATE.md") `
        -OutputPath (Join-Path $repoRoot "AGENTS.md") `
        -Replacements @{
        "INIT_FILE" = "AGENTS.md";
        "AI_NAME"   = "All AI Environments";
        "DIR"       = $dirName
    }

    Write-ColorOutput "✅ $targetName configuration complete" Success
}

# ---------------------------------------------------------
# KIRO PROCESSING
# ---------------------------------------------------------
if ($targetKiro) {
    $targetName = "Kiro"
    $targetBase = $kiroBase
    $dirName = ".kiro"
    $kiroSource = Join-Path $sharedBase "kiro"

    # Init Directory
    if (-not (Test-Path $targetBase)) { New-Item -ItemType Directory -Path $targetBase -Force | Out-Null }

    # Copy Kiro Configuration Files
    Write-ColorOutput "Processing $targetName Configuration..." Info

    # Copy .kirorules
    if (Test-Path "$kiroSource/.kirorules") {
        Copy-Content -Source "$kiroSource/.kirorules" -Target "$targetBase/.kirorules"
        Write-ColorOutput "  - Copied .kirorules" Success
    }

    # Copy kiro.json
    if (Test-Path "$kiroSource/kiro.json") {
        Copy-Content -Source "$kiroSource/kiro.json" -Target "$targetBase/kiro.json"
        Write-ColorOutput "  - Copied kiro.json" Success
    }

    # Generate context.md from template
    Write-ColorOutput "Generating Kiro context.md from template..." Info
    Process-Template -TemplatePath (Join-Path $templatesDir "TEMPLATE.md") `
        -OutputPath (Join-Path $targetBase "context.md") `
        -Replacements @{
        "INIT_FILE" = "context.md";
        "AI_NAME"   = "Kiro";
        "DIR"       = $dirName
    }

    # Copy README.md
    if (Test-Path "$kiroSource/README.md") {
        Copy-Content -Source "$kiroSource/README.md" -Target "$targetBase/README.md"
        Write-ColorOutput "  - Copied README.md" Success
    }

    # Copy Rules Directory
    if (Test-Path "$kiroSource/rules") {
        $targetRulesDir = Join-Path $targetBase "rules"
        Copy-Content -Source "$kiroSource/rules" -Target $targetRulesDir
        Write-ColorOutput "  - Copied rules/ (architecture, code-style, security, testing)" Success
    }

    # Copy Commands Directory
    if (Test-Path "$kiroSource/commands") {
        $targetCommandsDir = Join-Path $targetBase "commands"
        Copy-Content -Source "$kiroSource/commands" -Target $targetCommandsDir
        Write-ColorOutput "  - Copied commands/ (README with keybindings)" Success
    }

    # Copy Skills
    $targetSkillsDir = Join-Path $targetBase "skills"
    if (-not (Test-Path $targetSkillsDir)) { New-Item -ItemType Directory -Path $targetSkillsDir -Force | Out-Null }

    Write-ColorOutput "Processing $targetName Skills..." Info
    foreach ($skill in $skills) {
        $sourcePath = Join-Path "$sharedBase/skills" $skill
        $targetPath = Join-Path $targetSkillsDir $skill
        Copy-Content -Source $sourcePath -Target $targetPath
    }

    # Copy Agents
    $targetAgentsDir = Join-Path $targetBase "agents"
    if (-not (Test-Path $targetAgentsDir)) { New-Item -ItemType Directory -Path $targetAgentsDir -Force | Out-Null }

    Write-ColorOutput "Processing $targetName Agents..." Info
    foreach ($agent in $agents) {
        $sourcePath = Join-Path $agentsPath $agent
        $targetPath = Join-Path $targetAgentsDir $agent
        Copy-Content -Source $sourcePath -Target $targetPath -ReplaceModel "gpt-4o"
    }

    # Copy Scripts
    if (Test-Path $scriptsSource) {
        $targetScriptsDir = Join-Path $targetBase "scripts"
        Write-ColorOutput "Processing $targetName Scripts..." Info
        Copy-Content -Source $scriptsSource -Target $targetScriptsDir
    }

    # Generate AGENTS.md (shared resource for all non-Claude AIs)
    Write-ColorOutput "Generating AGENTS.md (shared agents documentation)..." Info
    Process-Template -TemplatePath (Join-Path $templatesDir "TEMPLATE.md") `
        -OutputPath (Join-Path $repoRoot "AGENTS.md") `
        -Replacements @{
        "INIT_FILE" = "AGENTS.md";
        "AI_NAME"   = "All AI Environments";
        "DIR"       = $dirName
    }

    Write-ColorOutput "✅ $targetName configuration complete" Success
}

Write-Host ""
Write-ColorOutput "- Done! All operations completed successfully" Success
Write-Host ""
