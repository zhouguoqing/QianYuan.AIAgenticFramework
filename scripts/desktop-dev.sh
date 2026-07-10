#!/usr/bin/env bash
# Quick WorkPartner Desktop debug launcher (macOS / Linux).
# Starts the Web dev server when needed, then runs Electron in the foreground.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB_DIR="$ROOT/src/QianYuan.Web"
DESKTOP_DIR="$ROOT/src/QianYuan.Desktop"
LOG_DIR="$ROOT/.runtime/logs"
WEB_URL="${WORKPARTNER_RENDERER_URL:-http://127.0.0.1:5173}"
WEB_PID=""

mkdir -p "$LOG_DIR"

info() { printf "==> %s\n" "$*"; }
fail() { printf "XX  %s\n" "$*" >&2; exit 1; }

cleanup() {
    if [[ -n "$WEB_PID" ]] && kill -0 "$WEB_PID" 2>/dev/null; then
        info "Stopping Web dev server (pid=$WEB_PID)"
        kill "$WEB_PID" 2>/dev/null || true
    fi
}
trap cleanup EXIT INT TERM

command -v npm >/dev/null 2>&1 || fail "npm not found. Install Node.js >=18."
command -v dotnet >/dev/null 2>&1 || fail "dotnet not found. Install .NET 10 SDK."

if [[ ! -d "$WEB_DIR/node_modules" ]]; then
    info "npm install ($WEB_DIR)"
    (cd "$WEB_DIR" && npm install)
fi

if [[ ! -d "$DESKTOP_DIR/node_modules" ]]; then
    info "npm install ($DESKTOP_DIR)"
    (cd "$DESKTOP_DIR" && npm install)
fi

if curl -fsS "$WEB_URL" >/dev/null 2>&1; then
    info "Using existing Web dev server: $WEB_URL"
else
    info "Starting Web dev server: $WEB_URL"
    (cd "$WEB_DIR" && npm run dev -- --host 127.0.0.1 > "$LOG_DIR/desktop-web.log" 2>&1) &
    WEB_PID=$!

    for _ in {1..40}; do
        if curl -fsS "$WEB_URL" >/dev/null 2>&1; then
            break
        fi
        sleep 0.5
    done

    curl -fsS "$WEB_URL" >/dev/null 2>&1 || fail "Web dev server did not start. See $LOG_DIR/desktop-web.log"
fi

info "Starting WorkPartner Desktop"
info "Renderer: $WEB_URL"
info "Web logs: $LOG_DIR/desktop-web.log"
(cd "$DESKTOP_DIR" && WORKPARTNER_RENDERER_URL="$WEB_URL" npm run dev)