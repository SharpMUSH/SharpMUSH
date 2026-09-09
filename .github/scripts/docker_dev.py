#!/usr/bin/env python3
"""Debounce dev publishing and compare against each image actually on Docker Hub."""
import argparse
from datetime import datetime
import json
import os
import re
import subprocess
import time
from urllib.parse import quote

from image_impact import IMAGES, compare, git, report


def quiet_period(sha, created, latest, now=time.time, sleep=time.sleep):
    if latest() != sha:
        return False
    remaining = max(0, created + 600 - now())
    if remaining:
        print(f"Waiting {remaining:.0f}s for ten minutes without a newer push", flush=True)
        sleep(remaining)
    return latest() == sha


def image_revision(data):
    """Buildx returns one config or a platform-to-config map for an image index."""
    configs = [data] if 'config' in data else list(data.values())
    revisions = {((config.get('config') or {}).get('Labels') or {}).get(
        'org.opencontainers.image.revision') for config in configs}
    if len(revisions) == 1:
        revision = revisions.pop()
        if isinstance(revision, str) and re.fullmatch(r'[0-9a-f]{40}', revision):
            return revision
    return None


def published_revision(image):
    tag = f'sharpmush/sharpmush-{image}:dev'
    # Registry errors fail the run; they must not silently trigger all publications.
    raw = subprocess.check_output(['docker', 'buildx', 'imagetools', 'inspect', tag,
                                   '--format', '{{json .Image}}'], text=True)
    revision = image_revision(json.loads(raw))
    if revision:
        try:
            git('rev-parse', '--verify', revision + '^{commit}')
        except subprocess.CalledProcessError:
            revision = None
    if not revision:
        print(f'::warning::{tag} has no usable source revision; rebuilding this image')
    return revision


def published_impact(head):
    result = {}
    for image in IMAGES:
        base = published_revision(image) or '0' * 40
        changes, _ = compare(base, head)
        result[image] = changes[image]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['quiet', 'impact'])
    args = parser.parse_args()
    if args.command == 'quiet':
        def api(path):
            return json.loads(subprocess.check_output(['gh', 'api', path], text=True))
        repo = os.environ['GITHUB_REPOSITORY']
        run = api(f"repos/{repo}/actions/runs/{os.environ['GITHUB_RUN_ID']}")
        created = datetime.fromisoformat(run['created_at'].replace('Z', '+00:00')).timestamp()
        branch = quote(os.environ['GITHUB_REF_NAME'], safe='')
        ready = quiet_period(os.environ['GITHUB_SHA'], created,
                             lambda: api(f'repos/{repo}/branches/{branch}')['commit']['sha'])
        with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
            output.write(f'ready={str(ready).lower()}\n')
        if not ready:
            print('Superseded by a newer push; skipping validation and publication.')
    else:
        result = published_impact(os.environ['GITHUB_SHA'])
        summary = report(result)
        print(summary)
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as output:
            output.write(summary)
        with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
            for image, paths in result.items():
                output.write(f'{image}={str(bool(paths)).lower()}\n')


if __name__ == '__main__':
    main()
