@echo off
REM QianYuan AgenticFramework one-click launcher (Windows cmd wrapper).
REM Forwards everything to start.ps1. Prefer PowerShell 7 (pwsh) if available.
REM Convenience:
REM   scripts\start.cmd --install       -> install npm deps only and exit
REM   scripts\start.cmd --install-deps  -> force install deps then start

setlocal
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%start.ps1"
set "PS_ARGS=%*"

if /I "%~1"=="--install" set "PS_ARGS=-InstallDepsOnly"
if /I "%~1"=="--install-deps" set "PS_ARGS=-InstallDeps"

where pwsh >nul 2>nul
if %ERRORLEVEL%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %PS_ARGS%
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %PS_ARGS%
)
exit /b %ERRORLEVEL%
