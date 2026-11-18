@echo off
setlocal
pushd "%~dp0" || exit /b 1
where pythonw.exe >nul 2>&1
if not errorlevel 1 (
    start "" pythonw.exe app.py
    exit /b
)
where pyw.exe >nul 2>&1
if not errorlevel 1 (
    start "" pyw.exe -3 app.py
    exit /b
)
echo Install Python 3.12 or newer for Windows, then run this file again.
pause
exit /b 1
