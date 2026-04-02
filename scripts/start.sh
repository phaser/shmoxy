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
    if [ "$(uname)" = "Darwin" ]; then
        HOST_CERT_DIR="$HOME/Library/Application Support/shmoxy"
    else
        HOST_CERT_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/shmoxy"
    fi

    CERT_MOUNT_ARGS=()
    if [ -f "$HOST_CERT_DIR/shmoxy-root-ca.pfx" ]; then
        echo "Found existing certs in $HOST_CERT_DIR, mounting into container..."
        CERT_MOUNT_ARGS=(-v "$HOST_CERT_DIR:/root/.local/share/shmoxy")
    fi

    echo "Starting shmoxy via Docker on port $API_PORT (proxy on port $PROXY_PORT)..."
    exec docker run --rm \
        -p "$API_PORT:5000" \
        -p "$PROXY_PORT:8080" \
        -v shmoxy-data:/root/.local/share/shmoxy-api \
        "${CERT_MOUNT_ARGS[@]}" \
        -e "ASPNETCORE_URLS=http://+:5000" \
        -e "ApiConfig__ProxyPort=8080" \
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
