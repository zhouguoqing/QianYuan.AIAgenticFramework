<#
.SYNOPSIS
  QianYuan AgenticFramework one-click launcher for Windows (PowerShell).
.DESCRIPTION
  Checks .NET 10 SDK and Node.js, restores+builds the solution, and starts the
  Api host and the React dev server in new windows. Logs go to .runtime\logs.
.PARAMETER Stop
  Kill running Api / Web processes recorded in .runtime\*.pid and exit.
.EXAMPLE
  pwsh -File scripts\start.ps1
  pwsh -File scripts\start.ps1 -Stop
#>

[CmdletBinding()]
param(
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'
$root        = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runtimeDir  = Join-Path $root '.runtime'
$logDir      = Join-Path $runtimeDir 'logs'
$apiPidFile  = Join-Path $runtimeDir 'api.pid'
$webPidFile  = Join-Path $runtimeDir 'web.pid'
$solutionFile = Join-Path $root 'QianYuan.AgenticFramework.sln'
$apiProject  = Join-Path $root 'src\QianYuan.Api\QianYuan.Api.csproj'
$webDir      = Join-Path $root 'src\QianYuan.Web'
$apiUrl      = if ($env:QIANYUAN_API_URL) { $env:QIANYUAN_API_URL } else { 'http://localhost:5050' }
$webUrl      = if ($env:QIANYUAN_WEB_URL) { $env:QIANYUAN_WEB_URL } else { 'http://localhost:5173' }

function Info($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Ok  ($m) { Write-Host "OK  $m" -ForegroundColor Green }
function Warn($m) { Write-Host "!!  $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "XX  $m" -ForegroundColor Red; exit 1 }

function Stop-PidFile($file, $name) {
    if (Test-Path $file) {
        $procId = (Get-Content $file -ErrorAction SilentlyContinue) -as [int]
        if ($procId) {
            $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
            if ($proc) {
                Info "Stopping $name (pid=$procId)"
                try { Stop-Process -Id $procId -Force -ErrorAction Stop } catch { }
            }
        }
        Remove-Item $file -Force -ErrorAction SilentlyContinue
    }
}

New-Item -ItemType Directory -Force -Path $runtimeDir, $logDir | Out-Null

if ($Stop) {
    Stop-PidFile $webPidFile 'Web'
    Stop-PidFile $apiPidFile 'Api'
    Ok 'Stopped.'
    exit 0
}

# --- Toolchain checks ---
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail 'dotnet not found. Install .NET 10 SDK from https://dotnet.microsoft.com/download'
}
$dotnetVer = (dotnet --version).Trim()
$major     = [int]($dotnetVer.Split('.')[0])
if ($major -lt 10) { Warn ".NET SDK $dotnetVer detected — this repo targets net10.0. Continuing." }
Ok ".NET SDK $dotnetVer"

$startWeb = $true
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    Warn 'npm not found — skipping WebUI. Install Node.js >=18 to enable it.'
    $startWeb = $false
} else {
    Ok "Node $(node --version)  npm $(npm --version)"
}

# --- Clean up stale processes ---
Stop-PidFile $apiPidFile 'Api (stale)'
Stop-PidFile $webPidFile 'Web (stale)'

# --- Build ---
Info 'dotnet restore'
$restoreLog = Join-Path $logDir 'restore.log'
& dotnet restore $solutionFile --nologo *>$restoreLog
if ($LASTEXITCODE -ne 0) { Get-Content $restoreLog -Tail 60; Fail "restore failed (see $restoreLog)" }

Info 'dotnet build (Release)'
$buildLog = Join-Path $logDir 'build.log'
& dotnet build $solutionFile -c Release --nologo --no-restore *>$buildLog
if ($LASTEXITCODE -ne 0) { Get-Content $buildLog -Tail 80; Fail "build failed (see $buildLog)" }
Ok 'build succeeded'

# --- npm install if needed ---
if ($startWeb -and -not (Test-Path (Join-Path $webDir 'node_modules'))) {
    Info "npm install ($webDir)"
    $npmLog = Join-Path $logDir 'npm-install.log'
    Push-Location $webDir
    try { & npm install --silent *>$npmLog }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { Get-Content $npmLog -Tail 60; Fail 'npm install failed' }
}

# --- Start Api ---
Info "Starting Api -> $apiUrl  (logs: $logDir\api.log)"
$apiLog = Join-Path $logDir 'api.log'
$env:ASPNETCORE_URLS = $apiUrl
$apiProc = Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run','--project',$apiProject,'-c','Release','--no-build') `
    -WorkingDirectory $root `
    -RedirectStandardOutput $apiLog `
    -RedirectStandardError  (Join-Path $logDir 'api.err.log') `
    -WindowStyle Hidden -PassThru
$apiProc.Id | Out-File -Encoding ascii $apiPidFile

# --- Start Web ---
if ($startWeb) {
    Info "Starting Web -> $webUrl  (logs: $logDir\web.log)"
    $webLog = Join-Path $logDir 'web.log'
    $webProc = Start-Process -FilePath 'cmd.exe' `
        -ArgumentList @('/c','npm','run','dev','--','--host','0.0.0.0') `
        -WorkingDirectory $webDir `
        -RedirectStandardOutput $webLog `
        -RedirectStandardError  (Join-Path $logDir 'web.err.log') `
        -WindowStyle Hidden -PassThru
    $webProc.Id | Out-File -Encoding ascii $webPidFile
}

Start-Sleep -Seconds 2
Ok 'QianYuan started.'
""
"  Api    : $apiUrl    (swagger: $apiUrl/swagger)"
if ($startWeb) { "  WebUI  : $webUrl" }
"  Logs   : $logDir"
"  Stop   : pwsh -File $PSCommandPath -Stop"
