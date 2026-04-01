#!/usr/bin/env bash
# Docker smoke tests for shmoxy
# Builds the image, starts a container, and runs basic health checks.
set -euo pipefail

CONTAINER_NAME="shmoxy-smoke-$$"
IMAGE_NAME="shmoxy-smoke:test"
API_PORT=15000
PROXY_PORT=18080
STARTUP_TIMEOUT=60
PASSED=0
FAILED=0

cleanup() {
    echo ""
    echo "=== Cleaning up ==="
    docker rm -f "$CONTAINER_NAME" 2>/dev/null || true
    docker rmi "$IMAGE_NAME" 2>/dev/null || true
}
trap cleanup EXIT

fail() {
    echo "  FAIL: $1"
    FAILED=$((FAILED + 1))
}

pass() {
    echo "  PASS: $1"
    PASSED=$((PASSED + 1))
}

# Build the image
echo "=== Building Docker image ==="
docker build -t "$IMAGE_NAME" . || { echo "Docker build failed"; exit 1; }

# Start the container
echo "=== Starting container ==="
docker run -d --name "$CONTAINER_NAME" \
    -p "$API_PORT:5000" \
    -p "$PROXY_PORT:8080" \
    -e "ASPNETCORE_URLS=http://+:5000" \
    -e "ApiConfig__ProxyPort=8080" \
    "$IMAGE_NAME"

# Wait for the API to be ready
echo "=== Waiting for API to be ready (up to ${STARTUP_TIMEOUT}s) ==="
elapsed=0
while [ $elapsed -lt $STARTUP_TIMEOUT ]; do
    if curl -sf "http://localhost:$API_PORT/api/health" >/dev/null 2>&1; then
        echo "  API ready after ${elapsed}s"
        break
    fi
    sleep 2
    elapsed=$((elapsed + 2))
done

if [ $elapsed -ge $STARTUP_TIMEOUT ]; then
    echo "  API failed to start within ${STARTUP_TIMEOUT}s"
    echo "=== Container logs ==="
    docker logs "$CONTAINER_NAME" 2>&1 | tail -50
    exit 1
fi

# Give proxy a few extra seconds to start after API is healthy
sleep 5

echo ""
echo "=== Running smoke tests ==="

# Test 1: Health endpoint returns 200
echo "[1] Health endpoint"
HTTP_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "http://localhost:$API_PORT/api/health" 2>/dev/null || echo "000")
if [ "$HTTP_CODE" = "200" ]; then
    pass "GET /api/health returned 200"
else
    fail "GET /api/health returned $HTTP_CODE (expected 200)"
fi

# Test 2: Web UI loads (Blazor app shell)
echo "[2] Web UI loads"
UI_RESPONSE=$(curl -sf "http://localhost:$API_PORT/" 2>/dev/null || echo "")
if echo "$UI_RESPONSE" | grep -qi "blazor\|_framework\|shmoxy"; then
    pass "Web UI returned HTML with Blazor app shell"
else
    fail "Web UI did not return expected Blazor content"
fi

# Test 3: Proxy status via API
echo "[3] Proxy status"
PROXY_STATE=$(curl -sf "http://localhost:$API_PORT/api/proxies/local" 2>/dev/null || echo "")
if echo "$PROXY_STATE" | grep -qi '"state".*:.*"Running"\|"running"'; then
    pass "Proxy state is Running"
else
    fail "Proxy state is not Running: $PROXY_STATE"
fi

# Test 4: Proxy is proxying traffic
echo "[4] Proxy traffic"
PROXY_RESPONSE=$(curl -sf --proxy "http://localhost:$PROXY_PORT" "http://httpbin.org/get" 2>/dev/null || echo "")
if echo "$PROXY_RESPONSE" | grep -q '"url"'; then
    pass "Proxy forwarded HTTP request successfully"
else
    fail "Proxy did not forward request (response: ${PROXY_RESPONSE:0:100})"
fi

# Test 5: CyberChef is bundled
echo "[5] CyberChef bundled"
CC_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "http://localhost:$API_PORT/cyberchef/CyberChef.html" 2>/dev/null || echo "000")
if [ "$CC_CODE" = "200" ]; then
    pass "CyberChef returned 200"
else
    fail "CyberChef returned $CC_CODE (expected 200)"
fi

# Test 6: Root CA certificate download (PEM)
echo "[6] Root CA cert download"
CERT_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "http://localhost:$API_PORT/api/proxies/local/certs/root?type=pem" 2>/dev/null || echo "000")
if [ "$CERT_CODE" = "200" ]; then
    pass "Root CA cert PEM download returned 200"
else
    fail "Root CA cert PEM download returned $CERT_CODE (expected 200)"
fi

# Test 7: Graceful shutdown
echo "[7] Graceful shutdown"
STOP_START=$(date +%s)
docker stop "$CONTAINER_NAME" --time 15 >/dev/null 2>&1
STOP_END=$(date +%s)
STOP_DURATION=$((STOP_END - STOP_START))
if [ $STOP_DURATION -le 15 ]; then
    pass "Container stopped gracefully in ${STOP_DURATION}s"
else
    fail "Container took ${STOP_DURATION}s to stop (expected <= 15s)"
fi

# Summary
echo ""
echo "=== Results ==="
echo "  Passed: $PASSED"
echo "  Failed: $FAILED"
echo "  Total:  $((PASSED + FAILED))"

if [ $FAILED -gt 0 ]; then
    echo ""
    echo "=== Container logs (last 30 lines) ==="
    docker logs "$CONTAINER_NAME" 2>&1 | tail -30 || true
    exit 1
fi

echo ""
echo "All smoke tests passed!"
