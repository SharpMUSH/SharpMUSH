#!/usr/bin/env pwsh
# Script to deploy SharpMUSH to the sharpmush-cluster Kubernetes cluster

param(
    [switch]$SkipBuild,
    [switch]$Delete,
    [string]$ClusterContext = "gke_gen-lang-client-0487922196_us-central1_sharpmush-cluster"
)

$ErrorActionPreference = "Stop"

Write-Host "SharpMUSH Cluster Deployment Script" -ForegroundColor Cyan
Write-Host "Target Cluster: $ClusterContext" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host ""

# Navigate to the root directory
$rootDir = Split-Path -Parent $PSScriptRoot
Set-Location $rootDir

# Check if kubectl is available
if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    Write-Host "kubectl not found. Please ensure kubectl is installed and in PATH." -ForegroundColor Red
    exit 1
}

# Switch Kubernetes Context
Write-Host "Switching context to '$ClusterContext'..." -ForegroundColor Yellow
try {
    kubectl config use-context $ClusterContext | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to switch context."
    }
    Write-Host "Successfully switched to context '$ClusterContext'." -ForegroundColor Green
} catch {
    Write-Host "Error: Failed to switch to context '$ClusterContext'. Please ensure it exists in your kubeconfig." -ForegroundColor Red
    Write-Host "Available contexts:"
    kubectl config get-contexts -o name
    exit 1
}

# Verify Cluster Connectivity
Write-Host "Verifying cluster connectivity..." -ForegroundColor Yellow
try {
    kubectl cluster-info | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Cluster not reachable."
    }
} catch {
    Write-Host "Error: Unable to connect to the cluster '$ClusterContext'." -ForegroundColor Red
    exit 1
}

if ($Delete) {
    Write-Host "Deleting existing deployment..." -ForegroundColor Yellow
    kubectl delete -f kubernetes/dev-k8s.yaml --ignore-not-found=true
    Write-Host "Deployment deleted successfully!" -ForegroundColor Green
    exit 0
}

if (-not $SkipBuild) {
    Write-Host "Building Docker images..." -ForegroundColor Yellow
    Write-Host "Note: If this is a remote cluster, ensure you push these images to a registry accessible by the cluster." -ForegroundColor Magenta
    
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
    $notReady = $pods.items | Where-Object { 
        $_.status.phase -ne "Running" -and $_.status.phase -ne "Succeeded" 
    }
    
    if (-not $notReady -and $pods.items.Count -gt 0) {
        Write-Host "`nAll pods are running!" -ForegroundColor Green
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
Write-Host "Deployment to $ClusterContext complete." -ForegroundColor Green
Write-Host ""