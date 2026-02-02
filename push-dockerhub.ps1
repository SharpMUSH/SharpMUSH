#!/usr/bin/env pwsh
# Script to build and push SharpMUSH images to DockerHub

param(
    [string]$Tag = "dev"
)

$ErrorActionPreference = "Stop"

Write-Host "SharpMUSH DockerHub Push Script" -ForegroundColor Cyan
Write-Host "===============================" -ForegroundColor Cyan
Write-Host "Target Tag: $Tag" -ForegroundColor Gray
Write-Host ""

# Ensure we are in the script's directory (project root)
Set-Location $PSScriptRoot

# 1. Build and Push SharpMUSH Server
Write-Host "Processing SharpMUSH Server..." -ForegroundColor Cyan
Write-Host "  Building..." -ForegroundColor Gray
docker build -t sharpmush/sharpmush-server:$Tag -f Dockerfile .
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

Write-Host "  Pushing..." -ForegroundColor Gray
docker push sharpmush/sharpmush-server:$Tag
if ($LASTEXITCODE -ne 0) { 
    Write-Error "Push failed. Please ensure you have run 'docker login'."
    exit 1 
}

# 2. Build and Push ConnectionServer
Write-Host "Processing ConnectionServer..." -ForegroundColor Cyan
Write-Host "  Building..." -ForegroundColor Gray
# Note: Context is root (.), Dockerfile is in subdirectory
docker build -t sharpmush/sharpmush-connectionserver:$Tag -f SharpMUSH.ConnectionServer/Dockerfile .
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

Write-Host "  Pushing..." -ForegroundColor Gray
docker push sharpmush/sharpmush-connectionserver:$Tag
if ($LASTEXITCODE -ne 0) { 
    Write-Error "Push failed."
    exit 1 
}

Write-Host ""
Write-Host "Successfully pushed images to DockerHub!" -ForegroundColor Green
Write-Host "  - sharpmush/sharpmush-server:$Tag"
Write-Host "  - sharpmush/sharpmush-connectionserver:$Tag"