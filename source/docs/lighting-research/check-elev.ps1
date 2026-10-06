$d = 'C:\Program Files\GIGABYTE\Control Center'
try {
  $t = Join-Path $d ('wtest_' + [guid]::NewGuid().ToString('N') + '.tmp')
  [IO.File]::WriteAllText($t, 'x')
  Remove-Item $t -Force
  Write-Output 'WRITABLE'
} catch {
  Write-Output ('NO-WRITE: ' + $_.Exception.Message)
}
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$p = New-Object Security.Principal.WindowsPrincipal($id)
Write-Output ('Elevated=' + $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
