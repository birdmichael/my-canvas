@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Cannot find the .NET Framework C# compiler.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /main:JonsboCanvas.Program /optimize+ /platform:x64 /win32manifest:JonsboCanvas.manifest /win32icon:JonsboCanvas.ico /out:JonsboCanvas-One.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll /resource:MSDISPLAYSDKWRRAPER.dll,JonsboCanvas.Embedded.MSDISPLAYSDKWRRAPER.dll /resource:libusb0.dll,JonsboCanvas.Embedded.libusb0.dll /resource:cpuidsdk.dll.gz,JonsboCanvas.Embedded.cpuidsdk.dll.gz /resource:themes\cyber-cyan.png,JonsboCanvas.Embedded.themes.cyber-cyan.png /resource:themes\molten-amber.png,JonsboCanvas.Embedded.themes.molten-amber.png /resource:themes\aurora-violet.png,JonsboCanvas.Embedded.themes.aurora-violet.png /resource:themes\animated-cyan.gif,JonsboCanvas.Embedded.themes.animated-cyan.gif /resource:assets\codex-pet-idle.png,JonsboCanvas.Embedded.assets.codex-pet-idle.png /resource:assets\claude-companion.png,JonsboCanvas.Embedded.assets.claude-companion.png /resource:drivers\MSUSBDisplay\msusbdisplay.inf,JonsboCanvas.Embedded.driver.msusbdisplay.inf /resource:drivers\MSUSBDisplay\MSUSBDisplay.cat,JonsboCanvas.Embedded.driver.MSUSBDisplay.cat /resource:drivers\MSUSBDisplay\amd64\libusb0.sys,JonsboCanvas.Embedded.driver.libusb0.sys /resource:drivers\MSUSBDisplay\x86\libusb0_x86.dll,JonsboCanvas.Embedded.driver.libusb0_x86.dll Program.cs AppConfig.cs AppLanguage.cs EmbeddedRuntime.cs UsbDriverManager.cs NativeDisplay.cs Metrics.cs NeteaseLauncher.cs NeteaseCdpBridge.cs NeteaseMedia.cs VibeCoding.cs DashboardRenderer.cs MainForm.cs
if errorlevel 1 exit /b 1
echo Built JonsboCanvas-One.exe
