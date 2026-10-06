# Jonsbo Canvas single-file installer

`Build-Installer.ps1` packages a published x64 WinUI build into one self-extracting
installer executable. The installer deploys the application to
`%LocalAppData%\Programs\JonsboCanvas`, creates Desktop and Start Menu shortcuts,
and launches the installed application.

Build example:

```powershell
.\Build-Installer.ps1 `
  -SourceDirectory ..\..\outputs\JonsboCanvas-WinUI-v1.2.0 `
  -OutputPath ..\..\outputs\JonsboCanvas-Setup-v1.2.0.exe
```

The installer requests administrator privileges because the application manages
USB display access and its optional scheduled startup task. It is currently not
Authenticode-signed, so Windows SmartScreen may show an unknown-publisher warning.
