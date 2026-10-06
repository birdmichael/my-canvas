param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceDirectory)
$output = [IO.Path]::GetFullPath($OutputPath)
$installerDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$payloadPath = Join-Path $installerDirectory 'payload.zip'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath (Join-Path $source 'JonsboCanvas.WinUI.exe'))) {
    throw "Invalid publish directory: $source"
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

if (Test-Path -LiteralPath $payloadPath) {
    Remove-Item -LiteralPath $payloadPath -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$files = @(Get-ChildItem -LiteralPath $source -File -Recurse)
$stream = [IO.File]::Open($payloadPath, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new(
        $stream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($source.Length).TrimStart('\').Replace('\', '/')
            $entryName = 'JonsboCanvas-WinUI/' + $relative
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $file.FullName,
                $entryName,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $stream.Dispose()
}

$outputDirectory = Split-Path -Parent $output
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Force
}

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    ('/out:' + $output),
    ('/win32icon:' + (Join-Path $installerDirectory '..\JonsboCanvas.WinUI\Assets\AppIcon.ico')),
    ('/win32manifest:' + (Join-Path $installerDirectory 'app.manifest')),
    ('/resource:' + $payloadPath + ',JonsboCanvas.Payload'),
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.IO.Compression.dll',
    '/reference:System.IO.Compression.FileSystem.dll',
    '/reference:System.Windows.Forms.dll',
    (Join-Path $installerDirectory 'Program.cs')
)

try {
    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Installer compilation failed with exit code $LASTEXITCODE"
    }
}
finally {
    if (Test-Path -LiteralPath $payloadPath) {
        Remove-Item -LiteralPath $payloadPath -Force
    }
}

$item = Get-Item -LiteralPath $output
[pscustomobject]@{
    Output = $item.FullName
    Bytes = $item.Length
    SourceFiles = $files.Count
    Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
}
