#!/bin/bash
# dist.sh - Build distribution artifacts
# Usage: ./scripts/dist.sh [--no-docker] [--smoke-test]
#
# By default builds a Docker image. Use --no-docker for bare-metal dotnet publish.
# Use --smoke-test to build the Docker image and run smoke tests against it.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$REPO_ROOT/dist"
DIST_YAML="$REPO_ROOT/dist.yaml"
SRC_DIR="$REPO_ROOT/src"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Parse arguments
USE_DOCKER=true
RUN_SMOKE_TEST=false
while [[ $# -gt 0 ]]; do
    case $1 in
        --no-docker)
            USE_DOCKER=false
            shift
            ;;
        --smoke-test)
            RUN_SMOKE_TEST=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--no-docker] [--smoke-test]"
            exit 1
            ;;
    esac
done

# Read current version from dist.yaml
if [ ! -f "$DIST_YAML" ]; then
    log_error "dist.yaml not found at $DIST_YAML"
    exit 1
fi

CURRENT_VERSION=$(grep '^version:' "$DIST_YAML" | sed 's/version:[[:space:]]*//')
if [ -z "$CURRENT_VERSION" ]; then
    log_error "Could not read version from dist.yaml"
    exit 1
fi

log_info "Current version: $CURRENT_VERSION"

# Parse and bump patch version
MAJOR=$(echo "$CURRENT_VERSION" | cut -d. -f1)
MINOR=$(echo "$CURRENT_VERSION" | cut -d. -f2)
PATCH=$(echo "$CURRENT_VERSION" | cut -d. -f3)

NEW_PATCH=$((PATCH + 1))
BASE_VERSION="$MAJOR.$MINOR.$NEW_PATCH"

# Append git commit hash suffix
GIT_HASH=$(git -C "$REPO_ROOT" rev-parse --short=8 HEAD 2>/dev/null || echo "unknown")
NEW_VERSION="$BASE_VERSION-gh.$GIT_HASH"

log_info "New version: $NEW_VERSION"

BUILD_TIMESTAMP=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
TAG_NAME="v$BASE_VERSION"

if [ "$USE_DOCKER" = true ]; then
    # Docker build
    log_info "Building Docker image..."
    docker build \
        --build-arg VERSION="$NEW_VERSION" \
        -t "shmoxy:$NEW_VERSION" \
        -t "shmoxy:latest" \
        "$REPO_ROOT"
    log_info "Docker image built: shmoxy:$NEW_VERSION"
else
    # Bare-metal dotnet publish
    rm -rf "$DIST_DIR"
    mkdir -p "$DIST_DIR"

    API_DIR="$DIST_DIR/shmoxy.api"

    # Download CyberChef assets (gitignored, must be fetched before publish)
    CYBERCHEF_DIR="$SRC_DIR/shmoxy.frontend/wwwroot/cyberchef"
    if [ ! -f "$CYBERCHEF_DIR/CyberChef.html" ]; then
        log_info "Downloading CyberChef assets..."
        "$SCRIPT_DIR/download-cyberchef.sh"
    else
        log_info "CyberChef assets already present, skipping download"
    fi

    # Publish shmoxy.api (includes shmoxy.frontend assets via RCL project reference)
    log_info "Publishing shmoxy.api to $API_DIR..."
    dotnet publish "$SRC_DIR/shmoxy.api" \
        -c Release \
        -o "$API_DIR" \
        /p:Version="$NEW_VERSION" \
        --nologo \
        -v quiet
    log_info "shmoxy.api published successfully"

    # Publish the shmoxy proxy into the API directory
    PROXY_TEMP_DIR="$DIST_DIR/.proxy-tmp"
    log_info "Publishing shmoxy proxy..."
    dotnet publish "$SRC_DIR/shmoxy" \
        -c Release \
        -o "$PROXY_TEMP_DIR" \
        /p:Version="$NEW_VERSION" \
        --nologo \
        -v quiet

    # Copy proxy files into the API directory (skip files already present)
    log_info "Copying proxy files into shmoxy.api output..."
    for file in "$PROXY_TEMP_DIR"/*; do
        filename=$(basename "$file")
        if [ ! -e "$API_DIR/$filename" ]; then
            cp -r "$file" "$API_DIR/"
        fi
    done
    rm -rf "$PROXY_TEMP_DIR"
    log_info "shmoxy proxy bundled into shmoxy.api"

    # Generate build manifest
    MANIFEST_FILE="$DIST_DIR/manifest.json"
    cat > "$MANIFEST_FILE" << EOF
{
  "version": "$NEW_VERSION",
  "timestamp": "$BUILD_TIMESTAMP",
  "components": ["shmoxy.api"],
  "git_commit": "$(git -C "$REPO_ROOT" rev-parse HEAD)",
  "git_tag": "$TAG_NAME"
}
EOF
    log_info "Build manifest written to $MANIFEST_FILE"
fi

# Update dist.yaml with new base version (no suffix, used for patch bumping)
cat > "$DIST_YAML" << EOF
version: $BASE_VERSION
EOF

log_info "Updated dist.yaml to version $NEW_VERSION"

# Create git tag
if git -C "$REPO_ROOT" tag "$TAG_NAME" 2>/dev/null; then
    log_info "Created git tag: $TAG_NAME"
else
    log_warn "Git tag $TAG_NAME already exists, skipping"
fi

echo ""
log_info "Distribution $NEW_VERSION complete!"

if [ "$USE_DOCKER" = true ]; then
    log_info "Run with:"
    echo ""
    echo "  docker run -p 5000:5000 -p 8080:8080 -v shmoxy-data:/root/.local/share/shmoxy-api shmoxy:$NEW_VERSION"
    echo ""
    log_info "Then open http://localhost:5000 in your browser."
else
    log_info "Output: $DIST_DIR"
fi

# --- Smoke Tests ---

run_smoke_tests() {
    local compose_file="$REPO_ROOT/docker-compose.yml"
    local passed=0
    local failed=0
    local test_failures=""

    log_info "=== Docker Smoke Tests ==="

    # Ensure any previous run is cleaned up
    docker compose -f "$compose_file" down --remove-orphans > /dev/null 2>&1 || true

    # Start container
    log_info "Starting container via docker compose..."
    docker compose -f "$compose_file" up -d

    # Wait for health endpoint
    log_info "Waiting for application to start..."
    local max_wait=120
    local waited=0
    while ! curl -sf http://localhost:5000/api/health > /dev/null 2>&1; do
        sleep 1
        waited=$((waited + 1))
        if [ $waited -ge $max_wait ]; then
            log_error "Application failed to start within ${max_wait}s"
            docker compose -f "$compose_file" logs
            docker compose -f "$compose_file" down
            exit 1
        fi
    done
    log_info "Application ready after ${waited}s"

    # Test 1: API health check
    log_info "Test 1: API health check (port 5000)..."
    if curl -sf http://localhost:5000/api/health > /dev/null 2>&1; then
        log_info "  PASS"
        passed=$((passed + 1))
    else
        log_error "  FAIL: API health endpoint not responding"
        failed=$((failed + 1))
        test_failures="${test_failures}\n  - API health check failed"
    fi

    # Test 2: Proxy port accepting connections
    log_info "Test 2: Proxy port (8080) accepting connections..."
    if curl -sf -o /dev/null --max-time 5 --proxy http://localhost:8080 http://localhost:5000/api/health 2>/dev/null; then
        log_info "  PASS"
        passed=$((passed + 1))
    else
        log_error "  FAIL: Proxy port 8080 not accepting connections"
        failed=$((failed + 1))
        test_failures="${test_failures}\n  - Proxy port not accepting connections"
    fi

    # Test 3: E2E proxy smoke tests — proxy HTTPS requests to known sites
    local sites=("https://news.ycombinator.com" "https://www.github.com" "https://www.microsoft.com")
    for site in "${sites[@]}"; do
        log_info "Test 3: Proxy request to $site..."
        local http_code
        http_code=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 --proxy http://localhost:8080 "$site" 2>/dev/null || echo "000")
        if [ "$http_code" -ge 200 ] 2>/dev/null && [ "$http_code" -lt 400 ] 2>/dev/null; then
            log_info "  PASS (HTTP $http_code)"
            passed=$((passed + 1))
        else
            log_error "  FAIL: $site returned HTTP $http_code"
            failed=$((failed + 1))
            test_failures="${test_failures}\n  - Proxy to $site failed (HTTP $http_code)"
        fi
    done

    # Test 4: Log inspection
    log_info "Test 4: Checking container logs for errors..."
    local logs
    logs=$(docker compose -f "$compose_file" logs 2>&1)
    local error_lines
    error_lines=$(echo "$logs" | grep -iE "^.*\s(fail|crit|fatal):" | grep -v "warn:" || true)
    if [ -n "$error_lines" ]; then
        local error_count
        error_count=$(echo "$error_lines" | wc -l | tr -d ' ')
        log_warn "  WARN: $error_count error(s) found in logs:"
        echo "$error_lines" | head -20 | while IFS= read -r line; do
            echo -e "    ${RED}$line${NC}"
        done
        # Log errors are warnings, not test failures — they get filed as separate issues
    else
        log_info "  PASS: No errors found in logs"
    fi
    passed=$((passed + 1))

    # Test 5: Graceful shutdown
    log_info "Test 5: Graceful shutdown..."
    local shutdown_output
    shutdown_output=$(docker compose -f "$compose_file" down 2>&1)
    local shutdown_exit=$?
    if [ $shutdown_exit -eq 0 ]; then
        log_info "  PASS: Container shut down gracefully"
        passed=$((passed + 1))
    else
        log_error "  FAIL: Shutdown returned exit code $shutdown_exit"
        echo "$shutdown_output"
        failed=$((failed + 1))
        test_failures="${test_failures}\n  - Graceful shutdown failed (exit $shutdown_exit)"
    fi

    # Summary
    echo ""
    local total=$((passed + failed))
    if [ $failed -eq 0 ]; then
        log_info "=== Smoke tests: $passed/$total passed ==="
    else
        log_error "=== Smoke tests: $passed/$total passed, $failed failed ==="
        echo -e "${RED}Failures:${NC}$test_failures"
        exit 1
    fi
}

if [ "$RUN_SMOKE_TEST" = true ]; then
    if [ "$USE_DOCKER" = false ]; then
        log_error "--smoke-test requires Docker (cannot use with --no-docker)"
        exit 1
    fi
    run_smoke_tests
fi
