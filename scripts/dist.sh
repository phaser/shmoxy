#!/bin/bash
# dist.sh - Build and publish all components to dist/
# Usage: ./scripts/dist.sh

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

# Components to publish
COMPONENTS=("shmoxy" "shmoxy.api" "shmoxy.frontend")

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
NEW_VERSION="$MAJOR.$MINOR.$NEW_PATCH"

log_info "New version: $NEW_VERSION"

# Clean and create dist directory
rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

# Build and publish each component
BUILD_TIMESTAMP=$(date -u '+%Y-%m-%dT%H:%M:%SZ')

for component in "${COMPONENTS[@]}"; do
    PROJECT_DIR="$SRC_DIR/$component"
    OUTPUT_DIR="$DIST_DIR/$component"

    if [ ! -d "$PROJECT_DIR" ]; then
        log_error "Project directory not found: $PROJECT_DIR"
        exit 1
    fi

    log_info "Publishing $component to $OUTPUT_DIR..."
    dotnet publish "$PROJECT_DIR" \
        -c Release \
        -o "$OUTPUT_DIR" \
        /p:Version="$NEW_VERSION" \
        --nologo \
        -v quiet

    log_info "$component published successfully"
done

# Update dist.yaml with new version
cat > "$DIST_YAML" << EOF
version: $NEW_VERSION
EOF

log_info "Updated dist.yaml to version $NEW_VERSION"

# Create git tag
TAG_NAME="v$NEW_VERSION"
if git -C "$REPO_ROOT" tag "$TAG_NAME" 2>/dev/null; then
    log_info "Created git tag: $TAG_NAME"
else
    log_warn "Git tag $TAG_NAME already exists, skipping"
fi

# Generate build manifest
MANIFEST_FILE="$DIST_DIR/manifest.json"
COMPONENT_JSON=""
for i in "${!COMPONENTS[@]}"; do
    if [ $i -gt 0 ]; then
        COMPONENT_JSON="$COMPONENT_JSON,"
    fi
    COMPONENT_JSON="$COMPONENT_JSON\"${COMPONENTS[$i]}\""
done

cat > "$MANIFEST_FILE" << EOF
{
  "version": "$NEW_VERSION",
  "timestamp": "$BUILD_TIMESTAMP",
  "components": [$COMPONENT_JSON],
  "git_commit": "$(git -C "$REPO_ROOT" rev-parse HEAD)",
  "git_tag": "$TAG_NAME"
}
EOF

log_info "Build manifest written to $MANIFEST_FILE"

echo ""
log_info "Distribution $NEW_VERSION complete!"
log_info "Output: $DIST_DIR"
