# Docker Container Cleanup for TestContainers with Reuse

## Background

With `WithReuse(true)` enabled, TestContainers marks containers for reuse across test sessions. These containers are NOT automatically cleaned up after tests complete - they persist to be reused in future test runs.

## Manual Cleanup Required

### Why Manual Cleanup?

TestContainers with reuse keeps containers running to speed up subsequent test runs. However, this means:
1. Containers continue running after tests finish
2. They consume system resources (CPU, memory, ports)
3. They must be manually stopped and removed when no longer needed

### How to Clean Up Reused Containers

#### Option 1: Use the Cleanup Script (Recommended)
```bash
./cleanup-test-containers.sh
```

#### Option 2: Clean All SharpMUSH Test Containers
```bash
docker ps -a --filter "label=testcontainers.reuse.hash" | grep sharpmush | awk '{print $1}' | xargs -r docker rm -f
```

#### Option 3: Clean Specific Container Types
```bash
# ArangoDB
docker rm -f $(docker ps -a --filter "label=testcontainers.reuse.hash=sharpmush-arango-test" -q)

# MySQL
docker rm -f $(docker ps -a --filter "label=testcontainers.reuse.hash=sharpmush-mysql-test" -q)

# Prometheus
docker rm -f $(docker ps -a --filter "label=testcontainers.reuse.hash=sharpmush-prometheus-test" -q)

# Redis
docker rm -f $(docker ps -a --filter "label=testcontainers.reuse.hash=sharpmush-redis-test" -q)

# RedPanda
docker rm -f $(docker ps -a --filter "label=testcontainers.reuse.hash=sharpmush-redpanda-test" -q)
```

#### Option 4: Clean All TestContainers (Any Project)
```bash
docker ps -a --filter "label=org.testcontainers" -q | xargs -r docker rm -f
```

### When to Clean Up

- **After development session**: Clean up to free resources
- **Before running tests**: Optional, if you want fresh containers
- **When switching branches**: If database schema changes
- **When debugging container issues**: Start with clean slate

### Verify Cleanup

Check running containers:
```bash
docker ps --filter "label=testcontainers.reuse.hash"
```

Should return empty if all cleaned up.

### Alternative: Disable Reuse (Not Recommended)

If manual cleanup is too cumbersome, you can revert to `WithReuse(false)` in the test server classes, but this will:
- Create new containers for every test class (slower)
- Use more resources during test execution
- Create container sprawl during parallel test runs

## Recommended Workflow

1. **Run tests**: Containers created and reused across test classes
2. **Verify tests pass**: All working correctly
3. **Clean up when done**: Run `./cleanup-test-containers.sh`
4. **Next test session**: Containers will be recreated and reused again

This balances performance (reuse during test session) with resource management (manual cleanup after).

## CI/CD Integration

In CI/CD environments, add cleanup as a post-test step:
```yaml
- name: Run Tests
  run: dotnet test

- name: Cleanup Test Containers
  if: always()
  run: ./cleanup-test-containers.sh
```

This ensures containers don't accumulate across CI runs.
