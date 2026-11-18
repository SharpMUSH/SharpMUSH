"""Image tags for a release, and the image tags the deploy files name.

    release_images.py tags <version>   writes each image's tag list to $GITHUB_OUTPUT
    release_images.py check            fails unless the deploy files agree (see below)

Every deploy file (the production Compose file, one-click VPS, Fly, Railway) runs :latest, the
newest release, or :dev; never an exact version, so a release needs no change to them. A game that
wants to hold one release sets SHARPMUSH_IMAGE_TAG.

A release vX.Y.Z pushes :X.Y.Z, and moves :X.Y, :X and :latest only while it is the highest
release under each of them, so publishing 1.0.1 after 1.1.0 leaves :1 and :latest on 1.1.0.
A prerelease (X.Y.Z-rc.1) pushes its own tag. docker-publish.yml then points :dev at it when it is
main's newest commit, holding the dev pipeline's lock so the two never write :dev at once.
"""
import json
import os
import re
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

IMAGES = {
    'server': 'sharpmush/sharpmush-server',
    'renderer': 'sharpmush/sharpmush-renderer',
    'socketserver': 'sharpmush/sharpmush-socketserver',
    'edge': 'sharpmush/sharpmush-edge',
    'allinone': 'sharpmush/sharpmush-allinone',
}

STABLE = re.compile(r'^(\d+)\.(\d+)\.(\d+)$')
SEMVER = re.compile(r'^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')

# Every file a game is deployed from, and how it names the image tag: each pattern's one group is
# the tag. docker-compose.cloudflare.yml is not here: it is the test stack, and follows :dev on
# purpose so watchtower deploys every merge to main.
TAG = r'([0-9A-Za-z._-]+)'
# Any image named outright in one of those files, however it is written, must also name the tag.
LITERAL = re.compile(r'sharpmush/sharpmush-[a-z]+:([0-9A-Za-z._-]+)')
PINS = {
    'deploy/docker-compose.prod.yml': [rf'image: sharpmush/sharpmush-[a-z]+:\$\{{SHARPMUSH_IMAGE_TAG:-{TAG}\}}'],
    'deploy/oneclick/compose.yml': [rf'sharpmush/sharpmush-[a-z]+:\$\{{SHARPMUSH_IMAGE_TAG:-{TAG}\}}'],
    'deploy/oneclick/install.sh': [rf'^DEFAULT_IMAGE_TAG={TAG}\b'],
    'deploy/oneclick/digitalocean/sharpmush.pkr.hcl': [rf'variable "image_tag" \{{[^}}]*default\s*=\s*"{TAG}"'],
    'deploy/fly/Dockerfile': [rf'^ARG SHARPMUSH_IMAGE_TAG={TAG}\b'],
    'deploy/fly/fly.toml': [rf'image = "sharpmush/sharpmush-allinone:{TAG}"'],
    'deploy/fly/edge.fly.toml': [rf'image = "sharpmush/sharpmush-edge:{TAG}"'],
    'deploy/fly/server.fly.toml': [rf'image = "sharpmush/sharpmush-server:{TAG}"'],
    'deploy/fly/renderer.fly.toml': [rf'image = "sharpmush/sharpmush-renderer:{TAG}"'],
    'deploy/railway/README.md': [rf'`sharpmush/sharpmush-[a-z]+:{TAG}`'],
}
# What a deploy file may name: the newest release, or the build of main.
DEPLOY_TAGS = {'dev', 'latest'}


def stable(version):
    match = STABLE.match(version)
    return tuple(int(part) for part in match.groups()) if match else None


def tags(version, released):
    """The tags `version` is pushed under, given the versions already released."""
    if not SEMVER.match(version):
        raise ValueError(f'{version!r} is not a version like 1.2.3 or 1.2.3-rc.1')
    own = stable(version)
    if own is None:
        return [version]
    others = [v for v in (stable(r) for r in released) if v is not None]
    result = [version]
    if all(v[:2] != own[:2] or v <= own for v in others):
        result.append(f'{own[0]}.{own[1]}')
    if all(v[0] != own[0] or v <= own for v in others):
        result.append(f'{own[0]}')
    if all(v <= own for v in others):
        result.append('latest')
    return result


def released_versions():
    """The versions of the repository's published, non-prerelease vX.Y.Z GitHub Releases.

    Releases, not tags: a stray tag, or one left by a refused prerelease, must not hold :latest back.
    """
    api = os.environ.get('GITHUB_API_URL', 'https://api.github.com')
    headers = {'Accept': 'application/vnd.github+json', 'Authorization': f'Bearer {os.environ["GITHUB_TOKEN"]}'}
    versions, page = [], 1
    while True:
        url = f'{api}/repos/{os.environ["GITHUB_REPOSITORY"]}/releases?per_page=100&page={page}'
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers)) as response:
            batch = json.load(response)
        versions += [r['tag_name'][1:] for r in batch
                     if not r['draft'] and not r['prerelease'] and r['tag_name'].startswith('v')]
        if len(batch) < 100:
            return versions
        page += 1


def write_tags(version):
    pushed = tags(version, released_versions())
    print(f'{version} is pushed as: {", ".join(pushed)}')
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
        output.write(f'version={version}\n')
        output.write(f'suffixes={" ".join(pushed)}\n')
        for key, repository in IMAGES.items():
            output.write(f'{key}<<EOF\n')
            output.write(''.join(f'{repository}:{tag}\n' for tag in pushed))
            output.write('EOF\n')


def pins(root=ROOT):
    """Each image tag a deploy file names: (path, tag, start, end) for every place it names the tag."""
    found = []
    for path, patterns in PINS.items():
        text = (root / path).read_text(encoding='utf-8')
        spans = {match.span(1) for pattern in patterns for match in re.finditer(pattern, text, re.MULTILINE)}
        spans |= {match.span(1) for match in LITERAL.finditer(text)}
        found.extend((path, text[a:b], a, b) for a, b in sorted(spans))
    return found


def problems(root=ROOT):
    """What is wrong with the deploy files: one that names no tag, an exact version, or tags that differ."""
    found = pins(root)
    named = {path for path, *_ in found}
    result = [f'{path}: names no image tag' for path in PINS if path not in named]
    result += [f'{path}: names {tag}; deploy files run {" or ".join(sorted(DEPLOY_TAGS))}'
               for path, tag, *_ in found if tag not in DEPLOY_TAGS]
    tags = sorted({tag for _, tag, *_ in found})
    if len(tags) > 1:
        result.append(f'the deploy files name different tags ({", ".join(tags)}); they must agree')
        result += [f'{path}: names {", ".join(sorted({t for p, t, *_ in found if p == path}))}'
                   for path in sorted(named, key=list(PINS).index)]
    return result


def main(argv):
    if len(argv) == 2 and argv[0] == 'tags':
        write_tags(argv[1])
        return 0
    if argv == ['check']:
        found = problems()
        for problem in found:
            print(f'::error::{problem}')
        if not found:
            print('The deploy files agree.')
        return 1 if found else 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
