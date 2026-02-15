# Issue Creation Scripts

This directory contains automation scripts to create GitHub issues for the 224 unimplemented SharpMUSH features.

## Files

- **github_issues.json** (22KB) - Complete issue data with all 15 issue templates
- **create_github_issues.sh** - Bash script for gh CLI users
- **create_github_issues.py** - Python script for REST API users

## Quick Start

### Option 1: Using gh CLI (Recommended)

```bash
# Authenticate if needed
gh auth login

# Create all issues
./scripts/create_github_issues.sh
```

### Option 2: Using Python API

```bash
# Install dependencies
pip install requests

# Set your GitHub token
export GITHUB_TOKEN='your_personal_access_token'

# Create all issues
./scripts/create_github_issues.py

# Or pass token as argument
./scripts/create_github_issues.py --token 'your_token'

# Dry run to see what would be created
./scripts/create_github_issues.py --dry-run
```

## What Gets Created

**15 GitHub Issues** will be created:

### Command Issues (7)
1. Implement Attributes Commands (5 items)
2. Implement Building/Creation Commands (13 items)
3. Implement Communication Commands (5 items)
4. Implement Database Management Commands (2 items)
5. Implement General Commands (21 items)
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

Each issue includes:
- ✅ Complete checklist of items to implement
- ✅ Testing requirements with unique test strings
- ✅ Implementation guidelines
- ✅ PennMUSH compatibility notes
- ✅ Proper labels (enhancement, commands/functions, category)
- ✅ Assigned to @copilot

## Requirements

### Bash Script
- `gh` CLI tool ([installation](https://cli.github.com/))
- `jq` for JSON parsing
- Authenticated GitHub session (`gh auth login`)

### Python Script
- Python 3.6+
- `requests` library (`pip install requests`)
- GitHub personal access token with `repo` scope

## Creating a GitHub Token

1. Go to GitHub Settings → Developer settings → Personal access tokens
2. Generate new token (classic)
3. Select scope: `repo` (Full control of private repositories)
4. Copy the token and use it with the scripts

## Verification

After running the script, verify issues were created:

```bash
gh issue list --repo SharpMUSH/SharpMUSH --label enhancement --assignee @copilot
```

Expected result: 15 issues

## Troubleshooting

### Bash Script Issues

**Error: jq not found**
```bash
# Ubuntu/Debian
sudo apt-get install jq

# macOS
brew install jq
```

**Error: Not authenticated**
```bash
gh auth login
```

### Python Script Issues

**Error: requests not installed**
```bash
pip install requests
```

**Error: 401 Unauthorized**
- Check that your token is valid
- Ensure token has `repo` scope
- Try regenerating the token

**Error: 422 Validation Failed**
- Issues might already exist
- Check for duplicate titles in the repository

## JSON File Structure

The `github_issues.json` file contains:

```json
{
  "repository": "SharpMUSH/SharpMUSH",
  "assignee": "@copilot",
  "issues": [
    {
      "title": "Issue title",
      "body": "Issue description with markdown",
      "labels": ["enhancement", "commands", "category"]
    }
  ]
}
```

## Next Steps

After creating the issues:

1. Review created issues in GitHub
2. Start with Core Utilities phase (see IMPLEMENTATION_ROADMAP.md)
3. Follow testing requirements in each issue
4. Update checklists as items are completed

## Support

For more information:
- See `ISSUE_CREATION_GUIDE.md` in repository root
- See `TASK_COMPLETION_SUMMARY.md` for overview
- See `IMPLEMENTATION_ROADMAP.md` for strategy
