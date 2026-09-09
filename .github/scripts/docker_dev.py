#!/usr/bin/env python3
"""Compare dev image inputs against each image actually on Docker Hub."""
import json
import os
import re
import subprocess

from image_impact import IMAGES, compare, git, report


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
