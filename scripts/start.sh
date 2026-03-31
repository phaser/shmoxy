#!/bin/bash
# start.sh - Start shmoxy from the dist directory
# Usage: ./scripts/start.sh [--port <api-port>] [--proxy-port <proxy-port>]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$REPO_ROOT/dist"
API_DIR="$DIST_DIR/shmoxy.api"
API_DLL="$API_DIR/shmoxy.api.dll"

# Defaults
API_PORT=5000
PROXY_PORT=8080

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --port)
            API_PORT="$2"
            shift 2
            ;;
        --proxy-port)
            PROXY_PORT="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--port <api-port>] [--proxy-port <proxy-port>]"
            exit 1
            ;;
    esac
done

if [ ! -f "$API_DLL" ]; then
    echo "Error: $API_DLL not found. Run ./scripts/dist.sh first."
    exit 1
fi

export ASPNETCORE_URLS="http://localhost:$API_PORT"
export ApiConfig__ProxyPort="$PROXY_PORT"

echo "Starting shmoxy API on port $API_PORT (proxy on port $PROXY_PORT)..."
exec dotnet "$API_DLL"
