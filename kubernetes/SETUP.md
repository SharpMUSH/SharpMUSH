# Setting Up Kubernetes for SharpMUSH Development

## Enable Kubernetes in Docker Desktop

1. **Open Docker Desktop**
   - Right-click the Docker icon in your system tray
   - Click "Settings" or "Preferences"

2. **Enable Kubernetes**
   - Navigate to the "Kubernetes" tab on the left
   - Check the box "Enable Kubernetes"
   - Click "Apply & Restart"
   - Wait for the Kubernetes status indicator to show green (may take 2-5 minutes)

3. **Verify Kubernetes is Running**
   ```powershell
   kubectl cluster-info
   ```
   
   You should see output like:
   ```
   Kubernetes control plane is running at https://kubernetes.docker.internal:6443
   CoreDNS is running at https://kubernetes.docker.internal:6443/api/v1/namespaces/kube-system/services/kube-dns:dns/proxy
   ```

## Deploy SharpMUSH

Once Kubernetes is enabled, run:

```powershell
# Navigate to the project root
cd d:\SharpMUSH

# Run the deployment script
./kubernetes/deploy-local.ps1
```

## What Gets Deployed

The script will:

1. ✅ Build Docker images for:
   - SharpMUSH Server
   - ConnectionServer

2. ✅ Deploy to Kubernetes:
   - ArangoDB (database)
   - RedPanda (message broker)
   - SharpMUSH Server
   - ConnectionServer

3. ✅ Create services and persistent volumes

## Accessing the Deployment

**ConnectionServer (Telnet):**
```bash
telnet localhost 4201
```

**View Logs:**
```powershell
# SharpMUSH Server logs
kubectl logs -f deployment/sharpmush

# ConnectionServer logs
kubectl logs -f deployment/connectionserver

# ArangoDB logs
kubectl logs -f deployment/arangodb

# RedPanda logs
kubectl logs -f deployment/redpanda
```

**Check Status:**
```powershell
# All resources
kubectl get all

# Pods only
kubectl get pods

# Services
kubectl get services

# Persistent volumes
kubectl get pvc
```

## Troubleshooting

### Kubernetes Not Starting

If Docker Desktop shows "Kubernetes is starting..." for more than 5 minutes:

1. Disable Kubernetes in Docker Desktop settings
2. Restart Docker Desktop
3. Re-enable Kubernetes
4. Wait for it to fully start (green indicator)

### Reset Kubernetes

If you need to completely reset:

1. Docker Desktop > Settings > Kubernetes
2. Click "Reset Kubernetes Cluster"
3. Confirm the reset
4. Wait for Kubernetes to restart

### Check Docker Desktop Resources

Ensure adequate resources are allocated:

1. Docker Desktop > Settings > Resources
2. Recommended minimums:
   - CPUs: 4
   - Memory: 8 GB
   - Swap: 1 GB
   - Disk: 50 GB

### Pods Stuck in Pending

```powershell
# Check what's wrong
kubectl describe pod <pod-name>

# Common issues:
# - Insufficient resources
# - Image pull errors
# - Volume mount issues
```

### Clean Up and Retry

```powershell
# Delete everything
./kubernetes/deploy-local.ps1 -Delete

# Wait a moment
Start-Sleep -Seconds 5

# Redeploy
./kubernetes/deploy-local.ps1
```

## Next Steps

After successful deployment:

1. Connect via telnet to port 4201
2. Monitor logs for any errors
3. Test basic MUSH commands
4. Check that all services are communicating properly

## Useful Commands

```powershell
# Interactive shell in a pod
kubectl exec -it deployment/sharpmush -- /bin/bash

# Port forward a service
kubectl port-forward service/arangodb 8529:8529

# Get pod resource usage
kubectl top pods

# Watch pod status in real-time
kubectl get pods -w

# Delete specific resources
kubectl delete deployment sharpmush
kubectl delete service connectionserver
kubectl delete pvc arangodb-pvc

# Delete everything from the manifest
kubectl delete -f kubernetes/dev-k8s.yaml
```
