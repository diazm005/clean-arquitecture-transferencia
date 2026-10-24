<#
.SYNOPSIS
Inicia el batchero HTTP, procesa una cola SQL opcional o ejecuta comprobaciones sin conexión.
.DESCRIPTION
Serve expone HTTP sin SQL. Once y Watch leen ConnectionStrings__Transferencias para
BD_Equipo2. No crea tablas, no mueve dinero y no guarda credenciales en archivos.
#>
[CmdletBinding()]
param(
    [ValidateSet('Serve', 'Once', 'Watch', 'SelfTest')]
    [string]$Modo = 'Serve',
    [ValidateRange(1, 300)]
    [int]$IntervaloSegundos = 1,
    [ValidateRange(1, 100000)]
    [int]$MaximoPorPasada = 1000
)

$ErrorActionPreference = 'Stop'
$batchProject = Join-Path $PSScriptRoot 'FraudBatch.csproj'
if ($Modo -eq 'Serve') {
    & dotnet run --project $batchProject --launch-profile http -- --serve
} elseif ($Modo -eq 'SelfTest') {
    & dotnet run --project $batchProject -- --self-test
} else {
    if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Transferencias)) {
        throw 'Define ConnectionStrings__Transferencias en esta sesión antes de ejecutar el batch.'
    }
    $batchMode = if ($Modo -eq 'Watch') { '--watch' } else { '--once' }
    & dotnet run --project $batchProject -- $batchMode --interval-seconds $IntervaloSegundos --max-per-pass $MaximoPorPasada
}
exit $LASTEXITCODE
