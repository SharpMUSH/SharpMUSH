#!/usr/bin/env pwsh
# Script to build Docker images and deploy to local Kubernetes (Docker Desktop)

param(
    [switch]$SkipBuild,
    [switch]$Delete
)

$ErrorActionPreference = "Stop"

Write-Host "SharpMUSH Local Kubernetes Deployment Script" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Navigate to the root directory
$rootDir = Split-Path -Parent $PSScriptRoot
Set-Location $rootDir

if ($Delete) {
    Write-Host "Deleting existing deployment..." -ForegroundColor Yellow
    kubectl delete -f kubernetes/dev-k8s.yaml --ignore-not-found=true
    Write-Host "Deployment deleted successfully!" -ForegroundColor Green
    exit 0
}

if (-not $SkipBuild) {
    Write-Host "Building Docker images..." -ForegroundColor Yellow
    Write-Host ""
    
    # Build SharpMUSH Server
    Write-Host "Building SharpMUSH Server image..." -ForegroundColor Cyan
    docker build -t sharpmush/sharpmush-server:dev -f Dockerfile .
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to build SharpMUSH Server image" -ForegroundColor Red
        exit 1
    }
    
    # Build ConnectionServer
    Write-Host "Building ConnectionServer image..." -ForegroundColor Cyan
    docker build -t sharpmush/sharpmush-connectionserver:dev -f SharpMUSH.ConnectionServer/Dockerfile .
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to build ConnectionServer image" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "Docker images built successfully!" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "Skipping build (using existing images)..." -ForegroundColor Yellow
    Write-Host ""
}

# Check if kubectl is available
Write-Host "Checking Kubernetes availability..." -ForegroundColor Yellow
try {
    kubectl cluster-info | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Kubernetes cluster not available. Is Docker Desktop Kubernetes enabled?" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "kubectl not found. Please ensure kubectl is installed and in PATH." -ForegroundColor Red
    exit 1
}

# Delete existing deployment if it exists
Write-Host "Cleaning up existing deployment..." -ForegroundColor Yellow
kubectl delete -f kubernetes/dev-k8s.yaml --ignore-not-found=true
Start-Sleep -Seconds 2

# Deploy to Kubernetes
Write-Host "Deploying to Kubernetes..." -ForegroundColor Yellow
kubectl apply -f kubernetes/dev-k8s.yaml
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to deploy to Kubernetes" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Deployment submitted successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Checking deployment status..." -ForegroundColor Yellow
Write-Host ""

# Wait for deployments to be ready
Write-Host "Waiting for pods to be ready (this may take a few minutes)..." -ForegroundColor Cyan
Write-Host ""

# Monitor pod status
$timeout = 300 # 5 minutes
$elapsed = 0
$interval = 5

while ($elapsed -lt $timeout) {
    $pods = kubectl get pods -o json | ConvertFrom-Json
    $allReady = $true
    
    foreach ($pod in $pods.items) {
        $ready = $false
        if ($pod.status.conditions) {
            foreach ($condition in $pod.status.conditions) {
                if ($condition.type -eq "Ready" -and $condition.status -eq "True") {
                    $ready = $true
                    break
                }
            }
        }
        if (-not $ready) {
            $allReady = $false
        }
    }
    
    if ($allReady -and $pods.items.Count -gt 0) {
        Write-Host ""
        Write-Host "All pods are ready!" -ForegroundColor Green
        break
    }
    
    Start-Sleep -Seconds $interval
    $elapsed += $interval
    Write-Host "." -NoNewline
}

Write-Host ""
Write-Host ""
Write-Host "Current deployment status:" -ForegroundColor Cyan
kubectl get all

Write-Host ""
Write-Host "To connect to the ConnectionServer:" -ForegroundColor Cyan
Write-Host "  telnet localhost 4201" -ForegroundColor White
Write-Host ""
Write-Host "To view logs:" -ForegroundColor Cyan
Write-Host "  kubectl logs -f deployment/sharpmush" -ForegroundColor White
Write-Host "  kubectl logs -f deployment/connectionserver" -ForegroundColor White
Write-Host ""
Write-Host "To delete the deployment:" -ForegroundColor Cyan
Write-Host "  ./kubernetes/deploy-local.ps1 -Delete" -ForegroundColor White
Write-Host ""
