# GitHub Issue Creation Guide

## Overview
This guide explains how to create GitHub issues for the 224 unimplemented commands and functions identified in SharpMUSH.

## Quick Start

### Option 1: Using the Shell Script (Recommended for gh CLI users)
A shell script has been created to automate issue creation:

```bash
# The script is available at scripts/create_github_issues.sh
# To use it, you need gh CLI authenticated:
gh auth login
bash scripts/create_github_issues.sh
```

### Option 2: Using the Python API Script (Recommended for API users)
A Python script is available that uses the GitHub REST API:

```bash
# Install requests if needed
pip install requests

# Set your GitHub token
export GITHUB_TOKEN='your_personal_access_token'

# Run the script
python3 scripts/create_github_issues.py
```

The script will run in DRY-RUN mode without a token to show what would be created.

### Option 3: Using the JSON File
A structured JSON file is available at `scripts/github_issues.json` containing all issue data for programmatic creation.

### Option 4: Manual Creation from UNIMPLEMENTED_ANALYSIS.md
The file `UNIMPLEMENTED_ANALYSIS.md` contains all 15 issues in markdown format. Each section can be manually copied to create a GitHub issue.

## Issue Structure

Each issue follows this structure:

- **Title**: "Implement [Category] [Commands/Functions]"
- **Labels**: 
  - `enhancement` (all issues)
  - `commands` or `functions` (type-specific)
  - Category-specific label (e.g., `attributes`, `communication`, etc.)
- **Assignee**: @copilot
- **Body**: Contains:
  - Category description
  - Checklist of items to implement
  - Implementation requirements
  - Testing guidelines
  - File locations
  - Total count

## Issues to Create (15 total)

### Command Issues (7)
1. Implement Attributes Commands (5 items)
2. Implement Building/Creation Commands (13 items)
3. Implement Communication Commands (5 items)
4. Implement Database Management Commands (2 items)
5. Implement General Commands Commands (21 items)
6. Implement HTTP Commands (1 item)
7. Implement Other Commands (60 items)

### Function Issues (8)
8. Implement Attributes Functions (12 items)
9. Implement Connection Management Functions (4 items)
10. Implement Database/SQL Functions (3 items)
11. Implement HTML Functions (6 items)
12. Implement JSON Functions (3 items)
13. Implement Math & Encoding Functions (13 items)
14. Implement Object Information Functions (39 items)
15. Implement Utility Functions (37 items)

## Testing Requirements

All issues specify that implementations must include:

1. **Comprehensive Unit Tests** with:
   - Actual/expected value comparisons
   - Unique test strings (e.g., "test_string_FUNCTIONNAME_case1")
   - Multiple argument combinations (for functions)
   - Edge case testing
   - Permission testing (for commands)

2. **Documentation Updates** where applicable

3. **PennMUSH Compatibility** verification

## Next Steps

After creating the issues:

1. All issues should be assigned to @copilot
2. Issues can be tackled incrementally by category
3. Each issue contains a checklist that can be updated as items are implemented
4. Cross-reference with PennMUSH documentation for implementation details

## Files Created

- `UNIMPLEMENTED_ANALYSIS.md` - Full analysis with all issue content
- `ANALYSIS_SUMMARY.md` - Quick reference summary
- `scripts/create_github_issues.sh` - Shell script for gh CLI automated creation
- `scripts/create_github_issues.py` - Python script for REST API automated creation
- `scripts/github_issues.json` - JSON data for programmatic creation
- This file - Guide for issue creation

## Analysis Details

The analysis identified:
- 208 NotImplementedException occurrences
- 107 unimplemented commands
- 117 unimplemented functions
- 14 distinct categories

All items were categorized by functionality and file location for easier implementation planning.
