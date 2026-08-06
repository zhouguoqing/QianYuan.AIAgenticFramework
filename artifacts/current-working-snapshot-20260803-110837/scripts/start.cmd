@echo off
REM QianYuan AgenticFramework one-click launcher (Windows cmd wrapper).
REM Forwards everything to start.ps1. Prefer PowerShell 7 (pwsh) if available.

setlocal
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%start.ps1"

where pwsh >nul 2>nul
if %ERRORLEVEL%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
)
exit /b %ERRORLEVEL%
