#!/usr/bin/env bash
# ============================================================================
# SharpCoreDB WebViewer — Cross-platform launcher (Linux / macOS)
# Builds (if needed) and starts the WebViewer, then opens the browser.
# Usage:
#   ./launch-viewer.sh                  # build + run + open browser
#   ./launch-viewer.sh --no-build       # run existing build
#   ./launch-viewer.sh --port 5443      # custom port
#   ./launch-viewer.sh --open /data/mydb  # connect to a database on startup
# ============================================================================
set -euo pipefail

NO_BUILD=false
PORT=5443
OPEN_PATH=""
CONFIGURATION="Release"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --no-build)          NO_BUILD=true; shift ;;
        --port)              PORT="$2"; shift 2 ;;
        --open)              OPEN_PATH="$2"; shift 2 ;;
        --configuration|-c)  CONFIGURATION="$2"; shift 2 ;;
        --help|-h)
            echo "Usage: $0 [--no-build] [--port 5443] [--open PATH] [--configuration Release]"
            exit 0
            ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/SharpCoreDB.WebViewer.csproj"
URL="https://localhost:${PORT}"

echo "=== SharpCoreDB WebViewer ==="

# 1. Build
if [ "$NO_BUILD" = false ]; then
    echo "[1/3] Building WebViewer ($CONFIGURATION)..."
    dotnet build "$PROJECT" -c "$CONFIGURATION" --nologo -v q
fi

# 2. Launch with settings
echo "[2/3] Starting WebViewer on $URL ..."
export WebViewer__HttpsPort="$PORT"
if [ -n "$OPEN_PATH" ]; then
    export WebViewer__InitialDatabasePath="$OPEN_PATH"
fi

# Use nohup + built-in ASP.NET Core web host in background
dotnet run --project "$PROJECT" --no-build -c "$CONFIGURATION" &
APP_PID=$!

# 3. Open browser once port responds
echo "[3/3] Opening browser..."
trap 'kill $APP_PID 2>/dev/null || true' EXIT INT TERM

sleep 2
READY=false
for _ in $(seq 1 40); do
    if command -v curl >/dev/null 2>&1; then
        if curl -k -s -o /dev/null --connect-timeout 2 "$URL"; then
            READY=true; break
        fi
    else
        # Fallback: check with bash /dev/tcp
        if timeout 2 bash -c "echo > /dev/tcp/localhost/$PORT" 2>/dev/null; then
            READY=true; break
        fi
    fi
    sleep 0.5
done

if [ "$READY" = true ]; then
    echo "WebViewer running at $URL"
    # Open default browser
    if command -v xdg-open >/dev/null 2>&1; then
        xdg-open "$URL" >/dev/null 2>&1 || true
    elif command -v open >/dev/null 2>&1; then
        open "$URL" >/dev/null 2>&1 || true
    elif command -v sensible-browser >/dev/null 2>&1; then
        sensible-browser "$URL" >/dev/null 2>&1 || true
    else
        echo "Open $URL in your browser."
    fi
else
    echo "WebViewer did not respond in time. Check console output above." >&2
fi

# Keep server running in foreground
wait "$APP_PID"