# Builds MyCanvas from this folder into ..\app and restarts it.
#   .\publish.ps1           publish and restart
#   .\publish.ps1 -Test     run the render and WinUI tests first
param([switch]$Test)
$ErrorActionPreference = 'Stop'

$source = $PSScriptRoot
$app = Join-Path (Split-Path $source -Parent) 'app'
$dotnet = Join-Path $source 'tools\dotnet\dotnet.exe'
$env:DOTNET_CLI_HOME = Join-Path $source 'tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $source 'tools\nuget'

if ($Test) {
    & $dotnet run --project (Join-Path $source 'MyCanvas.Tests\MyCanvas.Tests.csproj') -c Release -- (Join-Path $source 'test-output')
    if ($LASTEXITCODE -ne 0) { throw 'render tests failed' }
    & $dotnet run --project (Join-Path $source 'JonsboCanvas.WinUI.Tests\JonsboCanvas.WinUI.Tests.csproj') -c Release -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'WinUI tests failed' }
}

# The running app locks its files; app\data (config, wallpapers, logs) is kept.
Get-Process MyCanvas -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
& $dotnet publish (Join-Path $source 'JonsboCanvas.WinUI\JonsboCanvas.WinUI.csproj') -c Release -r win-x64 --self-contained true `
    -p:Platform=x64 -p:PublishTrimmed=false -p:PublishReadyToRun=false -o $app --nologo -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
& $dotnet build-server shutdown | Out-Null

# The autostart task runs elevated, which the lighting and sensors need.
schtasks /Run /TN '\My Canvas Dual Display AutoStart' | Out-Null
Write-Output "published to $app and restarted"
