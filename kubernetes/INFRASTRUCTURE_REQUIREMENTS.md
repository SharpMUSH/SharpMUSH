# SharpMUSH Infrastructure Requirements

Based on TestContainer usage and strategy patterns in the codebase, SharpMUSH requires the following infrastructure services to run.

## Required Services

### 1. ArangoDB (Database)
**Purpose:** Primary database for storing all MUSH game data (objects, attributes, etc.)

**Configuration:**
- Environment Variable: `ARANGO_CONNECTION_STRING`
- Default Port: 8529
- Example: `Server=http://arangodb:8529;User=root;Password=password;UseUnixSocket=false`

**Strategy Files:**
- `ArangoStartupStrategyProvider.cs` - Selects appropriate strategy
- `ArangoKubernetesStartupStrategy.cs` - Production/Kubernetes deployment
- `ArangoSocketStartupStrategy.cs` - Docker Compose with Unix socket
- `ArangoTestContainerStartupStrategy.cs` - Local dev fallback (uses Docker to spawn container)

**Strategy Selection Priority:**
1. If connection string contains `.sock` → Use Unix socket strategy
2. If `ARANGO_CONNECTION_STRING` is set → Use Kubernetes/HTTP strategy
3. If `ARANGO_TEST_CONNECTION_STRING` is set → Use Kubernetes/HTTP strategy
4. Otherwise → Spawn test container (requires Docker)

---

### 2. RedPanda/Kafka (Message Broker)
**Purpose:** Message queue for inter-service communication between SharpMUSH Server and ConnectionServer

**Configuration:**
- Environment Variables: `KAFKA_HOST` and `KAFKA_PORT`
- Default Port: 9092
- Test Variables: `KAFKA_TEST_HOST` and `KAFKA_TEST_PORT` (for testing)
- Max Message Size: 6MB (`kafka_batch_max_bytes=6291456`)

**Strategy Files:**
- `MessageQueueStrategyProvider.cs` - Selects appropriate strategy
- `RedPandaContainerStrategy.cs` - Uses external RedPanda instance
- `RedPandaTestContainerStrategy.cs` - Local dev fallback (uses Docker to spawn container)

**Strategy Selection Priority:**
1. If `KAFKA_HOST` and `KAFKA_PORT` are set → Use external RedPanda
2. If `KAFKA_TEST_HOST` and `KAFKA_TEST_PORT` are set → Use external RedPanda
3. Otherwise → Spawn test container (requires Docker)

**Used By:**
- SharpMUSH Server
- ConnectionServer

---

### 3. Redis (State Store)
**Purpose:** Shared connection state storage between ConnectionServer instances

**Configuration:**
- Environment Variable: `REDIS_CONNECTION`
- Default Port: 6379
- Test Variable: `REDIS_TEST_CONNECTION_STRING` (for testing)
- Example: `redis:6379`

**Strategy Files:**
- `RedisStrategyProvider.cs` - Selects appropriate strategy (in both Server and ConnectionServer)
- `RedisExternalStrategy.cs` - Uses external Redis instance
- `RedisTestContainerStrategy.cs` - Local dev fallback (uses Docker to spawn container)

**Strategy Selection Priority:**
1. If `REDIS_TEST_CONNECTION_STRING` is set → Use external Redis (for tests)
2. If `REDIS_CONNECTION` is set → Use external Redis (production)
3. Otherwise → Spawn test container (requires Docker)

**Used By:**
- SharpMUSH Server
- ConnectionServer

---

### 4. Prometheus (Metrics/Monitoring)
**Purpose:** Metrics collection and monitoring

**Configuration:**
- Environment Variable: `PROMETHEUS_URL`
- Default Port: 9090
- Test Variable: `PROMETHEUS_TEST_URL` (for testing)
- Example: `http://localhost:9090`

**Strategy Files:**
- `PrometheusStrategyProvider.cs` - Selects appropriate strategy
- `PrometheusExternalStrategy.cs` - Uses external Prometheus instance
- `PrometheusTestContainerStrategy.cs` - Local dev fallback (uses Docker to spawn container)

**Strategy Selection Priority:**
1. If `PROMETHEUS_URL` is set → Use external Prometheus
2. If `PROMETHEUS_TEST_URL` is set → Use external Prometheus (for tests)
3. Otherwise → Spawn test container (requires Docker)

**Used By:**
- SharpMUSH Server
- ConnectionServer (exports metrics on port 9091)

**Metrics Endpoints:**
- SharpMUSH Server: port 9092
- ConnectionServer: port 9091

---

## Environment Variables Summary

### SharpMUSH Server

**Required:**
- `ARANGO_CONNECTION_STRING` - ArangoDB connection string
- `KAFKA_HOST` - RedPanda/Kafka hostname
- `KAFKA_PORT` - RedPanda/Kafka port

**Recommended:**
- `PROMETHEUS_URL` - Prometheus server URL (prevents test container spawn)
- `REDIS_CONNECTION` - Redis connection string (prevents test container spawn)

**Optional:**
- `ARANGO_TEST_CONNECTION_STRING` - For test mode (creates unique database)
- `SHARPMUSH_FAST_MIGRATION` - Enables test mode (disables Prometheus metrics export)

### ConnectionServer

**Required:**
- `KAFKA_HOST` - RedPanda/Kafka hostname (if not set, spawns test container)

**Recommended:**
- `REDIS_CONNECTION` - Redis connection string (prevents test container spawn)
- `PROMETHEUS_URL` - Prometheus server URL (prevents test container spawn)

**Optional:**
- `KAFKA_PORT` - RedPanda/Kafka port (defaults to 9092)
- `SHARPMUSH_FAST_MIGRATION` - Enables test mode (disables Prometheus metrics export)

---

## Deployment Configurations

### Minimal Kubernetes (Current dev-k8s.yaml)
```yaml
Services:
- ArangoDB (required)
- RedPanda (required)
- SharpMUSH Server
- ConnectionServer

Optional but recommended:
- Redis (prevents test container spawning)
- Prometheus (prevents test container spawning)
```

### Full Production Stack
```yaml
Services:
- ArangoDB (required)
- RedPanda (required)
- Redis (required for multi-instance ConnectionServer)
- Prometheus (required for monitoring)
- Grafana (optional, for visualization)
- SharpMUSH Server
- ConnectionServer (can scale horizontally with Redis)
```

---

## Test Container Fallback Behavior

If environment variables are not set, the application will attempt to spawn Docker containers using Testcontainers:

1. **ArangoDB:** Spawns `arangodb:latest` container
2. **RedPanda:** Spawns `docker.redpanda.com/redpandadata/redpanda:latest` container
3. **Redis:** Spawns `redis:7-alpine` container
4. **Prometheus:** Spawns `prom/prometheus:latest` container

**Important:** This requires:
- Docker daemon to be accessible (won't work inside Kubernetes pods)
- Permission to spawn containers
- Not recommended for production

---

## Port Mapping

| Service | Port | Purpose |
|---------|------|---------|
| ArangoDB | 8529 | Database API |
| RedPanda | 9092 | Kafka API |
| RedPanda | 9644 | Admin API |
| RedPanda | 8082 | HTTP Proxy |
| Redis | 6379 | Key-Value Store |
| Prometheus | 9090 | Metrics Server/UI |
| ConnectionServer | 4201 | Telnet Port |
| ConnectionServer | 4202 | HTTP/WebSocket API |
| ConnectionServer | 9091 | Prometheus Metrics Export |
| SharpMUSH Server | 9092 | Prometheus Metrics Export |
| Grafana | 3000 | Dashboard UI (if deployed) |

---

## Connection Flow

```
Player (Telnet/WebSocket)
    ↓
ConnectionServer (4201/4202)
    ↓
RedPanda/Kafka (Message Queue)
    ↓
SharpMUSH Server
    ↓
ArangoDB (Database)

ConnectionServer ←→ Redis (Shared State)
Both Services → Prometheus (Metrics)
```

---

## Recommendations for Kubernetes

1. **Always set environment variables** to avoid test container spawning attempts
2. **Deploy Redis** for proper ConnectionServer state management
3. **Deploy Prometheus** for monitoring and metrics
4. **Use persistent volumes** for ArangoDB and RedPanda data
5. **Configure resource limits** appropriately
6. **Use health/readiness endpoints** for pod lifecycle management:
   - `/health` - Health check
   - `/ready` - Readiness check

---

## Current dev-k8s.yaml Status

✅ **Configured:**
- ArangoDB
- RedPanda
- SharpMUSH Server
- ConnectionServer

⚠️ **Missing but recommended:**
- Redis (using placeholder connection string - should deploy actual Redis)
- Prometheus (using placeholder URL - should deploy actual Prometheus)
- Grafana (optional, for metrics visualization)

These missing services are set to placeholder values which prevents test container spawning but means their features are not fully functional.
