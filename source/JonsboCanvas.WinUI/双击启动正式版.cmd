@echo off
setlocal
set "JONSBO_CANVAS_DATA_DIR=%~dp0data"
start "" "%~dp0MyCanvas.exe" "--data-dir=%~dp0data"
