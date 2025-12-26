# Redis Shared State Store Implementation

## Overview

SharpMUSH now uses Redis as a shared state store for connection management, solving the connection desync problem that occurred when the Server process restarted.

## Problem Solved

**Before**: 
- ConnectionServer and Server maintained independent in-memory connection lists
- When Server restarted, it lost all connection state
- Existing user connections were "orphaned" - they appeared connected but Server couldn't handle commands

**After**:
- Both processes share connection state via Redis
- Server can rebuild its state from Redis on startup
- Existing connections survive Server restarts

## Architecture

```
┌──────────┐         ┌───────┐         ┌────────┐
│  Client  │────────▶│Connect│────────▶│ Server │
└──────────┘         │Server │         └────────┘
                     └───┬───┘              │
                         │                  │
                         └──────Redis───────┘
                              (Shared State)
```

Both processes read/write connection state to Redis, making it the single source of truth.

## Components

### 1. IConnectionStateStore Interface
Location: `SharpMUSH.Library/Services/Interfaces/IConnectionStateStore.cs`

Provides abstraction for connection state operations:
- `SetConnectionAsync`: Store connection metadata
- `GetConnectionAsync`: Retrieve connection data
- `RemoveConnectionAsync`: Delete connection
- `GetAllConnectionsAsync`: List all active connections (for reconciliation)
- `SetPlayerBindingAsync`: Update player login state
- `UpdateMetadataAsync`: Update connection metadata

### 2. RedisConnectionStateStore
Location: `SharpMUSH.Library/Services/RedisConnectionStateStore.cs`

Redis implementation using StackExchange.Redis:
- Stores connections as JSON with 24-hour TTL
- Uses Redis SET for active connection handles
- Automatic cleanup of expired connections
- Error handling with logging

**Redis Data Structure**:
```
Key Pattern: sharpmush:conn:{handle}
Value: JSON-serialized ConnectionStateData
TTL: 24 hours

Set: sharpmush:conn:active
Members: [handle1, handle2, ...]
```

### 3. ConnectionReconciliationService
Location: `SharpMUSH.Server/Services/ConnectionReconciliationService.cs`

Hosted service that reconciles state on Server startup:
- Queries Redis for all active connections
- Rebuilds in-memory connection list
- Restores player bindings
- Creates Kafka output functions for each connection
- Non-fatal errors (allows startup even if Redis unavailable)

## Configuration

### Docker Compose
Redis container added to `docker-compose.yml`:
```yaml
redis:
  image: redis:7-alpine
  ports:
    - "6379:6379"
  volumes:
    - redis-data:/data
  command: redis-server --appendonly yes
```

Both ConnectionServer and Server have:
- `REDIS_CONNECTION=redis:6379` environment variable
- Dependency on Redis with health check

### Connection String
Environment variable: `REDIS_CONNECTION`
- Default: `localhost:6379`
- Docker: `redis:6379`
- Production: Can use Redis Cluster, Sentinel, etc.

## Testing

### 1. Start All Services
```bash
docker-compose up -d
```

### 2. Verify Redis Connection
```bash
# Check Redis is running
docker exec sharpmush-redis redis-cli ping
# Should return: PONG

# View active connections
docker exec sharpmush-redis redis-cli SMEMBERS sharpmush:conn:active

# View specific connection data
docker exec sharpmush-redis redis-cli GET sharpmush:conn:1
```

### 3. Test Connection Persistence

**Test Scenario: Server Restart**
```bash
# 1. Connect a telnet client
telnet localhost 4201

# 2. Login as a player (if you have accounts set up)
connect wizard password

# 3. Check connection is in Redis
docker exec sharpmush-redis redis-cli SMEMBERS sharpmush:conn:active

# 4. Restart Server (NOT ConnectionServer)
docker restart sharpmush-server

# 5. Watch logs for reconciliation
docker logs -f sharpmush-server
# Should see: "Starting connection state reconciliation from Redis..."
# Should see: "Connection state reconciliation completed successfully"

# 6. In telnet client, try a command
look
# Should work! Connection state was restored.
```

**Test Scenario: Connection TTL**
```bash
# View TTL of a connection
docker exec sharpmush-redis redis-cli TTL sharpmush:conn:1
# Should return seconds remaining (up to 86400 for 24 hours)

# After disconnect, verify cleanup
# (Disconnect telnet client)
docker exec sharpmush-redis redis-cli SMEMBERS sharpmush:conn:active
# Connection should be removed
```

### 4. Monitor Redis
```bash
# View all connection keys
docker exec sharpmush-redis redis-cli KEYS "sharpmush:conn:*"

# Monitor Redis commands in real-time
docker exec sharpmush-redis redis-cli MONITOR

# Check Redis memory usage
docker exec sharpmush-redis redis-cli INFO memory
```

## Data Flow

### Connection Established
1. Client connects to ConnectionServer on port 4201
2. ConnectionServer:
   - Creates in-memory connection entry
   - Stores to Redis: `SET sharpmush:conn:{handle} {json} EX 86400`
   - Adds to set: `SADD sharpmush:conn:active {handle}`
   - Publishes `ConnectionEstablishedMessage` to Kafka
3. Server:
   - Receives message from Kafka
   - Registers connection in memory
   - (Already in Redis from ConnectionServer)

### Player Login
1. User sends login command
2. Server processes authentication
3. Server:
   - Updates in-memory connection with player DBRef
   - Updates Redis: `SET sharpmush:conn:{handle} {json_with_player} EX 86400`
   - Connection state now shows "LoggedIn"

### Server Restart
1. Server shuts down (in-memory state lost)
2. ConnectionServer continues running (connections still active)
3. Server starts up:
   - ConnectionReconciliationService runs
   - Queries Redis: `SMEMBERS sharpmush:conn:active`
   - For each handle: `GET sharpmush:conn:{handle}`
   - Rebuilds in-memory state with Kafka output functions
   - Restores player bindings
4. Connections work immediately (no user reconnect needed)

### Connection Closed
1. Client disconnects (or Server sends disconnect command)
2. ConnectionServer:
   - Removes from Redis: `DEL sharpmush:conn:{handle}`
   - Removes from set: `SREM sharpmush:conn:active {handle}`
   - Publishes `ConnectionClosedMessage` to Kafka
3. Server:
   - Receives message from Kafka
   - Removes from in-memory state

## Performance Considerations

### In-Memory Cache
Both processes maintain in-memory caches for performance:
- Most operations (sending output, checking state) use memory
- Redis is consulted for:
  - Initial registration
  - Player binding updates
  - Reconciliation on startup

### Redis Operations
- `GET`: O(1) - fast
- `SET`: O(1) - fast
- `SMEMBERS`: O(N) where N = number of connections - acceptable for reconciliation
- `SADD/SREM`: O(1) - fast

For typical MUSH with <1000 concurrent connections, Redis performance is excellent.

### Memory Usage
Each connection stores:
```json
{
  "Handle": 1,
  "PlayerRef": "P#123",
  "State": "LoggedIn",
  "IpAddress": "192.168.1.1",
  "Hostname": "192.168.1.1:12345",
  "ConnectionType": "telnet",
  "ConnectedAt": "2025-01-01T00:00:00Z",
  "LastSeen": "2025-01-01T00:05:00Z",
  "Metadata": {...}
}
```
~500 bytes per connection, so 1000 connections = ~500KB in Redis.

## High Availability

For production deployments, consider:

### Redis Cluster
```yaml
redis:
  image: redis:7-alpine
  command: redis-server --cluster-enabled yes --cluster-config-file nodes.conf
```

### Redis Sentinel
Automatic failover for master/replica setup.

### Connection String
Update `REDIS_CONNECTION` to include multiple endpoints:
```
REDIS_CONNECTION=redis1:6379,redis2:6379,redis3:6379
```

## Troubleshooting

### Redis Connection Failed
If Redis is unavailable:
- ConnectionServer: Logs warning, continues without state persistence
- Server: Logs warning during reconciliation, starts without restored state
- Connections still work but won't survive Server restart

Check logs:
```bash
docker logs sharpmush-server | grep -i redis
docker logs sharpmush-connectionserver | grep -i redis
```

### Stale Connections
If connections remain in Redis after disconnect:
- TTL of 24 hours will auto-expire them
- Or manually clean up:
```bash
docker exec sharpmush-redis redis-cli DEL sharpmush:conn:{handle}
docker exec sharpmush-redis redis-cli SREM sharpmush:conn:active {handle}
```

### Reconciliation Failures
If ConnectionReconciliationService fails:
- Check Redis is accessible from Server container
- Verify `REDIS_CONNECTION` environment variable
- Check network connectivity: `docker exec sharpmush-server ping redis`

## Future Enhancements

Potential improvements:
1. **Redis Pub/Sub**: Real-time connection state updates between processes
2. **Compression**: Compress JSON for larger connection metadata
3. **Partitioning**: Shard connections across multiple Redis instances
4. **Metrics**: Track Redis operation latency and errors
5. **Connection Migration**: Active connection transfer between ConnectionServer instances

## References

This implementation follows patterns from:
- **SignalR with Redis Backplane**: [Microsoft Docs](https://docs.microsoft.com/aspnet/signalr/overview/performance/scaleout-with-redis)
- **Socket.IO Redis Adapter**: [Socket.IO Docs](https://socket.io/docs/v4/redis-adapter/)
- **Distributed Cache Pattern**: [Microsoft Docs](https://docs.microsoft.com/azure/architecture/patterns/cache-aside)

## Summary

The Redis shared state store implementation:
- ✅ Solves connection desync on Server restart
- ✅ Uses production-ready patterns (SignalR, Socket.IO)
- ✅ Maintains in-memory performance with Redis persistence
- ✅ Automatic cleanup via TTL
- ✅ Graceful degradation if Redis unavailable
- ✅ Ready for horizontal scaling

Existing connections now survive Server restarts, providing a seamless experience for users.
