# SharpMUSH Kubernetes Deployment

This directory contains Kubernetes manifests for deploying SharpMUSH.

## Local Development (Docker Desktop)

### Prerequisites

1. **Docker Desktop** with Kubernetes enabled
   - Open Docker Desktop Settings
   - Go to Kubernetes tab
   - Enable Kubernetes
   - Wait for Kubernetes to start

2. **kubectl** command-line tool (included with Docker Desktop)

### Quick Start

Deploy to your local Kubernetes cluster:

```powershell
# Build and deploy
./kubernetes/deploy-local.ps1

# Deploy without rebuilding images
./kubernetes/deploy-local.ps1 -SkipBuild

# Delete the deployment
./kubernetes/deploy-local.ps1 -Delete
```

### Manual Deployment

If you prefer to run commands manually:

```powershell
# Build Docker images
docker build -t sharpmush/sharpmush-server:dev -f Dockerfile .
docker build -t sharpmush/sharpmush-connectionserver:dev -f SharpMUSH.ConnectionServer/Dockerfile .

# Deploy to Kubernetes
kubectl apply -f kubernetes/dev-k8s.yaml

# Check status
kubectl get all

# View logs
kubectl logs -f deployment/sharpmush
kubectl logs -f deployment/connectionserver

# Delete deployment
kubectl delete -f kubernetes/dev-k8s.yaml
```

### Connecting

Once deployed, the ConnectionServer is available on port 4201:

```bash
telnet localhost 4201
```

### Troubleshooting

**Pods not starting:**
```powershell
# Check pod status
kubectl get pods

# View pod details
kubectl describe pod <pod-name>

# View logs
kubectl logs <pod-name>
```

**Images not found:**
- Ensure `imagePullPolicy: Never` is set in dev-k8s.yaml
- Verify images are built locally: `docker images | Select-String sharpmush`

**Services not accessible:**
```powershell
# Check services
kubectl get services

# Port forward if needed
kubectl port-forward service/connectionserver 4201:4201
```

**Clean slate:**
```powershell
# Delete everything and redeploy
kubectl delete -f kubernetes/dev-k8s.yaml
./kubernetes/deploy-local.ps1
```

## Files

- **dev-k8s.yaml**: Development Kubernetes manifest for local deployment
- **deploy-local.ps1**: PowerShell script to build and deploy to local Kubernetes
- **README.md**: This file

## Architecture

The deployment includes:

- **ArangoDB**: Database (port 8529)
- **RedPanda**: Kafka-compatible message broker (port 9092)
- **SharpMUSH Server**: Main game server
- **ConnectionServer**: Handles player connections (port 4201)

## Notes

- All services use `ClusterIP` by default except ConnectionServer which uses `LoadBalancer`
- CPU limits are set to 500m (0.5 cores) per container for local development
- Persistent volumes are used for ArangoDB and RedPanda data
- Images use `imagePullPolicy: Never` to use locally built images
