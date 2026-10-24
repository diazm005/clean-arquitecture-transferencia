param(
    [string]$BatchUrl = 'http://127.0.0.1:5310/'
)

$ErrorActionPreference = 'Stop'
$proyecto = Join-Path $PSScriptRoot '../SystemAPI/SystemAPI.csproj'

# La conexión se toma de la terminal o de la variable del usuario de Windows; nunca de Git.
if (-not $env:ConnectionStrings__Transferencias) {
    $env:ConnectionStrings__Transferencias = [Environment]::GetEnvironmentVariable('ConnectionStrings__Transferencias', 'User')
}
if (-not $env:ConnectionStrings__Transferencias) {
    throw 'Configura ConnectionStrings__Transferencias con la conexión a BD_Equipo2 antes de iniciar la API.'
}

# La clave de firma JWT la crea dotnet user-jwts (scripts/nuevo-token.ps1) en User Secrets.
$secretos = & dotnet user-secrets list --project $proyecto
if ($LASTEXITCODE -ne 0 -or -not ($secretos -match 'Authentication:Schemes:Bearer:SigningKeys')) {
    throw 'No hay clave JWT local. Ejecuta primero ./scripts/nuevo-token.ps1 para crearla.'
}

# Fraude.Batch vive en el repositorio transferencias-api-antifraude (scripts/iniciar-batch.ps1).
try { Invoke-RestMethod -Uri ($BatchUrl.TrimEnd('/') + '/health') -TimeoutSec 2 | Out-Null }
catch { Write-Warning "Fraude.Batch no responde en $BatchUrl. Las transferencias quedarán registradas como error hasta que se inicie." }

$env:Infrastructure__Provider = 'SqlServer'
$env:Antifraude__BaseUrl = $BatchUrl
& dotnet run --project $proyecto --launch-profile http
