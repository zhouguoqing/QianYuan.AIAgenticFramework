#!/usr/bin/env bash
# QianYuan AgenticFramework one-click launcher (macOS / Linux).
# Checks toolchain, restores, builds, starts the Api host and the React dev server.
# Re-run safe: keeps PID files under .runtime/ ; pass --stop to kill them.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

RUNTIME_DIR="$ROOT/.runtime"
LOG_DIR="$RUNTIME_DIR/logs"
API_PID_FILE="$RUNTIME_DIR/api.pid"
WEB_PID_FILE="$RUNTIME_DIR/web.pid"
API_PROJECT="src/QianYuan.Api/QianYuan.Api.csproj"
WEB_DIR="src/QianYuan.Web"
API_URL="${QIANYUAN_API_URL:-http://localhost:5050}"
WEB_URL="${QIANYUAN_WEB_URL:-http://localhost:5173}"

mkdir -p "$RUNTIME_DIR" "$LOG_DIR"

color() { printf "\033[%sm%s\033[0m\n" "$1" "$2"; }
info()  { color "1;34" "==> $*"; }
ok()    { color "1;32" "OK  $*"; }
warn()  { color "1;33" "!!  $*"; }
fail()  { color "1;31" "XX  $*"; exit 1; }

stop_pid_file() {
    local f="$1" name="$2"
    if [[ -f "$f" ]]; then
        local pid; pid=$(cat "$f" 2>/dev/null || echo "")
        if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
            info "Stopping $name (pid=$pid)"
            kill "$pid" 2>/dev/null || true
            sleep 1
            kill -9 "$pid" 2>/dev/null || true
        fi
        rm -f "$f"
    fi
}

extract_port() {
    local url="$1"
    local value="${url##*:}"
    value="${value%%/*}"
    echo "$value"
}

kill_listeners_on_port() {
    local port="$1" name="$2"
    local pids
    pids=$(lsof -ti tcp:"$port" -sTCP:LISTEN 2>/dev/null || true)
    [[ -z "$pids" ]] && return 0
    warn "$name port $port is busy; stopping stale listener(s): $pids"
    for pid in $pids; do
        kill "$pid" 2>/dev/null || true
    done
    sleep 1
    for pid in $pids; do
        kill -9 "$pid" 2>/dev/null || true
    done
}

wait_http_ready() {
    local url="$1" timeout_sec="${2:-30}"
    local max_steps=$((timeout_sec * 2))
    local i
    for ((i = 1; i <= max_steps; i++)); do
        if curl -fsS "$url" >/dev/null 2>&1; then
            return 0
        fi
        sleep 0.5
    done
    return 1
}

cleanup_orphan_web_processes() {
    local web_root="$1"
    local pids
    pids=$(pgrep -f "$web_root/node_modules/.*vite" 2>/dev/null || true)
    [[ -z "$pids" ]] && return 0
    warn "Stopping orphan Web dev process(es): $pids"
    for pid in $pids; do
        kill "$pid" 2>/dev/null || true
    done
    sleep 1
    for pid in $pids; do
        kill -9 "$pid" 2>/dev/null || true
    done
}

if [[ "${1:-}" == "--stop" ]]; then
    stop_pid_file "$WEB_PID_FILE" "Web"
    stop_pid_file "$API_PID_FILE" "Api"
    ok "Stopped."
    exit 0
fi

# --- Toolchain checks ---
command -v dotnet >/dev/null 2>&1 || fail "dotnet not found. Install .NET 10 SDK: https://dotnet.microsoft.com/download"
DOTNET_MAJOR=$(dotnet --version | cut -d. -f1)
if [[ "${DOTNET_MAJOR:-0}" -lt 10 ]]; then
    warn "Detected .NET SDK $(dotnet --version) — this repo targets net10.0. Continuing, but build may fail."
fi
ok ".NET SDK $(dotnet --version)"

START_WEB=1
if ! command -v npm >/dev/null 2>&1; then
    warn "npm not found — skipping WebUI. Install Node.js >=18 to enable it."
    START_WEB=0
else
    ok "Node $(node --version)  npm $(npm --version)"
fi

# --- Stale-process cleanup ---
stop_pid_file "$API_PID_FILE" "Api (stale)"
stop_pid_file "$WEB_PID_FILE" "Web (stale)"
cleanup_orphan_web_processes "$ROOT/$WEB_DIR"

API_PORT=$(extract_port "$API_URL")
WEB_PORT=$(extract_port "$WEB_URL")
kill_listeners_on_port "$API_PORT" "Api"
kill_listeners_on_port "$WEB_PORT" "Web"

# --- Build ---
info "dotnet restore"
dotnet restore --nologo > "$LOG_DIR/restore.log" 2>&1 || { tail -n 60 "$LOG_DIR/restore.log"; fail "restore failed (see $LOG_DIR/restore.log)"; }
info "dotnet build (Release)"
dotnet build -c Release --nologo --no-restore > "$LOG_DIR/build.log" 2>&1 || { tail -n 80 "$LOG_DIR/build.log"; fail "build failed (see $LOG_DIR/build.log)"; }
ok "build succeeded"

# --- npm install (only if package-lock changed or node_modules missing) ---
if [[ $START_WEB -eq 1 ]]; then
    if [[ ! -d "$WEB_DIR/node_modules" ]]; then
        info "npm install ($WEB_DIR)"
        (cd "$WEB_DIR" && npm install --silent) > "$LOG_DIR/npm-install.log" 2>&1 \
            || { tail -n 60 "$LOG_DIR/npm-install.log"; fail "npm install failed"; }
    fi
fi

# --- Start Api ---
info "Starting Api → $API_URL  (logs: $LOG_DIR/api.log)"
ASPNETCORE_URLS="$API_URL" \
    nohup dotnet run --project "$API_PROJECT" -c Release --no-build \
    > "$LOG_DIR/api.log" 2>&1 &
echo $! > "$API_PID_FILE"

if wait_http_ready "$API_URL/api/sessions?limit=1" 30; then
    ok "Api is ready"
else
    warn "Api readiness check timed out; Web may show temporary 500 until Api warms up"
fi

# --- Start Web ---
if [[ $START_WEB -eq 1 ]]; then
    info "Starting Web → $WEB_URL  (logs: $LOG_DIR/web.log)"
    (cd "$WEB_DIR" && nohup npm run dev -- --host 0.0.0.0 --port "$WEB_PORT" --strictPort > "$LOG_DIR/web.log" 2>&1 & echo $! > "$WEB_PID_FILE")
fi

sleep 2
ok "QianYuan started."
echo
echo "  Api    : $API_URL    (swagger: $API_URL/swagger)"
[[ $START_WEB -eq 1 ]] && echo "  WebUI  : $WEB_URL"
echo "  Logs   : $LOG_DIR"
echo "  Stop   : $0 --stop"
