#!/bin/bash
# inspect.sh - Run JetBrains ReSharper code inspections
# Usage: ./scripts/inspect.sh [--fix] [--severity LEVEL]
#
# Runs inspectcode on the solution and reports findings.
# Requires: dotnet tool install -g JetBrains.ReSharper.GlobalTools
#
# Options:
#   --fix           Run cleanupcode to auto-fix issues after inspection
#   --severity LVL  Minimum severity: ERROR, WARNING, SUGGESTION, HINT, INFO (default: WARNING)
#   --project PAT   Only inspect projects matching pattern (e.g., "shmoxy")
#   --include PAT   Only inspect files matching pattern (e.g., "**/*Controller*.cs")

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SOLUTION="$REPO_ROOT/src/shmoxy.slnx"
REPORT_FILE="/tmp/shmoxy-inspect.xml"

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

# Defaults
SEVERITY="WARNING"
FIX_MODE=false
PROJECT_FILTER=""
INCLUDE_FILTER=""

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --fix)
            FIX_MODE=true
            shift
            ;;
        --severity)
            SEVERITY="$2"
            shift 2
            ;;
        --project)
            PROJECT_FILTER="$2"
            shift 2
            ;;
        --include)
            INCLUDE_FILTER="$2"
            shift 2
            ;;
        -h|--help)
            head -14 "$0" | tail -13
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

# Check that jb is available
if ! command -v jb &> /dev/null; then
    # Try the default dotnet tools path
    export PATH="$PATH:$HOME/.dotnet/tools"
    if ! command -v jb &> /dev/null; then
        echo -e "${RED}Error: JetBrains CLI tools not found.${NC}"
        echo "Install with: dotnet tool install -g JetBrains.ReSharper.GlobalTools"
        exit 1
    fi
fi

# Build inspectcode command
INSPECT_CMD=(jb inspectcode "$SOLUTION" --output="$REPORT_FILE" --format=Xml --severity="$SEVERITY" --no-swea)

if [[ -n "$PROJECT_FILTER" ]]; then
    INSPECT_CMD+=(--project="$PROJECT_FILTER")
fi

if [[ -n "$INCLUDE_FILTER" ]]; then
    INSPECT_CMD+=(--include="$INCLUDE_FILTER")
fi

echo -e "${CYAN}Running ReSharper inspections (severity >= $SEVERITY)...${NC}"
"${INSPECT_CMD[@]}" 2>&1 | grep -v "^Inspecting " | grep -v "^$"

if [[ ! -f "$REPORT_FILE" ]]; then
    echo -e "${RED}Error: Inspection report was not generated.${NC}"
    exit 1
fi

# Parse and display results
ISSUE_COUNT=$(grep -c '<Issue ' "$REPORT_FILE" 2>/dev/null || true)
ISSUE_COUNT=${ISSUE_COUNT:-0}
ISSUE_COUNT=$(echo "$ISSUE_COUNT" | tr -d '[:space:]')

if [[ "$ISSUE_COUNT" -eq 0 ]]; then
    echo ""
    echo -e "${GREEN}No issues found.${NC}"
    exit 0
fi

echo ""
echo -e "${BOLD}Found $ISSUE_COUNT issue(s):${NC}"
echo ""

# Summary by type
echo -e "${BOLD}By category:${NC}"
grep '<Issue ' "$REPORT_FILE" \
    | sed 's/.*TypeId="\([^"]*\)".*/\1/' \
    | sort | uniq -c | sort -rn \
    | while read count type; do
        printf "  %4d  %s\n" "$count" "$type"
    done

echo ""

# Show individual issues grouped by file
echo -e "${BOLD}Details:${NC}"
grep '<Issue ' "$REPORT_FILE" \
    | python3 -c "
import sys, re
for line in sys.stdin:
    f = re.search(r'File=\"([^\"]*)\"', line)
    ln = re.search(r'Line=\"([^\"]*)\"', line)
    tid = re.search(r'TypeId=\"([^\"]*)\"', line)
    msg = re.search(r'Message=\"([^\"]*)\"', line)
    if f and ln and tid and msg:
        print(f'{f.group(1)}:{ln.group(1)}: {tid.group(1)} — {msg.group(1)}')
" | sort

echo ""

# Auto-fix if requested
if [[ "$FIX_MODE" = true ]]; then
    echo -e "${CYAN}Running cleanupcode to auto-fix issues...${NC}"
    CLEANUP_CMD=(jb cleanupcode "$SOLUTION")
    if [[ -n "$INCLUDE_FILTER" ]]; then
        CLEANUP_CMD+=(--include="$INCLUDE_FILTER")
    fi
    "${CLEANUP_CMD[@]}"
    echo -e "${GREEN}Cleanup complete. Re-run inspection to verify.${NC}"
fi

# Exit with non-zero if issues were found (useful in CI)
exit 1
