#!/bin/bash
# Script to create GitHub issues for unimplemented SharpMUSH features
# This script requires gh CLI to be installed and authenticated

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JSON_FILE="$SCRIPT_DIR/github_issues.json"
REPO="SharpMUSH/SharpMUSH"

# Check if jq is installed
if ! command -v jq &> /dev/null; then
    echo "Error: jq is required but not installed."
    echo "Install with: sudo apt-get install jq (Debian/Ubuntu) or brew install jq (macOS)"
    exit 1
fi

# Check if gh is installed
if ! command -v gh &> /dev/null; then
    echo "Error: gh CLI is required but not installed."
    echo "Install from: https://cli.github.com/"
    exit 1
fi

# Check if authenticated
if ! gh auth status &> /dev/null; then
    echo "Error: Not authenticated with GitHub."
    echo "Run: gh auth login"
    exit 1
fi

# Check if JSON file exists
if [ ! -f "$JSON_FILE" ]; then
    echo "Error: JSON file not found: $JSON_FILE"
    exit 1
fi

echo "Creating GitHub issues for unimplemented SharpMUSH features..."
echo "Repository: $REPO"
echo ""

# Read assignee from JSON
ASSIGNEE=$(jq -r '.assignee' "$JSON_FILE")
# Remove @ prefix if present for gh CLI
ASSIGNEE_CLEAN="${ASSIGNEE#@}"

# Get number of issues
ISSUE_COUNT=$(jq '.issues | length' "$JSON_FILE")

echo "Will create $ISSUE_COUNT issues, assigned to $ASSIGNEE"
echo ""

# Create each issue
CREATED=0
FAILED=0

for i in $(seq 0 $((ISSUE_COUNT - 1))); do
    TITLE=$(jq -r ".issues[$i].title" "$JSON_FILE")
    BODY=$(jq -r ".issues[$i].body" "$JSON_FILE")
    LABELS=$(jq -r ".issues[$i].labels | join(\",\")" "$JSON_FILE")
    
    echo "[$((i + 1))/$ISSUE_COUNT] Creating: $TITLE"
    
    if gh issue create \
        --repo "$REPO" \
        --title "$TITLE" \
        --body "$BODY" \
        --label "$LABELS" \
        --assignee "$ASSIGNEE_CLEAN" 2>&1; then
        CREATED=$((CREATED + 1))
        echo "  ✓ Success"
    else
        FAILED=$((FAILED + 1))
        echo "  ✗ Failed"
    fi
    echo ""
done

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Summary:"
echo "  ✓ Created: $CREATED"
if [ $FAILED -gt 0 ]; then
    echo "  ✗ Failed: $FAILED"
fi
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if [ $CREATED -gt 0 ]; then
    echo ""
    echo "Verify issues created:"
    echo "  gh issue list --repo $REPO --label enhancement --assignee $ASSIGNEE_CLEAN"
fi
