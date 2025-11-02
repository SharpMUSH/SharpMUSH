#!/usr/bin/env python3
"""
Script to create GitHub issues for SharpMUSH unimplemented features.

This script can be run with a GitHub token to automatically create all issues.

Usage:
    # Using environment variable
    export GITHUB_TOKEN="your_token_here"
    python3 create_github_issues.py

    # Or pass token as argument
    python3 create_github_issues.py --token "your_token_here"

    # Dry run (no actual creation)
    python3 create_github_issues.py --dry-run
"""

import json
import os
import sys
import argparse
from pathlib import Path

try:
    import requests
except ImportError:
    print("Error: requests library not installed")
    print("Install with: pip install requests")
    sys.exit(1)

# Configuration
SCRIPT_DIR = Path(__file__).parent
JSON_FILE = SCRIPT_DIR / 'github_issues.json'
API_BASE = 'https://api.github.com'


def load_issues_data():
    """Load issue data from JSON file"""
    if not JSON_FILE.exists():
        print(f"Error: JSON file not found: {JSON_FILE}")
        sys.exit(1)
    
    with open(JSON_FILE, 'r') as f:
        return json.load(f)


def create_issue(token, repo, title, body, labels, assignee):
    """Create a GitHub issue using the REST API"""
    
    url = f'{API_BASE}/repos/{repo}/issues'
    headers = {
        'Authorization': f'token {token}',
        'Accept': 'application/vnd.github.v3+json',
    }
    
    # Remove @ prefix from assignee if present
    assignee_clean = assignee.lstrip('@')
    
    data = {
        'title': title,
        'body': body,
        'labels': labels,
        'assignees': [assignee_clean]
    }
    
    try:
        response = requests.post(url, headers=headers, json=data, timeout=30)
        response.raise_for_status()
        issue_data = response.json()
        return issue_data, None
    except requests.exceptions.RequestException as e:
        error_msg = str(e)
        if hasattr(e, 'response') and e.response is not None:
            try:
                error_detail = e.response.json()
                error_msg = f"{error_msg}\nDetails: {error_detail}"
            except:
                error_msg = f"{error_msg}\nResponse: {e.response.text}"
        return None, error_msg


def main():
    parser = argparse.ArgumentParser(
        description='Create GitHub issues for SharpMUSH unimplemented features'
    )
    parser.add_argument(
        '--token',
        help='GitHub personal access token (or use GITHUB_TOKEN env var)'
    )
    parser.add_argument(
        '--dry-run',
        action='store_true',
        help='Show what would be created without actually creating issues'
    )
    
    args = parser.parse_args()
    
    # Get token from args or environment
    token = args.token or os.environ.get('GITHUB_TOKEN')
    dry_run = args.dry_run or not token
    
    # Load issue data
    data = load_issues_data()
    repo = data['repository']
    assignee = data['assignee']
    issues = data['issues']
    
    print("=" * 80)
    print("SharpMUSH Issue Creation Script")
    print("=" * 80)
    print(f"Repository: {repo}")
    print(f"Assignee: {assignee}")
    print(f"Issues to create: {len(issues)}")
    print("=" * 80)
    print()
    
    if dry_run:
        if not token:
            print("⚠️  WARNING: GITHUB_TOKEN not set")
        print("Running in DRY-RUN mode - no issues will be created")
        print()
        print("To create issues:")
        print("  1. Set GITHUB_TOKEN environment variable:")
        print("     export GITHUB_TOKEN='your_token_here'")
        print("  2. Run this script again")
        print()
        print("Or use the bash script:")
        print("  bash scripts/create_github_issues.sh")
        print()
    
    created = 0
    failed = 0
    
    for i, issue in enumerate(issues, 1):
        title = issue['title']
        body = issue['body']
        labels = issue['labels']
        
        print(f"[{i}/{len(issues)}] ", end='')
        
        if dry_run:
            print(f"Would create: {title}")
            print(f"  Labels: {', '.join(labels)}")
            print(f"  Assignee: {assignee}")
        else:
            print(f"Creating: {title}")
            result, error = create_issue(token, repo, title, body, labels, assignee)
            
            if result:
                created += 1
                print(f"  ✓ Success - Issue #{result['number']}")
            else:
                failed += 1
                print(f"  ✗ Failed")
                print(f"  Error: {error}")
        
        print()
    
    print("=" * 80)
    if dry_run:
        print(f"DRY-RUN complete - {len(issues)} issues ready to create")
    else:
        print(f"✓ Successfully created: {created}")
        if failed > 0:
            print(f"✗ Failed: {failed}")
        
        if created > 0:
            print()
            print("Verify issues created:")
            print(f"  gh issue list --repo {repo} --label enhancement")
    print("=" * 80)


if __name__ == '__main__':
    main()
