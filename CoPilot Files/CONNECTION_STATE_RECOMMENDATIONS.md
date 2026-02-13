# Connection State Sharing - Action Items

## Quick Reference

**TL;DR:** Current architecture is **sound and follows industry best practices**. No major changes needed. Focus on incremental improvements to observability and performance.

---

## Priority 1: Add Telemetry for Background Update Failures ⭐⭐⭐

**Effort:** LOW (1-2 hours)  
**Impact:** HIGH (visibility into production issues)  
**Risk:** NONE (logging only)

### Problem
```csharp
// Current code swallows exceptions silently
catch
{
    // Ignore errors in background update ❌
}
```

### Solution
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to update metadata for handle {Handle}: {Key}={Value}", 
        handle, key, value);
    telemetryService?.RecordConnectionMetadataUpdate("failure", key);
}
```

### Files to Modify
- `SharpMUSH.Library/Services/ConnectionService.cs` (line 105-116)

### Test Plan
1. Simulate Redis failure
2. Verify warnings appear in logs
3. Verify Prometheus metrics increment
4. No functional changes - same behavior

---

## Priority 2: Optimize Metadata Updates with Redis Hashes ⭐⭐

**Effort:** MEDIUM (4-8 hours)  
**Impact:** MEDIUM-HIGH (performance improvement)  
**Risk:** MEDIUM (requires schema migration)

### Problem
Current implementation serializes entire ConnectionStateData JSON for every metadata update:
- ~500 bytes per update
- Full read-modify-write cycle
- Optimistic locking with 10 retries
- High contention risk

### Solution
Use Redis Hash data structure for metadata:

```csharp
// Instead of:
// GET sharpmush:conn:1 → deserialize → modify → serialize → SET

// Use:
// HSET sharpmush:conn:1:metadata LastConnectionSignal "1234567890"
```

### Benefits
- 10x faster (no serialization)
- 10x less network traffic
- No race conditions (atomic operation)
- No retry logic needed

### Implementation Plan
1. Create migration script to convert existing keys
2. Update `RedisConnectionStateStore.UpdateMetadataAsync()`
3. Update `GetConnectionAsync()` to read from both structures
4. Add compatibility layer for gradual rollout
5. Monitor performance improvements

### Files to Modify
- `SharpMUSH.Library/Services/RedisConnectionStateStore.cs`
- Add migration script in `SharpMUSH.Server/Migrations/`

### Test Plan
1. Unit tests for new Hash operations
2. Integration test: update metadata during high load
3. Measure latency before/after (should improve 5-10x)
4. Verify backward compatibility

---

## Priority 3: Differentiate Critical vs Non-Critical State ⭐

**Effort:** LOW-MEDIUM (2-4 hours)  
**Impact:** MEDIUM (consistency improvement)  
**Risk:** LOW (additive change)

### Problem
All state updates use same consistency model (fire-and-forget)

### Solution
Different consistency models for different metadata:

**Critical State** (synchronous writes):
- PlayerRef (player binding)
- State (Connected → LoggedIn)

**Non-Critical State** (async writes):
- LastConnectionSignal (idle time)
- Custom metadata

### Implementation
```csharp
public async ValueTask Bind(long handle, DBRef player)
{
    // ... update in-memory state ...
    
    // CRITICAL: Synchronous Redis update
    if (stateStore != null)
    {
        await stateStore.SetPlayerBindingAsync(handle, player);
    }
    
    // ... rest of method ...
}

public void Update(long handle, string key, string value)
{
    // ... update in-memory state ...
    
    // NON-CRITICAL: Async Redis update
    if (stateStore != null && IsNonCriticalMetadata(key))
    {
        _ = Task.Run(async () => { /* background update */ });
    }
    else if (stateStore != null)
    {
        // CRITICAL: Synchronous update
        await stateStore.UpdateMetadataAsync(handle, key, value);
    }
}
```

### Files to Modify
- `SharpMUSH.Library/Services/ConnectionService.cs`
- Add metadata classification enum

### Test Plan
1. Unit test: verify critical metadata updates block
2. Unit test: verify non-critical metadata updates don't block
3. Integration test: player login should wait for Redis

---

## Priority 4: Add Redis Health Monitoring ⭐

**Effort:** LOW (1-2 hours)  
**Impact:** LOW-MEDIUM (operational improvement)  
**Risk:** NONE (monitoring only)

### Implementation
```csharp
public class RedisHealthService : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var latency = await db.PingAsync();
            
            if (latency.TotalMilliseconds > 100)
            {
                return HealthCheckResult.Degraded(
                    $"Redis latency high: {latency.TotalMilliseconds}ms");
            }
            
            return HealthCheckResult.Healthy(
                $"Redis responsive ({latency.TotalMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis connection failed", ex);
        }
    }
}
```

### Files to Create/Modify
- Create `SharpMUSH.Library/Services/RedisHealthService.cs`
- Modify `SharpMUSH.Server/Startup.cs` to register health check
- Modify `SharpMUSH.ConnectionServer/Program.cs` to register health check

### Test Plan
1. Verify `/health` endpoint shows Redis status
2. Stop Redis → verify health check fails
3. Start Redis → verify health check recovers

---

## Priority 5: Document Authority Model ⭐⭐⭐

**Effort:** IMMEDIATE (30 minutes)  
**Impact:** HIGH (developer clarity)  
**Risk:** NONE (documentation only)

### Add to Code Documentation

**File:** `SharpMUSH.Library/Services/Interfaces/IConnectionStateStore.cs`

```csharp
/// <summary>
/// Interface for storing and retrieving connection state across processes.
/// 
/// AUTHORITY MODEL:
/// - ConnectionServer: Authority on connection status (Connected/Disconnected)
/// - Server: Authority on connection metadata (PlayerRef, LastSeen, custom metadata)
/// - Redis: Shared persistence layer for both processes
/// 
/// CONSISTENCY MODEL:
/// - In-memory state is authoritative for hot-path operations
/// - Redis state is authoritative for reconciliation after restart
/// - Updates use eventual consistency (async background writes)
/// 
/// USAGE PATTERNS:
/// - Register: In-memory + immediate Redis write
/// - Get: In-memory only (no Redis query)
/// - Update: In-memory + fire-and-forget Redis write
/// - Reconcile: One-time Redis read on Server startup
/// </summary>
public interface IConnectionStateStore { /* ... */ }
```

---

## NOT Recommended

### ❌ Don't: Make It More In-Memory Only
**Reason:** Would lose connection persistence on restart

### ❌ Don't: Make It More Redis-Centric
**Reason:** Would add latency to hot path with no benefit

### ❌ Don't: Implement Strong Consistency Everywhere
**Reason:** Overkill for MUSH workloads, adds complexity

### ❌ Don't: Add Redis Pub/Sub for Real-Time Sync
**Reason:** Only needed for horizontal scaling (multiple Server instances)

---

## Implementation Roadmap

### Phase 1: Observability (Week 1)
- [ ] Priority 5: Document authority model (30 min)
- [ ] Priority 1: Add telemetry for failures (2 hours)
- [ ] Priority 4: Add Redis health monitoring (2 hours)
- [ ] Deploy to staging
- [ ] Monitor for 1 week

### Phase 2: Performance (Week 2-3)
- [ ] Priority 2: Design Hash-based metadata schema
- [ ] Create migration script
- [ ] Update RedisConnectionStateStore
- [ ] Add backward compatibility layer
- [ ] Test in staging with production data snapshot
- [ ] Gradual rollout (canary deployment)

### Phase 3: Consistency (Week 4)
- [ ] Priority 3: Implement critical/non-critical classification
- [ ] Add synchronous updates for player binding
- [ ] Verify no performance regression
- [ ] Deploy to production

---

## Success Metrics

### Observability Goals (Phase 1)
- [ ] Zero silent failures (all errors logged)
- [ ] Prometheus dashboard shows Redis health
- [ ] Alert fires when Redis latency >100ms
- [ ] Clear documentation of authority model

### Performance Goals (Phase 2)
- [ ] Metadata update latency <5ms (down from 10-50ms)
- [ ] Redis network traffic reduced by 80%
- [ ] Zero optimistic locking retries under normal load
- [ ] Connection registration throughput >1000/sec

### Consistency Goals (Phase 3)
- [ ] Player login bindings never lost
- [ ] In-memory and Redis state divergence <1%
- [ ] Connection reconciliation after restart <500ms

---

## Questions for Stakeholders

1. **What is the expected concurrent connection count?**
   - <100: Current pattern is overkill, could simplify
   - 100-1000: Current pattern is perfect
   - >1000: Should implement Priority 2 immediately

2. **How frequently do Server restarts occur?**
   - Rare: Reconciliation complexity may not be worth it
   - Frequent: Critical to maintain (justify current complexity)

3. **Are there plans for horizontal scaling (multiple Server instances)?**
   - No: Can simplify some code
   - Yes: Should plan for Redis Pub/Sub in future

4. **What is acceptable staleness for idle time calculations?**
   - <1 second: Need stronger consistency
   - 1-60 seconds: Current pattern is fine

5. **What is the Redis infrastructure deployment plan?**
   - TestContainers (dev): Current approach works
   - Kubernetes: Should add health checks and monitoring
   - Cloud managed: Can rely on external monitoring

---

## Appendix: Performance Benchmarks

### Current Performance Estimates

**Operation** | **Latency** | **Throughput**
---|---|---
Get Connection (in-memory) | 10-50ns | 20M ops/sec
Register Connection | 5-10ms | 200/sec
Update Metadata (fire-and-forget) | 50ns (async) | 20M ops/sec
Player Login (with Redis) | 10-20ms | 100/sec

### After Priority 2 Implementation

**Operation** | **Current** | **Optimized** | **Improvement**
---|---|---|---
Update Metadata | 10-50ms | 1-5ms | 5-10x faster
Redis Network Traffic | 500 bytes | 50 bytes | 10x less
Optimistic Lock Retries | 0-10 | 0 | 100% success rate

---

**Document Version:** 1.0  
**Date:** 2026-02-13  
**Status:** READY FOR IMPLEMENTATION
