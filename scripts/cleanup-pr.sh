#!/bin/bash
# cleanup-pr.sh - Clean up worktree and documentation after a PR is merged
# Usage: ./scripts/cleanup-pr.sh <pr-name>

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(git rev-parse --show-toplevel)"
WORKTREES_DIR="$REPO_ROOT/worktrees"
DOCS_PRS_DIR="$REPO_ROOT/docs/prs"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Validate arguments
if [ $# -lt 1 ]; then
    log_error "Usage: $0 <pr-name>"
    exit 1
fi

PR_NAME="$1"
SANITIZED_NAME=$(echo "$PR_NAME" | tr '[:upper:]' '[:lower:]' | tr ' ' '-' | sed 's/[^a-z0-9-]//g')

if [ -z "$SANITIZED_NAME" ]; then
    log_error "PR name cannot be empty after sanitization"
    exit 1
fi

WORKTREE_PATH="$WORKTREES_DIR/$SANITIZED_NAME"
DOC_FILE="$DOCS_PRS_DIR/${SANITIZED_NAME}.md"

# Check if worktree exists
if [ ! -d "$WORKTREE_PATH/.git" ]; then
    log_warn "Worktree does not exist at: $WORKTREE_PATH"
else
    # Remove the worktree (force if there are uncommitted changes)
    log_info "Removing worktree: $WORKTREE_PATH"
    git worktree remove -f "$WORKTREE_PATH" 2>/dev/null || true

    # Also remove the branch from this repo if it exists here
    BRANCH_NAME="pr/$SANITIZED_NAME"
    if git show-ref --verify --quiet "refs/heads/$BRANCH_NAME"; then
        log_info "Removing local branch: $BRANCH_NAME"
        git branch -D "$BRANCH_NAME" 2>/dev/null || true
    fi

    # Also try to remove from remote tracking (optional, may fail if already deleted)
    log_warn "You should also delete the remote branch on your Git hosting platform"
fi

# Update documentation marker
if [ -f "$DOC_FILE" ]; then
    log_info "Updating documentation: $DOC_FILE"

    # Add merged status to file
    if grep -q "^- \[x\] Merged$" "$DOC_FILE"; then
        log_warn "Documentation already marked as merged"
    else
        cat >> "$DOC_FILE" << EOF

**Merged:** $(date '+%Y-%m-%d %H:%M:%S')
EOF
    fi

    echo ""
    log_info "Documentation preserved at: $DOC_FILE"
else
    log_warn "No documentation found at: $DOC_FILE"
fi

echo ""
log_info "Cleanup complete!"
