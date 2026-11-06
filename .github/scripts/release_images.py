"""Image tags for a release, and the version the deploy files pin.

    release_images.py tags <version>   writes each image's tag list to $GITHUB_OUTPUT
    release_images.py check [version]  fails unless every deploy file pins <version>
                                       (default: SharpMUSHVersion in Directory.Build.props)

A release vX.Y.Z pushes :X.Y.Z, and moves :X.Y, :X and :latest only while it is the highest
release under each of them, so publishing 1.0.1 after 1.1.0 leaves :1 and :latest on 1.1.0.
A prerelease (X.Y.Z-rc.1) pushes only its own tag.
"""
import os
import re
import subprocess
import sys
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

# Every file a game is deployed from, and how it names the image tag. Each must name it at least
# once, and only as the release's version. docker-compose.cloudflare.yml is not here: it is the
# test stack, and follows :dev on purpose so watchtower deploys every merge to main.
TAG = r'([0-9A-Za-z._-]+)'
PINS = {
    'deploy/docker-compose.prod.yml': [rf'image: sharpmush/sharpmush-[a-z]+:{TAG}'],
    'deploy/oneclick/compose.yml': [rf'sharpmush/sharpmush-[a-z]+:\$\{{SHARPMUSH_IMAGE_TAG:-{TAG}\}}'],
    'deploy/oneclick/install.sh': [rf'^DEFAULT_IMAGE_TAG={TAG}$'],
    'deploy/oneclick/digitalocean/sharpmush.pkr.hcl': [rf'variable "image_tag" \{{[^}}]*default\s*=\s*"{TAG}"'],
    'deploy/fly/Dockerfile': [rf'^ARG SHARPMUSH_IMAGE_TAG={TAG}$'],
    'deploy/fly/fly.toml': [rf'image = "sharpmush/sharpmush-allinone:{TAG}"'],
    'deploy/fly/edge.fly.toml': [rf'image = "sharpmush/sharpmush-edge:{TAG}"'],
    'deploy/fly/server.fly.toml': [rf'image = "sharpmush/sharpmush-server:{TAG}"'],
    'deploy/fly/renderer.fly.toml': [rf'image = "sharpmush/sharpmush-renderer:{TAG}"'],
    'deploy/railway/README.md': [rf'`sharpmush/sharpmush-[a-z]+:{TAG}`'],
}


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
    """The versions of every vX.Y.Z tag on origin."""
    out = subprocess.run(['git', 'ls-remote', '--tags', '--refs', 'origin', 'refs/tags/v*'],
                         check=True, capture_output=True, text=True).stdout
    return [line.rsplit('refs/tags/v', 1)[1] for line in out.splitlines() if 'refs/tags/v' in line]


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


def project_version(root=ROOT):
    props = (root / 'Directory.Build.props').read_text(encoding='utf-8')
    match = re.search(r'<SharpMUSHVersion[^>]*>([^<]+)</SharpMUSHVersion>', props)
    if not match:
        raise ValueError('Directory.Build.props sets no SharpMUSHVersion')
    return match.group(1)


def mismatches(version, root=ROOT):
    """Each deploy file that does not pin `version`, with what it pins instead."""
    problems = []
    for path, patterns in PINS.items():
        text = (root / path).read_text(encoding='utf-8')
        found = [tag for pattern in patterns for tag in re.findall(pattern, text, re.MULTILINE)]
        if not found:
            problems.append(f'{path}: names no image tag')
        problems.extend(f'{path}: pins {tag}, not {version}' for tag in sorted(set(found)) if tag != version)
    return problems


def main(argv):
    if len(argv) == 2 and argv[0] == 'tags':
        write_tags(argv[1])
        return 0
    if len(argv) in (1, 2) and argv[0] == 'check':
        version = argv[1] if len(argv) == 2 else project_version()
        problems = mismatches(version)
        for problem in problems:
            print(f'::error::{problem}')
        if not problems:
            print(f'Every deploy file pins {version}.')
        return 1 if problems else 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
