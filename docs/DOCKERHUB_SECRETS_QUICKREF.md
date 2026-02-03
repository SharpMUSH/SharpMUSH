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

1. Push a version tag: `git tag v1.0.0 && git push origin v1.0.0`
2. GitHub Actions automatically builds and pushes:
   - `sharpmush/sharpmush-server:1.0.0` and `:latest`
   - `sharpmush/sharpmush-connectionserver:1.0.0` and `:latest`

## Full Documentation

See [DOCKERHUB_SETUP.md](./DOCKERHUB_SETUP.md) for complete setup instructions and troubleshooting.
