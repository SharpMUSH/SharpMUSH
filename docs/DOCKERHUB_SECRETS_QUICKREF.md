# Quick Reference: DockerHub Secrets Setup

## Required Secrets

Add these two secrets to your GitHub repository:

### 1. DOCKERHUB_USERNAME
- **Location**: Settings → Secrets and variables → Actions → New repository secret
- **Value**: Your DockerHub username (e.g., `sharpmush`)

### 2. DOCKERHUB_TOKEN
- **Location**: Settings → Secrets and variables → Actions → New repository secret
- **Value**: DockerHub access token (create at hub.docker.com → Account Settings → Security → Personal Access Tokens)
- **Permissions**: Read & Write (or Read, Write, Delete)

## How It Works

### Production Releases
1. Publish a GitHub Release with a new tag `v1.0.0` (Releases → Draft a new release)
2. GitHub Actions builds and pushes the server, renderer, socketserver, edge and all-in-one images
   as `:1.0.0`, `:1.0`, `:1` and `:latest`
3. Open a pull request with `python3 .github/scripts/release_images.py bump 1.0.0`, which moves the
   deploy files to the new images

### Dev Builds
1. Push to main branch: `git push origin main`
2. GitHub Actions automatically builds and pushes:
   - `sharpmush/sharpmush-server:dev`
   - `sharpmush/sharpmush-renderer:dev`

## Full Documentation

See [DOCKERHUB_SETUP.md](./DOCKERHUB_SETUP.md) for complete setup instructions and troubleshooting.
