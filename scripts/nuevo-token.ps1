param(
    [ValidatePattern('^[0-9]{8}$')]
    [string]$Dni = '12345678',
    [switch]$SoloLectura
)

$ErrorActionPreference = 'Stop'
$proyecto = Join-Path $PSScriptRoot '../SystemAPI/SystemAPI.csproj'

# Emisor local exclusivo para el taller. No es un login ni valida contraseñas de clientes.
# dotnet guarda la clave de firma en User Secrets del usuario de Windows, fuera del repositorio.
$argumentos = @('user-jwts', 'create', '--project', $proyecto,
    '--name', "cliente-$Dni", '--issuer', 'clean-transferencias-dev',
    '--audience', 'clean-transferencias-api', '--claim', "national_id=$Dni",
    '--scope', 'transferencias.read', '--valid-for', '15m', '--output', 'token')
if (-not $SoloLectura) { $argumentos += @('--scope', 'transferencias.write') }
& dotnet @argumentos
if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar el token local.' }
