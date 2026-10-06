$ErrorActionPreference = 'Stop'
$root = 'C:\code'
$src = Join-Path $root 'MyCanvas'
$out = 'R:\out'
$dest = Join-Path $root 'MyCanvas-app'

if (-not (Get-PSDrive -Name R -ErrorAction SilentlyContinue)) { subst R: $src | Out-Null }

# bundle GHidApi.dll so lighting works standalone
Copy-Item (Join-Path $root 'MyCanvas-lighting\gcc\GHidApi.dll') (Join-Path $out 'GHidApi.dll') -Force

# stop the running app, deploy new binaries (data/ config folder is preserved), restart
Get-Process MyCanvas -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Copy-Item (Join-Path $out '*') $dest -Recurse -Force
Write-Output 'deployed'
Write-Output ('dest GHidApi.dll = ' + (Test-Path (Join-Path $dest 'GHidApi.dll')))
$exe = Join-Path $dest 'MyCanvas.exe'
$data = Join-Path $dest 'data'
Start-Process -FilePath $exe -WorkingDirectory $dest -ArgumentList ('--data-dir="' + $data + '"')
Write-Output 'started'
