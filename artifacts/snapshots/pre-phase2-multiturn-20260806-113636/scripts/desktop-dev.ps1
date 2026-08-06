<#
.SYNOPSIS
  Quick WorkPartner Desktop debug launcher for Windows PowerShell.
.DESCRIPTION
  Starts the Web dev server when needed, then runs Electron in the foreground.
  The Electron main process starts the local QianYuan.Api host.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root       = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$webDir     = Join-Path $root 'src\QianYuan.Web'
$desktopDir = Join-Path $root 'src\QianYuan.Desktop'
$logDir     = Join-Path $root '.runtime\logs'
$webUrl     = if ($env:WORKPARTNER_RENDERER_URL) { $env:WORKPARTNER_RENDERER_URL } else { 'http://127.0.0.1:5173' }
$webProc    = $null

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Info($message) { Write-Host "==> $message" -ForegroundColor Cyan }
function Fail($message) { Write-Host "XX  $message" -ForegroundColor Red; exit 1 }

function Test-Url($url) {
    try {
        Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2 | Out-Null
        return $true
    } catch {
        return $false
    }
}

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    Fail 'npm not found. Install Node.js >=18.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail 'dotnet not found. Install .NET 10 SDK.'
}

if (-not (Test-Path (Join-Path $webDir 'node_modules'))) {
    Info "npm install ($webDir)"
    Push-Location $webDir
    try { & npm install }
    finally { Pop-Location }
}

if (-not (Test-Path (Join-Path $desktopDir 'node_modules'))) {
    Info "npm install ($desktopDir)"
    Push-Location $desktopDir
    try { & npm install }
    finally { Pop-Location }
}

try {
    if (Test-Url $webUrl) {
        Info "Using existing Web dev server: $webUrl"
    } else {
        Info "Starting Web dev server: $webUrl"
        $webLog = Join-Path $logDir 'desktop-web.log'
        $webErrLog = Join-Path $logDir 'desktop-web.err.log'
        $webProc = Start-Process -FilePath 'cmd.exe' `
            -ArgumentList @('/c','npm','run','dev','--','--host','127.0.0.1') `
            -WorkingDirectory $webDir `
            -RedirectStandardOutput $webLog `
            -RedirectStandardError $webErrLog `
            -WindowStyle Hidden -PassThru

        for ($i = 0; $i -lt 40; $i++) {
            if (Test-Url $webUrl) { break }
            Start-Sleep -Milliseconds 500
        }

        if (-not (Test-Url $webUrl)) {
            Fail "Web dev server did not start. See $webLog"
        }
    }

    Info 'Starting WorkPartner Desktop'
    Info "Renderer: $webUrl"
    $env:WORKPARTNER_RENDERER_URL = $webUrl
    Push-Location $desktopDir
    try { & npm run dev }
    finally { Pop-Location }
}
finally {
    if ($webProc -and -not $webProc.HasExited) {
        Info "Stopping Web dev server (pid=$($webProc.Id))"
        Stop-Process -Id $webProc.Id -Force -ErrorAction SilentlyContinue
    }
}