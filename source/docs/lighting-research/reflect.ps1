$ErrorActionPreference = 'Continue'
$dst = 'work/lighting/gcc'
New-Item -ItemType Directory -Force -Path $dst | Out-Null
$src = 'C:\Program Files\GIGABYTE\Control Center'
$files = @(
  'GHidApi.dll',
  'SMBCtrl.dll',
  'Lib\COMMDLL\RgbCommon.dll',
  'Lib\COMMDLL\RGBFI.dll',
  'Lib\GBT_rgbMotherboard_UC\LedIoControl.dll',
  'Lib\GBT_rgbMotherboard_UC\MB_RGB_Capability.dll',
  'Lib\GBT_rgbMotherboard_UC\RgbMotherboard.dll',
  'Lib\GBT_rgbMotherboard_UC\GBT_rgbMotherboard_UC.dll'
)
foreach ($f in $files) {
  $s = Join-Path $src $f
  if (Test-Path $s) {
    Copy-Item $s (Join-Path $dst (Split-Path $f -Leaf)) -Force
    Write-Output ("copied " + $f)
  } else {
    Write-Output ("missing " + $f)
  }
}
