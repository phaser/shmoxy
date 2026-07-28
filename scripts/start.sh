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

# Resolve the global cert/config location (matches ProxyConfig.DefaultCertStoragePath)
# .NET SpecialFolder.ApplicationData: macOS = ~/Library/Application Support, Linux = ~/.config
if [ "$(uname)" = "Darwin" ]; then
    HOST_CERT_DIR="$HOME/Library/Application Support/shmoxy"
else
    HOST_CERT_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/shmoxy"
fi
CONFIG_FILE="$HOST_CERT_DIR/proxy-config.json"

# The API prefers the persisted proxy config over ApiConfig__ProxyPort, so the persisted
# value is the port the proxy will really listen on.
PERSISTED_PORT=""
if [ -f "$CONFIG_FILE" ]; then
    PERSISTED_PORT=$(grep -o '"Port":[[:space:]]*[0-9]*' "$CONFIG_FILE" | grep -o '[0-9]*')
fi

if [ "$USE_DOCKER" = true ]; then
    CERT_MOUNT_ARGS=()
    if [ -f "$HOST_CERT_DIR/shmoxy-root-ca.pfx" ]; then
        echo "Found existing certs in $HOST_CERT_DIR, mounting into container..."
        CERT_MOUNT_ARGS=(-v "$HOST_CERT_DIR:/root/.config/shmoxy")
    fi

    # Docker can paper over a mismatch by mapping the requested host port onto whatever
    # port the proxy actually binds inside the container.
    INTERNAL_PROXY_PORT="${PERSISTED_PORT:-$PROXY_PORT}"
    if [ -n "$PERSISTED_PORT" ] && [ "$PERSISTED_PORT" != "$PROXY_PORT" ]; then
        echo "Persisted proxy config uses port $PERSISTED_PORT, mapping $PROXY_PORT -> $PERSISTED_PORT"
    fi

    echo "Starting shmoxy via Docker on port $API_PORT (proxy on port $PROXY_PORT)..."
    exec docker run --rm \
        -p "$API_PORT:5000" \
        -p "$PROXY_PORT:$INTERNAL_PROXY_PORT" \
        -v shmoxy-data:/data \
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

    # Bare metal has no port mapping to fall back on: if a persisted config exists, the
    # proxy binds that port and --proxy-port is silently ignored. Say so instead of
    # printing a port nothing is listening on.
    EFFECTIVE_PROXY_PORT="${PERSISTED_PORT:-$PROXY_PORT}"
    if [ -n "$PERSISTED_PORT" ] && [ "$PERSISTED_PORT" != "$PROXY_PORT" ]; then
        echo "WARNING: persisted proxy config in $CONFIG_FILE sets port $PERSISTED_PORT," >&2
        echo "         which overrides the requested --proxy-port $PROXY_PORT." >&2
        echo "         The proxy will listen on $PERSISTED_PORT. Change the port in the UI's" >&2
        echo "         Proxy tab, or edit that file, to use $PROXY_PORT instead." >&2
    fi

    echo "Starting shmoxy API on port $API_PORT (proxy on port $EFFECTIVE_PROXY_PORT)..."
    exec dotnet "$API_DLL"
fi
