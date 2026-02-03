# DockerHub Automation Setup Guide

This guide explains how to set up automatic Docker image publishing to DockerHub when new versions are released.

## Overview

The repository is configured to automatically build and publish Docker images to DockerHub whenever a new version tag is pushed to the `main` branch. The workflow builds two images:
- `sharpmush/sharpmush-server` - The main SharpMUSH server
- `sharpmush/sharpmush-connectionserver` - The connection server

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

## How to Trigger a Build

The workflow automatically runs when you push a version tag to the repository. Here's how to create and push a version tag:

### Creating a Version Tag

```bash
# Create a new version tag (e.g., v1.0.0)
git tag v1.0.0

# Push the tag to GitHub
git push origin v1.0.0
```

### Version Tag Format

- Tags must follow the format: `v*.*.*` (e.g., `v1.0.0`, `v2.1.3`, `v0.9.0`)
- The workflow will automatically extract the version number and use it to tag the Docker images

### What Gets Published

When you push a tag like `v1.0.0`, the workflow will publish:
- `sharpmush/sharpmush-server:1.0.0`
- `sharpmush/sharpmush-server:latest`
- `sharpmush/sharpmush-connectionserver:1.0.0`
- `sharpmush/sharpmush-connectionserver:latest`

The `latest` tag is always updated to point to the most recent version.

## Manual Triggering

You can also manually trigger the workflow from the GitHub Actions UI:
1. Go to **Actions** tab in your repository
2. Select **Publish Docker Images** workflow
3. Click **Run workflow**
4. Select the branch/tag and click **Run workflow**

## Verifying the Workflow

After setting up the secrets:
1. Create and push a test tag: `git tag v0.0.1 && git push origin v0.0.1`
2. Go to the **Actions** tab in your repository
3. You should see the **Publish Docker Images** workflow running
4. Once complete, verify the images are on DockerHub at:
   - https://hub.docker.com/r/sharpmush/sharpmush-server
   - https://hub.docker.com/r/sharpmush/sharpmush-connectionserver

## Troubleshooting

### Authentication Failed
- Verify that `DOCKERHUB_USERNAME` is correct
- Ensure `DOCKERHUB_TOKEN` is a valid access token (not your password)
- Check that the token has Write permissions

### Images Not Appearing on DockerHub
- Verify the repository names are correct (`sharpmush/sharpmush-server` and `sharpmush/sharpmush-connectionserver`)
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
