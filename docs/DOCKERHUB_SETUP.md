# DockerHub Automation Setup Guide

This guide explains how to set up automatic Docker image publishing to DockerHub when new versions are released.

## Overview

The repository has two Docker publishing workflows:

### 1. Releases
Publishing a GitHub Release tagged `vX.Y.Z` builds and publishes the images at that tag:
- `sharpmush/sharpmush-server` - the game server and the web portal
- `sharpmush/sharpmush-renderer` - the rendering worker
- `sharpmush/sharpmush-socketserver` - the socket owner
- `sharpmush/sharpmush-edge` - Caddy, NATS and the socket owner, for split one-container hosts
- `sharpmush/sharpmush-allinone` - everything in one container

### 2. Dev Builds (main branch)
Automatically builds and publishes Docker images with the `dev` tag on every push to the `main` branch:
- `sharpmush/sharpmush-server:dev` - Latest development build of the server
- `sharpmush/sharpmush-renderer:dev` - Latest development build of the rendering worker

## Required GitHub Secrets

To enable this automation, you need to configure two GitHub repository secrets:

### 1. DOCKERHUB_USERNAME

This is your DockerHub username or organization name.

**How to set it up:**
1. Go to your repository on GitHub
2. Navigate to **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Name: `DOCKERHUB_USERNAME`
5. Value: Your DockerHub username (e.g., `sharpmush`)
6. Click **Add secret**

### 2. DOCKERHUB_TOKEN

This is a DockerHub access token (not your password) that allows GitHub Actions to push images.

**How to create a DockerHub access token:**
1. Log in to [DockerHub](https://hub.docker.com/)
2. Click on your username in the top-right corner
3. Select **Account Settings**
4. Go to **Security** → **Personal Access Tokens** (or **New Access Token**)
5. Click **Generate New Token** or **New Access Token**
6. Give it a description (e.g., "GitHub Actions - SharpMUSH")
7. Set permissions to **Read, Write, Delete** (or at minimum **Read & Write**)
8. Click **Generate**
9. **Copy the token immediately** - you won't be able to see it again!

**How to add the token to GitHub:**
1. Go to your repository on GitHub
2. Navigate to **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Name: `DOCKERHUB_TOKEN`
5. Value: Paste the access token you copied from DockerHub
6. Click **Add secret**

## How to Trigger Builds

### Releases

A release is cut in the GitHub UI, never by pushing a tag. Before it, on `main`:

- `CHANGELOG.md` has a `## [1.0.0]` section. The images are built after the release is published,
  so a missing section leaves a published release with no images until a manual run.
- `SharpMUSHVersion` in `Directory.Build.props` names the version, so builds from the tagged commit
  agree with the images.

Then, under **Releases → Draft a new release**, create the tag `v1.0.0` on `main`, write the notes,
and press **Publish release**. A release candidate gets a tag with a suffix (`v1.1.0-rc.1`) and
**Set as a pre-release**; the workflow refuses a pre-release whose tag has no suffix, and a
release whose tag has one.

The tag sets the version: the workflow tags the images with it and builds the server with it, so
`version()`, `@version` and MSSP report it. This is the engine's version. The plugin NuGets
(`SharpMUSH.Library`, `SharpMUSH.Implementation.Generated`) carry the plugin contract version and
are released apart, with a release tagged `plugin-nuget/vX.Y.Z`; the template pack with one tagged
`sharpmush-templates/vX.Y.Z`. Each publish workflow ignores the others' releases.

#### What Gets Published

Each image is pushed under the release's version and the shorter tags that point at the newest
release they cover. For `v1.2.3`:

| Tag | Moves when |
|---|---|
| `1.2.3` | always |
| `1.2` | no `1.2.x` release is newer |
| `1` | no `1.x.y` release is newer |
| `latest` | no release is newer |

So a `v1.0.4` fix published after `v1.1.0` moves `1.0` but leaves `1` and `latest` on `1.1.0`. A
pre-release pushes its own tag (`1.1.0-rc.1`). When its commit is still the newest on `main` once
its images are built, `dev` is moved to it too, while no dev build is running, so `dev` never goes
behind `main`. A merge in that moment can leave `dev` one commit behind until the next merge
publishes again.

#### After the release

The one-click files (`deploy/oneclick/`, the Fly settings files, the Railway settings) run
`latest`, so a one-click game is launched on the newest release. A one-click VPS game then updates
its server and renderer to each new release by itself, major releases included (watchtower); a Fly
or Railway game takes it when its operator deploys again. The other deploy files
(`docker-compose.prod.yml`, the Fly Dockerfile's base tag) pin one exact release. Until the first
release's images exist, both run `dev`. New games are deployed from the files on `main`, so the
files move only once the release's images exist. When the workflow has finished, open a pull
request with:

```bash
python3 .github/scripts/release_images.py bump 1.0.0
```

It pins the release in the exact files and moves the one-click files to `latest`; after the
first release only the exact files change. CI fails a build whose one-click files or exact files
disagree among themselves (`release_images.py check`). The Railway template lives in Railway's
template composer, not in this repository: set its three services' images to `latest` there once.
A DigitalOcean snapshot keeps the tag it was built with, so rebuild and resubmit it after the bump.

Which shorter tags move is decided from the repository's published GitHub Releases, not its git
tags, so a stray `vX.Y.Z` tag or a refused pre-release does not hold `latest` back.

### Dev Build (Main Branch Pushes)

The dev workflow automatically runs on **every push to the `main` branch**:

#### What Gets Published (Main Branch)

Every push to `main` updates:
- `sharpmush/sharpmush-server:dev`
- `sharpmush/sharpmush-renderer:dev`

These images reflect the latest state of the `main` branch, or a pre-release cut from it, for
development and testing.

## Manual Triggering

You can also manually trigger either workflow from the GitHub Actions UI:

### For Releases:
A manual run publishes a release's images again (after a failed run, say). It reads the tag's GitHub Release, so it refuses a tag with no published release and treats a pre-release as one.
1. Go to **Actions** tab in your repository
2. Select **Publish Docker Images** workflow
3. Click **Run workflow**
4. Select the release's `v*` tag (the workflow refuses a branch) and click **Run workflow**

### For Dev Builds:
1. Go to **Actions** tab in your repository
2. Select **Publish Dev Docker Images** workflow
3. Click **Run workflow**
4. Select the `main` branch and click **Run workflow**

## Verifying the Workflow

After setting up the secrets:
1. Publish a release as described above, or run the workflow manually on an existing one's tag
2. Go to the **Actions** tab in your repository
3. You should see the **Publish Docker Images** workflow running
4. Once complete, verify the images are on DockerHub at:
   - https://hub.docker.com/r/sharpmush/sharpmush-server
   - https://hub.docker.com/r/sharpmush/sharpmush-renderer

## Troubleshooting

### Authentication Failed
- Verify that `DOCKERHUB_USERNAME` is correct
- Ensure `DOCKERHUB_TOKEN` is a valid access token (not your password)
- Check that the token has Write permissions

### Images Not Appearing on DockerHub
- Verify the repository names are correct (`sharpmush/sharpmush-server` and `sharpmush/sharpmush-renderer`)
- Ensure the DockerHub account has permission to push to these repositories
- Check the workflow logs in the Actions tab for detailed error messages

### Build Failures
- Check the workflow logs in the Actions tab
- Verify the Dockerfiles are valid
- Ensure all required dependencies are accessible

## Additional Notes

- The workflow uses Docker Buildx for improved build performance and caching
- Build cache is stored in GitHub Actions cache to speed up subsequent builds
- Both images are built from multi-stage Dockerfiles to minimize final image size
- Images include OpenContainers metadata labels for better traceability
