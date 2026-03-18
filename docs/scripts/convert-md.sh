#!/bin/bash

# Convert markdown files to HTML and PDF with Mermaid diagram support
# Usage: ./convert-md.sh <input.md> [output-format]
#
# Prerequisites:
#   Homebrew:
#     brew install pandoc mermaid-cli librsvg mactex-no-gui
#
#   First-time setup for mermaid-cli:
#     npx puppeteer browsers install chrome-headless-shell
#
#   Nix:
#     nix-shell -p pandoc mermaid-cli librsvg texlive.combined.scheme

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCS_DIR="$(dirname "$SCRIPT_DIR")"

# Set puppeteer executable path
export PUPPETEER_EXECUTABLE_PATH="$HOME/.cache/puppeteer/chrome-headless-shell/mac_arm-*/chrome-headless-shell-mac-arm64/chrome-headless-shell"

# Resolve the glob pattern
if [[ -z "$PUPPETEER_EXECUTABLE_PATH" ]] || [[ ! -x "$PUPPETEER_EXECUTABLE_PATH" ]]; then
    PUPPETEER_EXECUTABLE_PATH=$(find "$HOME/.cache/puppeteer" -name "chrome-headless-shell" -type f 2>/dev/null | head -1)
fi

if [[ -z "$PUPPETEER_EXECUTABLE_PATH" ]] || [[ ! -x "$PUPPETEER_EXECUTABLE_PATH" ]]; then
    echo "Error: Chrome headless shell not found for puppeteer."
    echo ""
    echo "Run this command to install:"
    echo "  npx puppeteer browsers install chrome-headless-shell"
    echo ""
    exit 1
fi

export PUPPETEER_EXECUTABLE_PATH

# Default input file
INPUT_FILE="${1:-$DOCS_DIR/proxy-server-architecture.md}"
OUTPUT_FORMAT="${2:-both}"

# Validate input file exists
if [ ! -f "$INPUT_FILE" ]; then
    echo "Error: Input file not found: $INPUT_FILE"
    exit 1
fi

# Get base name without extension
BASE_NAME=$(basename "$INPUT_FILE" .md)
INPUT_DIR=$(dirname "$INPUT_FILE")
OUTPUT_DIR="$INPUT_DIR/${BASE_NAME}-assets"
TEMP_DIR=$(mktemp -d)

# Create output directory for diagrams
mkdir -p "$OUTPUT_DIR"

# Cleanup temp dir on exit (but keep output dir)
trap "rm -rf $TEMP_DIR" EXIT

echo "Converting: $INPUT_FILE"
echo "Output format: $OUTPUT_FORMAT"
echo "Using Chrome: $PUPPETEER_EXECUTABLE_PATH"

# Function to extract and convert Mermaid diagrams
convert_mermaid_diagrams() {
    local input_file="$1"
    local output_file="$2"
    
    local diagram_count=0
    local in_mermaid_block=false
    local mermaid_content=""
    
    while IFS= read -r line || [[ -n "$line" ]]; do
        if [[ "$line" =~ ^\`\`\`mermaid ]]; then
            in_mermaid_block=true
            mermaid_content=""
            continue
        fi
        
        if [[ "$line" =~ ^\`\`\` ]] && [ "$in_mermaid_block" = true ]; then
            in_mermaid_block=false
            diagram_count=$((diagram_count + 1))
            
            # Save mermaid content to temp file
            local mmd_file="$TEMP_DIR/diagram_$diagram_count.mmd"
            local svg_file="$OUTPUT_DIR/diagram_$diagram_count.svg"
            local png_file="$OUTPUT_DIR/diagram_$diagram_count.png"
            echo "$mermaid_content" > "$mmd_file"
            
            # Convert to PNG directly using mmdc (better text rendering than rsvg-convert)
            echo "  Converting Mermaid diagram $diagram_count..." >&2
            if mmdc -i "$mmd_file" -o "$png_file" -w 1200 -b white 2>/dev/null; then
                # Also create SVG for HTML output
                mmdc -i "$mmd_file" -o "$svg_file" -w 1200 -b white 2>/dev/null
                
                # Output image reference with absolute path for pandoc
                echo ""
                echo "![Diagram $diagram_count]($png_file)"
                echo ""
            else
                echo "  Warning: Failed to convert diagram $diagram_count" >&2
                echo "$mermaid_content"
            fi
            continue
        fi
        
        if [ "$in_mermaid_block" = true ]; then
            mermaid_content+="$line"$'\n'
        else
            echo "$line"
        fi
    done < "$input_file" > "$output_file"
    
    echo "Converted $diagram_count Mermaid diagram(s)" >&2
}

# Convert to HTML
if [ "$OUTPUT_FORMAT" = "html" ] || [ "$OUTPUT_FORMAT" = "both" ]; then
    OUTPUT_HTML="$INPUT_DIR/$BASE_NAME.html"
    echo "Generating HTML: $OUTPUT_HTML"
    
    # Convert mermaid diagrams first
    CONVERTED_MD="$TEMP_DIR/converted.md"
    convert_mermaid_diagrams "$INPUT_FILE" "$CONVERTED_MD"
    
    pandoc "$CONVERTED_MD" \
        --standalone \
        --toc \
        --toc-depth=3 \
        --css="$DOCS_DIR/styles/markdown.css" \
        --from=gfm \
        -o "$OUTPUT_HTML"
    
    echo "✓ HTML generated: $OUTPUT_HTML"
fi

# Convert to PDF
if [ "$OUTPUT_FORMAT" = "pdf" ] || [ "$OUTPUT_FORMAT" = "both" ]; then
    OUTPUT_PDF="$INPUT_DIR/$BASE_NAME.pdf"
    echo "Generating PDF: $OUTPUT_PDF"
    
    # Convert mermaid diagrams first
    CONVERTED_MD="$TEMP_DIR/converted.md"
    convert_mermaid_diagrams "$INPUT_FILE" "$CONVERTED_MD"
    
    # Convert to PDF using xelatex
    pandoc "$CONVERTED_MD" \
        --standalone \
        --toc \
        --toc-depth=3 \
        --pdf-engine=xelatex \
        -o "$OUTPUT_PDF"
    
    echo "✓ PDF generated: $OUTPUT_PDF"
fi

echo ""
echo "Done!"
echo "Output files:"
echo "  - $OUTPUT_HTML"
echo "  - $OUTPUT_PDF"
echo "  - $OUTPUT_DIR/ (diagram assets)"
