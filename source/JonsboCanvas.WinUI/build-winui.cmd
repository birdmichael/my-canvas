@echo off
setlocal

set "PROJECT_DIR=%~dp0"
set "DOTNET_ROOT=%PROJECT_DIR%..\tools\dotnet"
set "DOTNET_CLI_HOME=%PROJECT_DIR%..\tools\dotnet-home"
set "OUTPUT_DIR=%PROJECT_DIR%..\..\outputs\JonsboCanvas-WinUI"

if not exist "%DOTNET_ROOT%\dotnet.exe" (
  echo Local .NET SDK was not found at "%DOTNET_ROOT%\dotnet.exe".
  exit /b 1
)

pushd "%PROJECT_DIR%"
"%DOTNET_ROOT%\dotnet.exe" restore --configfile "%PROJECT_DIR%NuGet.Config" -p:Platform=x64 -p:RuntimeIdentifier=win-x64
if errorlevel 1 goto :failed

"%DOTNET_ROOT%\dotnet.exe" publish -c Release -r win-x64 --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:PublishTrimmed=false -p:PublishReadyToRun=false -o "%OUTPUT_DIR%" -clp:ErrorsOnly
if errorlevel 1 goto :failed

echo.
echo Published successfully:
echo %OUTPUT_DIR%\JonsboCanvas.WinUI.exe
popd
exit /b 0

:failed
echo.
echo Build failed.
popd
exit /b 1
