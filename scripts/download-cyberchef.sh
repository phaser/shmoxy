#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TARGET_DIR="$PROJECT_ROOT/src/shmoxy.frontend/wwwroot/cyberchef"

echo "Fetching latest CyberChef release info from GitHub..."
RELEASE_JSON=$(curl -sL "https://api.github.com/repos/gchq/CyberChef/releases/latest")
TAG=$(echo "$RELEASE_JSON" | grep -m1 '"tag_name"' | sed 's/.*"tag_name": *"\([^"]*\)".*/\1/')
ZIP_URL=$(echo "$RELEASE_JSON" | grep '"browser_download_url".*\.zip"' | head -1 | sed 's/.*"browser_download_url": *"\([^"]*\)".*/\1/')

if [ -z "$ZIP_URL" ]; then
    echo "Error: Could not find CyberChef ZIP download URL."
    exit 1
fi

echo "Downloading CyberChef $TAG..."
TEMP_DIR=$(mktemp -d)
trap 'rm -rf "$TEMP_DIR"' EXIT

curl -sL "$ZIP_URL" -o "$TEMP_DIR/cyberchef.zip"

echo "Extracting to $TARGET_DIR..."
rm -rf "$TARGET_DIR"
mkdir -p "$TARGET_DIR"
unzip -q "$TEMP_DIR/cyberchef.zip" -d "$TARGET_DIR"

# Create a stable CyberChef.html symlink to the versioned HTML file
VERSIONED_HTML=$(find "$TARGET_DIR" -maxdepth 1 -name 'CyberChef_v*.html' | head -1)
if [ -n "$VERSIONED_HTML" ]; then
    ln -sf "$(basename "$VERSIONED_HTML")" "$TARGET_DIR/CyberChef.html"
    echo "Created symlink: CyberChef.html -> $(basename "$VERSIONED_HTML")"
fi

# Download the Apache 2.0 LICENSE file
echo "Downloading LICENSE..."
curl -sL "https://raw.githubusercontent.com/gchq/CyberChef/master/LICENSE" -o "$TARGET_DIR/LICENSE"

echo "CyberChef $TAG installed successfully at $TARGET_DIR"
