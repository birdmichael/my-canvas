$ErrorActionPreference = 'Stop'
$root = 'C:\code'
$src = Join-Path $root 'MyCanvas'
$dotnet = Join-Path $root 'MyCanvas-tools\dotnet\dotnet.exe'

if (-not (Get-PSDrive -Name R -ErrorAction SilentlyContinue)) { subst R: $src | Out-Null }
Set-Location 'R:\'

foreach ($p in @('R:\work\JonsboCanvas.WinUI\obj','R:\work\JonsboCanvas.WinUI\bin')) { if (Test-Path $p) { Remove-Item -Recurse -Force $p } }

$env:DOTNET_CLI_HOME = Join-Path $root 'MyCanvas-tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root 'MyCanvas-tools\nuget'

& $dotnet publish 'R:\work\JonsboCanvas.WinUI\JonsboCanvas.WinUI.csproj' -c Release -r win-x64 -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:PublishTrimmed=false -p:PublishReadyToRun=false -o 'R:\out' --nologo -clp:ErrorsOnly
Write-Output ('EXIT=' + $LASTEXITCODE)
