#!/bin/bash
# start.sh - Start shmoxy
# Usage: ./scripts/start.sh [--port <api-port>] [--proxy-port <proxy-port>] [--no-docker]
#
# Prefers Docker if available and image exists. Use --no-docker for bare-metal.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$REPO_ROOT/dist"
API_DIR="$DIST_DIR/shmoxy.api"
API_DLL="$API_DIR/shmoxy.api.dll"

# Defaults
API_PORT=5000
PROXY_PORT=8080
USE_DOCKER=auto

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
        --no-docker)
            USE_DOCKER=false
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--port <api-port>] [--proxy-port <proxy-port>] [--no-docker]"
            exit 1
            ;;
    esac
done

# Determine whether to use Docker
if [ "$USE_DOCKER" = "auto" ]; then
    if command -v docker &>/dev/null && docker image inspect shmoxy:latest &>/dev/null; then
        USE_DOCKER=true
    else
        USE_DOCKER=false
    fi
fi

if [ "$USE_DOCKER" = true ]; then
    # Resolve the global cert location (matches ProxyConfig.DefaultCertStoragePath)
    # .NET SpecialFolder.ApplicationData: macOS = ~/Library/Application Support, Linux = ~/.config
    if [ "$(uname)" = "Darwin" ]; then
        HOST_CERT_DIR="$HOME/Library/Application Support/shmoxy"
    else
        HOST_CERT_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/shmoxy"
    fi

    CERT_MOUNT_ARGS=()
    if [ -f "$HOST_CERT_DIR/shmoxy-root-ca.pfx" ]; then
        echo "Found existing certs in $HOST_CERT_DIR, mounting into container..."
        CERT_MOUNT_ARGS=(-v "$HOST_CERT_DIR:/root/.config/shmoxy")
    fi

    # Read persisted proxy port from config if it exists, otherwise use PROXY_PORT
    INTERNAL_PROXY_PORT="$PROXY_PORT"
    CONFIG_FILE="$HOST_CERT_DIR/proxy-config.json"
    if [ -f "$CONFIG_FILE" ]; then
        PERSISTED_PORT=$(grep -o '"Port":[[:space:]]*[0-9]*' "$CONFIG_FILE" | grep -o '[0-9]*')
        if [ -n "$PERSISTED_PORT" ]; then
            INTERNAL_PROXY_PORT="$PERSISTED_PORT"
            echo "Persisted proxy config uses port $PERSISTED_PORT, mapping $PROXY_PORT -> $PERSISTED_PORT"
        fi
    fi

    echo "Starting shmoxy via Docker on port $API_PORT (proxy on port $PROXY_PORT)..."
    exec docker run --rm \
        -p "$API_PORT:5000" \
        -p "$PROXY_PORT:$INTERNAL_PROXY_PORT" \
        -v shmoxy-data:/root/.local/share/shmoxy-api \
        "${CERT_MOUNT_ARGS[@]}" \
        -e "ASPNETCORE_URLS=http://+:5000" \
        -e "ApiConfig__ProxyPort=$INTERNAL_PROXY_PORT" \
        shmoxy:latest
else
    if [ ! -f "$API_DLL" ]; then
        echo "Error: $API_DLL not found. Run ./scripts/dist.sh --no-docker first."
        exit 1
    fi

    export ASPNETCORE_URLS="http://localhost:$API_PORT"
    export ApiConfig__ProxyPort="$PROXY_PORT"

    echo "Starting shmoxy API on port $API_PORT (proxy on port $PROXY_PORT)..."
    exec dotnet "$API_DLL"
fi
