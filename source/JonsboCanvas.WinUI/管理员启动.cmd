@echo off
setlocal
set "MYCANVAS_EXE=%~dp0MyCanvas.exe"
set "MYCANVAS_DIR=%~dp0"
set "MYCANVAS_DATA=%~dp0data"
powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Process -FilePath $env:MYCANVAS_EXE -WorkingDirectory $env:MYCANVAS_DIR -ArgumentList ('--data-dir=' + [char]34 + $env:MYCANVAS_DATA + [char]34) -Verb RunAs"
