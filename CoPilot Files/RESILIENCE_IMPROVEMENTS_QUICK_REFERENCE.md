# Resilience Improvements - Quick Reference

## Overview

This document provides a quick reference for the resilience improvements designed for SharpMUSH's connection management system.

**Full Design Document:** `RESILIENCE_IMPROVEMENTS_DESIGN.md` (1,340 lines)

---

## Four Core Improvements

### 1. Graceful Connection Draining ⏱️

**What:** Clean shutdown with user notification instead of abrupt disconnection

**How it works:**
```
Shutdown Signal → Send warning → 30s grace period → Force disconnect
                  ↓
                  Countdown messages every 10s
```

**Key benefit:** Users have time to save work, no data loss

**Configuration:**
```json
{ "ConnectionDraining": { "GracePeriodSeconds": 30 } }
```

---

### 2. Duplicate Connection Detection 🔍

**What:** Detect and handle same player connecting multiple times

**Three Policies:**
- **Allow**: Both connections stay active (default, backwards compatible)
- **RejectNew**: Keep existing, reject new login attempt
- **DisconnectPrevious**: Disconnect old, allow new (most recent wins)

**Key benefit:** Prevents account confusion and security issues

**Configuration:**
```json
{ "DuplicateConnection": { "Policy": "DisconnectPrevious" } }
```

---

### 3. Connection Backpressure Management 🚦

**What:** Protect system from connection floods via rate limiting

**Four Levels:**
```
Normal    (< 80%): No limits
Elevated  (80-90%): Rate limit to 1 conn/sec per IP
High      (90-95%): Rate limit to 1 conn/5sec per IP
Critical  (> 95%): Reject all new connections
```

**Key benefit:** DoS protection, maintains service for existing users

**Configuration:**
```json
{ "Backpressure": { "MaxConnections": 10000 } }
```

---

### 4. Circuit Breakers for Dependencies ⚡

**What:** Fail fast when Kafka/Redis are slow/unavailable

**Three States:**
```
Closed → Normal operation
Open → Fail fast (30s)
Half-Open → Test recovery
```

**Key benefit:** Prevents cascading failures, automatic recovery

**Implementation:** Uses Polly library

---

## When to Use Each Feature

### Graceful Draining
- ✅ **Deployments:** Rolling updates, maintenance windows
- ✅ **Scaling down:** Reducing server count
- ✅ **Emergency shutdown:** Server issues detected

### Duplicate Detection
- ✅ **Security:** Detect account compromise
- ✅ **User experience:** Prevent confusion from multiple connections
- ✅ **State consistency:** Avoid split command output

### Backpressure
- ✅ **Traffic spikes:** Legitimate load increases
- ✅ **DoS attacks:** Malicious connection floods
- ✅ **Resource protection:** Prevent memory/thread exhaustion

### Circuit Breakers
- ✅ **Kafka issues:** Message broker degraded/down
- ✅ **Redis issues:** State store slow/unavailable
- ✅ **Network problems:** Intermittent connectivity

---

## Monitoring at a Glance

### Key Metrics

| Metric | Normal | Warning | Critical |
|--------|--------|---------|----------|
| **Backpressure Level** | 0 | 1-2 | 3 |
| **Active Connections** | < 8000 | 8000-9500 | > 9500 |
| **Duplicate Rate** | < 1/min | 1-10/min | > 10/min |
| **Circuit State** | Closed | Half-Open | Open |

### Prometheus Queries

```promql
# Current backpressure level
sharpmush_backpressure_level

# Duplicate detection rate
rate(sharpmush_duplicate_detected[5m])

# Circuit breaker states
sharpmush_circuit_kafka_state
sharpmush_circuit_redis_state

# Graceful draining status
sharpmush_draining_active
```

### Alert Rules

```yaml
# Critical capacity
- alert: BackpressureCritical
  expr: sharpmush_backpressure_level >= 3
  for: 1m

# Circuit open
- alert: CircuitBreakerOpen
  expr: sharpmush_circuit_kafka_state == 1 OR sharpmush_circuit_redis_state == 1
  for: 1m

# Duplicate flood
- alert: ExcessiveDuplicates
  expr: rate(sharpmush_duplicate_detected[5m]) > 10
```

---

## Configuration Quick Start

### Minimal Configuration (Defaults)

```json
{
  "ConnectionDraining": {
    "EnableGracefulDraining": true,
    "GracePeriodSeconds": 30
  },
  "DuplicateConnection": {
    "Policy": "Allow"
  },
  "Backpressure": {
    "Enabled": true,
    "MaxConnections": 10000
  },
  "CircuitBreaker": {
    "Kafka": { "Enabled": true },
    "Redis": { "Enabled": true }
  }
}
```

### Recommended Production Configuration

```json
{
  "ConnectionDraining": {
    "EnableGracefulDraining": true,
    "GracePeriodSeconds": 30,
    "WarningIntervalSeconds": 10
  },
  "DuplicateConnection": {
    "Policy": "DisconnectPrevious",
    "CheckIpAddress": true,
    "AllowMultipleIps": true,
    "MaxConnectionsPerPlayer": 3
  },
  "Backpressure": {
    "Enabled": true,
    "MaxConnections": 10000,
    "Thresholds": {
      "ElevatedPercent": 80.0,
      "HighPercent": 90.0,
      "CriticalPercent": 95.0
    }
  },
  "CircuitBreaker": {
    "Kafka": {
      "Enabled": true,
      "FailureThreshold": 5,
      "DurationOfBreakSeconds": 30
    },
    "Redis": {
      "Enabled": true,
      "FailureThreshold": 5,
      "DurationOfBreakSeconds": 30
    }
  }
}
```

---

## Implementation Checklist

### Phase 1: Graceful Draining (Week 1)
- [ ] Create `ConnectionDrainingService.cs`
- [ ] Add `ConnectionDrainingOptions.cs`
- [ ] Create `ServerShutdownWarningMessage.cs`
- [ ] Integrate with `IHostApplicationLifetime`
- [ ] Add unit tests (8 tests)
- [ ] Add integration test (1 test)
- [ ] Update documentation

### Phase 2: Duplicate Detection (Week 2)
- [ ] Create `DuplicateConnectionDetectionService.cs`
- [ ] Add `DuplicateConnectionOptions.cs`
- [ ] Integrate with login flow
- [ ] Add telemetry events
- [ ] Add unit tests (12 tests)
- [ ] Add integration tests (4 tests)

### Phase 3: Backpressure (Week 3)
- [ ] Create `BackpressureManagementService.cs`
- [ ] Add `BackpressureOptions.cs`
- [ ] Integrate with connection acceptance
- [ ] Add Prometheus metrics
- [ ] Add unit tests (10 tests)
- [ ] Add load tests (4 tests)
- [ ] Create Grafana dashboard

### Phase 4: Circuit Breakers (Week 4)
- [ ] Add Polly NuGet package
- [ ] Create `CircuitBreakerPolicies.cs`
- [ ] Integrate with Kafka producers
- [ ] Integrate with Redis operations
- [ ] Add unit tests (10 tests)
- [ ] Add integration tests (4 tests)

### Phase 5: Integration (Week 5)
- [ ] End-to-end integration tests
- [ ] Performance testing
- [ ] Documentation updates
- [ ] Operational runbooks
- [ ] Team training

---

## Testing Quick Reference

### Running Tests

```bash
# Unit tests
dotnet test --filter "ConnectionDrainingServiceTests"
dotnet test --filter "DuplicateConnectionDetectionServiceTests"
dotnet test --filter "BackpressureManagementServiceTests"
dotnet test --filter "CircuitBreakerTests"

# Integration tests
dotnet test --filter "GracefulShutdownIntegrationTests"
dotnet test --filter "DuplicateConnectionIntegrationTests"
dotnet test --filter "BackpressureIntegrationTests"

# Load tests
dotnet test --filter "ConnectionFloodTest"
dotnet test --filter "DuplicateConnectionLoadTest"
```

### Test Coverage Goals

| Component | Unit Tests | Integration Tests | Load Tests |
|-----------|------------|-------------------|------------|
| Graceful Draining | 8 | 3 | 1 |
| Duplicate Detection | 12 | 4 | 1 |
| Backpressure | 10 | 4 | 1 |
| Circuit Breakers | 10 | 4 | 2 |
| **Total** | **40** | **15** | **5** |

---

## Operational Procedures

### Responding to Backpressure Alert

1. Check Grafana dashboard for capacity metrics
2. Verify if legitimate traffic or attack:
   ```promql
   rate(sharpmush_connection_events_total[5m])
   ```
3. If attack: Enable stricter rate limits
4. If legitimate: Scale horizontally (add servers)
5. Monitor recovery

### Responding to Circuit Breaker Open

1. Check which service (Kafka or Redis)
2. Verify service health independently
3. Check network connectivity
4. Review error logs for root cause
5. Circuit will auto-recover if service recovers

### Responding to Duplicate Connection Flood

1. Check IP addresses for patterns
2. Review affected user accounts
3. Consider temporary IP restrictions
4. Notify security team if suspicious
5. Check for account compromises

### Emergency: Disable a Feature

```json
{
  "Features": {
    "GracefulDraining": false,    // Disable specific feature
    "DuplicateDetection": false,
    "Backpressure": false,
    "CircuitBreakers": false
  }
}
```

Restart not required - most services check config periodically.

---

## Benefits Summary

| Feature | User Benefit | Operator Benefit | System Benefit |
|---------|--------------|------------------|----------------|
| **Graceful Draining** | Time to save work | Clean deployments | Zero data loss |
| **Duplicate Detection** | Clear which connection is active | Security visibility | State consistency |
| **Backpressure** | Service remains available | DoS protection | Resource protection |
| **Circuit Breakers** | Fast failures vs timeouts | Automatic recovery | Prevents cascades |

---

## Related Documentation

- 📊 `TELNET_CONNECTION_TIMING_ANALYSIS.md` - Connection flow analysis (944 lines)
- 📋 `TELNET_CONNECTION_TIMING_SUMMARY.md` - Quick reference
- 🏗️ `RESILIENCE_IMPROVEMENTS_DESIGN.md` - Full design (1,340 lines)
- 🔧 `INPUT_OUTPUT_ARCHITECTURE.md` - System architecture

---

## Quick Decision Matrix

### Should I implement this feature?

| Scenario | Graceful Draining | Duplicate Detection | Backpressure | Circuit Breakers |
|----------|-------------------|---------------------|--------------|------------------|
| **Frequent deployments** | ✅ YES | Optional | Optional | Optional |
| **Public-facing server** | Optional | ✅ YES | ✅ YES | ✅ YES |
| **Private server** | Optional | Optional | Optional | ✅ YES |
| **High traffic** | Recommended | Recommended | ✅ YES | ✅ YES |
| **Distributed setup** | ✅ YES | ✅ YES | ✅ YES | ✅ YES |
| **Single server** | Optional | Optional | Recommended | ✅ YES |

---

## Getting Help

**Design Questions:** Refer to `RESILIENCE_IMPROVEMENTS_DESIGN.md`

**Implementation Questions:** See architecture diagrams in design doc

**Configuration Questions:** See complete config reference in design doc

**Monitoring Questions:** See Prometheus queries section

**Operational Issues:** See operational runbooks in design doc

---

## Next Steps

1. ✅ **Read this document** - Quick overview
2. ✅ **Read full design** - `RESILIENCE_IMPROVEMENTS_DESIGN.md`
3. 📋 **Plan implementation** - Choose features to implement
4. 🏗️ **Start with Phase 1** - Graceful draining (lowest risk)
5. 📊 **Set up monitoring** - Before enabling in production
6. 🧪 **Test thoroughly** - Follow testing strategy
7. 🚀 **Gradual rollout** - Use phased approach

---

**Document Version:** 1.0
**Last Updated:** 2026-02-17
**Related Analysis:** TELNET_CONNECTION_TIMING_ANALYSIS.md
