# Resilience Improvements - Design Document

## Overview

This document outlines the design for implementing resilience improvements to SharpMUSH's connection management system. These improvements ensure graceful degradation, clean shutdown, duplicate connection handling, and connection backpressure management.

**Related Documentation:**
- `TELNET_CONNECTION_TIMING_ANALYSIS.md` - Connection flow and timing analysis
- `INPUT_OUTPUT_ARCHITECTURE.md` - System architecture overview

---

## 1. Graceful Connection Draining

### Problem Statement

When the ConnectionServer or Server shuts down, active connections are abruptly terminated without:
- Warning messages to connected users
- Time to save state or complete in-progress commands
- Graceful logout procedures

This can result in:
- Lost user input
- Incomplete transactions
- Poor user experience
- Database inconsistencies

### Solution Design

#### 1.1 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Shutdown Sequence (30-second grace period)                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  T+0s:  ApplicationStopping Signal                              │
│         ├─→ ConnectionDrainingService.StartDrainingAsync()      │
│         │   ├─→ Set _isDraining = true                          │
│         │   ├─→ Reject new connections                          │
│         │   └─→ Send warning to all connected clients           │
│         │                                                        │
│         └─→ Send Shutdown Notice via Kafka                      │
│             Message: "Server shutting down in 30 seconds.       │
│                      Please save and disconnect."               │
│                                                                  │
│  T+0-30s: Grace Period                                          │
│         ├─→ Continue processing existing connections            │
│         ├─→ Allow voluntary disconnections                      │
│         ├─→ Countdown messages every 10 seconds                 │
│         │   (T+10s: "20 seconds remaining...")                  │
│         │   (T+20s: "10 seconds remaining...")                  │
│         └─→ Log connection count every 5 seconds                │
│                                                                  │
│  T+30s: ApplicationStopped Signal                               │
│         ├─→ Force disconnect remaining connections              │
│         ├─→ Flush Kafka producers                               │
│         ├─→ Close Redis connections                             │
│         └─→ Shutdown complete                                   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

#### 1.2 Implementation Components

**New Service: `ConnectionDrainingService`**

Location: `SharpMUSH.ConnectionServer/Services/ConnectionDrainingService.cs`

```csharp
public class ConnectionDrainingService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConnectionServerService _connectionService;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ConnectionDrainingService> _logger;
    private readonly ConnectionDrainingOptions _options;
    private CancellationTokenSource? _gracePeriodCts;
    private bool _isDraining;
    
    // Configuration from appsettings.json
    public class ConnectionDrainingOptions
    {
        public int GracePeriodSeconds { get; set; } = 30;
        public int WarningIntervalSeconds { get; set; } = 10;
        public bool EnableGracefulDraining { get; set; } = true;
        public string ShutdownMessage { get; set; } = 
            "Server is shutting down. Please save and disconnect.";
    }
    
    public Task StartAsync(CancellationToken ct)
    {
        // Register for ApplicationStopping event
        _lifetime.ApplicationStopping.Register(OnApplicationStopping);
        _lifetime.ApplicationStopped.Register(OnApplicationStopped);
        return Task.CompletedTask;
    }
    
    private void OnApplicationStopping()
    {
        // Initiate graceful draining
        StartDrainingAsync().GetAwaiter().GetResult();
    }
    
    private async Task StartDrainingAsync()
    {
        if (!_options.EnableGracefulDraining) return;
        
        _isDraining = true;
        _logger.LogWarning("Starting graceful connection draining. " +
            "Grace period: {Seconds}s", _options.GracePeriodSeconds);
        
        // Get all active connections
        var connections = _connectionService.GetAll().ToList();
        _logger.LogInformation("Active connections at shutdown: {Count}", 
            connections.Count);
        
        // Send shutdown notice to all connections
        foreach (var connection in connections)
        {
            try
            {
                var message = $"\r\n\x1b[1;33m*** {_options.ShutdownMessage} ***\x1b[0m\r\n" +
                             $"You have {_options.GracePeriodSeconds} seconds to disconnect.\r\n";
                await connection.OutputFunction(
                    Encoding.UTF8.GetBytes(message));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Failed to send shutdown notice to {Handle}", 
                    connection.Handle);
            }
        }
        
        // Publish shutdown event to Server
        await _messageBus.Publish(new ServerShutdownWarningMessage(
            _options.GracePeriodSeconds,
            DateTimeOffset.UtcNow));
        
        // Start countdown task
        _gracePeriodCts = new CancellationTokenSource();
        _ = CountdownTaskAsync(_gracePeriodCts.Token);
    }
    
    private async Task CountdownTaskAsync(CancellationToken ct)
    {
        var remaining = _options.GracePeriodSeconds;
        var interval = _options.WarningIntervalSeconds;
        
        while (remaining > 0 && !ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            remaining -= interval;
            
            if (remaining <= 0) break;
            
            // Send countdown message
            var connections = _connectionService.GetAll().ToList();
            _logger.LogInformation(
                "Shutdown countdown: {Remaining}s remaining, " +
                "{Count} connections active", 
                remaining, connections.Count);
            
            var message = $"\r\n\x1b[1;33m*** " +
                         $"Server shutdown in {remaining} seconds ***\x1b[0m\r\n";
            
            foreach (var connection in connections)
            {
                try
                {
                    await connection.OutputFunction(
                        Encoding.UTF8.GetBytes(message));
                }
                catch { /* Connection may have disconnected */ }
            }
        }
    }
    
    private void OnApplicationStopped()
    {
        _gracePeriodCts?.Cancel();
        
        // Force disconnect all remaining connections
        var connections = _connectionService.GetAll().ToList();
        _logger.LogWarning(
            "Forcing disconnect of {Count} remaining connections", 
            connections.Count);
        
        foreach (var connection in connections)
        {
            try
            {
                connection.DisconnectFunction();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error disconnecting {Handle}", connection.Handle);
            }
        }
    }
    
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**New Message: `ServerShutdownWarningMessage`**

Location: `SharpMUSH.Messages/ServerShutdownWarningMessage.cs`

```csharp
public record ServerShutdownWarningMessage(
    int GracePeriodSeconds,
    DateTimeOffset Timestamp);
```

**Consumer on Server Side**

Location: `SharpMUSH.Server/Consumers/ServerShutdownConsumer.cs`

```csharp
public class ServerShutdownWarningConsumer(
    ILogger<ServerShutdownWarningConsumer> logger,
    IConnectionService connectionService,
    INotifyService notifyService)
    : IMessageConsumer<ServerShutdownWarningMessage>
{
    public async Task HandleAsync(
        ServerShutdownWarningMessage message, 
        CancellationToken ct)
    {
        logger.LogWarning(
            "Received shutdown warning. Grace period: {Seconds}s", 
            message.GracePeriodSeconds);
        
        // Notify all logged-in players
        var connections = await connectionService.GetAll().ToListAsync();
        foreach (var conn in connections.Where(c => c.Ref.HasValue))
        {
            await notifyService.Notify(conn.Handle,
                $"SERVER SHUTDOWN: Save your work! " +
                $"Disconnecting in {message.GracePeriodSeconds} seconds.");
        }
    }
}
```

#### 1.3 Configuration

**appsettings.json:**

```json
{
  "ConnectionDraining": {
    "EnableGracefulDraining": true,
    "GracePeriodSeconds": 30,
    "WarningIntervalSeconds": 10,
    "ShutdownMessage": "Server is shutting down for maintenance. Please save and disconnect."
  }
}
```

#### 1.4 Registration

**Program.cs changes:**

```csharp
// Add configuration
builder.Services.Configure<ConnectionDrainingOptions>(
    builder.Configuration.GetSection("ConnectionDraining"));

// Register service
builder.Services.AddHostedService<ConnectionDrainingService>();
```

#### 1.5 Testing Strategy

**Unit Tests:**
- `ConnectionDrainingServiceTests.cs`
  - Test grace period countdown
  - Test message sending to connections
  - Test forced disconnection after grace period
  - Test configuration options

**Integration Tests:**
- `GracefulShutdownIntegrationTests.cs`
  - Start server with test client
  - Trigger shutdown
  - Verify warning message received
  - Verify connection remains open during grace period
  - Verify forced disconnect after grace period

---

## 2. Duplicate Connection Detection

### Problem Statement

The same player may attempt multiple simultaneous connections:
- Forgotten connection from home + new connection at work
- Browser tabs with multiple WebSocket connections
- Network issues causing "zombie" connections
- Malicious connection attempts

Current behavior: All connections are allowed, leading to:
- Confusion about which connection is "real"
- Split command output across connections
- Potential state synchronization issues

### Solution Design

#### 2.1 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Duplicate Connection Detection Flow                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  New Connection Attempt                                         │
│  ├─→ Check: Same IP + Same Username?                            │
│  │   └─→ NO: Allow connection normally                          │
│  │                                                               │
│  └─→ YES: Duplicate detected                                    │
│      ├─→ Get Policy from Configuration                          │
│      │                                                           │
│      ├─→ Policy: ALLOW                                          │
│      │   ├─→ Allow new connection                               │
│      │   ├─→ Notify both connections: "Multiple connections"    │
│      │   └─→ Log: Duplicate allowed                             │
│      │                                                           │
│      ├─→ Policy: REJECT_NEW                                     │
│      │   ├─→ Reject new connection                              │
│      │   ├─→ Send to new: "Already connected from {IP}"         │
│      │   ├─→ Notify old: "Connection attempt blocked from {IP}" │
│      │   └─→ Log: Duplicate rejected                            │
│      │                                                           │
│      └─→ Policy: DISCONNECT_PREVIOUS                            │
│          ├─→ Disconnect old connection                          │
│          ├─→ Send to old: "Disconnected: New login from {IP}"   │
│          ├─→ Allow new connection                               │
│          ├─→ Log: Previous connection disconnected              │
│          └─→ Record in telemetry                                │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

#### 2.2 Implementation Components

**Configuration Class:**

Location: `SharpMUSH.Configuration/DuplicateConnectionPolicy.cs`

```csharp
public enum DuplicateConnectionPolicy
{
    /// <summary>
    /// Allow multiple connections from the same player.
    /// Both connections remain active.
    /// </summary>
    Allow,
    
    /// <summary>
    /// Reject new connection attempts when player is already connected.
    /// Existing connection remains active.
    /// </summary>
    RejectNew,
    
    /// <summary>
    /// Disconnect previous connection when new connection is made.
    /// Most recent connection wins.
    /// </summary>
    DisconnectPrevious
}

public class DuplicateConnectionOptions
{
    /// <summary>
    /// Policy for handling duplicate connections.
    /// Default: Allow (backwards compatible)
    /// </summary>
    public DuplicateConnectionPolicy Policy { get; set; } = 
        DuplicateConnectionPolicy.Allow;
    
    /// <summary>
    /// Check IP address as part of duplicate detection.
    /// If false, only checks player identity.
    /// </summary>
    public bool CheckIpAddress { get; set; } = true;
    
    /// <summary>
    /// Allow duplicate connections from different IPs.
    /// Useful for mobile + desktop scenarios.
    /// </summary>
    public bool AllowMultipleIps { get; set; } = true;
    
    /// <summary>
    /// Grace period (seconds) to consider connections as duplicate.
    /// Prevents false positives during reconnection.
    /// </summary>
    public int DuplicateGraceWindowSeconds { get; set; } = 5;
    
    /// <summary>
    /// Maximum connections allowed per player (if Policy = Allow).
    /// 0 = unlimited
    /// </summary>
    public int MaxConnectionsPerPlayer { get; set; } = 3;
}
```

**Service: `DuplicateConnectionDetectionService`**

Location: `SharpMUSH.Library/Services/DuplicateConnectionDetectionService.cs`

```csharp
public interface IDuplicateConnectionDetectionService
{
    Task<DuplicateConnectionResult> CheckDuplicateAsync(
        long newHandle,
        string ipAddress,
        DBRef? playerRef);
    
    Task RecordConnectionAsync(
        long handle, 
        string ipAddress, 
        DBRef? playerRef);
    
    Task RemoveConnectionAsync(long handle);
}

public record DuplicateConnectionResult(
    bool IsDuplicate,
    List<long> ExistingHandles,
    DuplicateConnectionAction RecommendedAction);

public enum DuplicateConnectionAction
{
    Allow,
    RejectNew,
    DisconnectPrevious
}

public class DuplicateConnectionDetectionService(
    IConnectionService connectionService,
    IOptions<DuplicateConnectionOptions> options,
    ILogger<DuplicateConnectionDetectionService> logger)
    : IDuplicateConnectionDetectionService
{
    private readonly DuplicateConnectionOptions _options = options.Value;
    
    // Track connection -> (IP, PlayerRef, ConnectedAt)
    private readonly ConcurrentDictionary<long, ConnectionInfo> _connections = new();
    
    private record ConnectionInfo(
        string IpAddress, 
        DBRef? PlayerRef, 
        DateTimeOffset ConnectedAt);
    
    public async Task<DuplicateConnectionResult> CheckDuplicateAsync(
        long newHandle,
        string ipAddress,
        DBRef? playerRef)
    {
        // No player ref yet = not duplicate (pre-login)
        if (!playerRef.HasValue)
        {
            return new DuplicateConnectionResult(
                false, [], DuplicateConnectionAction.Allow);
        }
        
        // Find existing connections for this player
        var existingConnections = _connections
            .Where(kvp => kvp.Value.PlayerRef.HasValue && 
                         kvp.Value.PlayerRef.Value.Equals(playerRef.Value))
            .ToList();
        
        if (!existingConnections.Any())
        {
            return new DuplicateConnectionResult(
                false, [], DuplicateConnectionAction.Allow);
        }
        
        // Check if duplicate by IP (if configured)
        if (_options.CheckIpAddress && _options.AllowMultipleIps)
        {
            // Allow if from different IP
            var sameIp = existingConnections.Any(kvp => 
                kvp.Value.IpAddress == ipAddress);
            
            if (!sameIp)
            {
                return new DuplicateConnectionResult(
                    false, [], DuplicateConnectionAction.Allow);
            }
        }
        
        // Check connection limit
        if (_options.Policy == DuplicateConnectionPolicy.Allow)
        {
            if (_options.MaxConnectionsPerPlayer > 0 && 
                existingConnections.Count >= _options.MaxConnectionsPerPlayer)
            {
                logger.LogWarning(
                    "Player {Player} exceeded max connections ({Max})", 
                    playerRef, _options.MaxConnectionsPerPlayer);
                
                return new DuplicateConnectionResult(
                    true,
                    existingConnections.Select(kvp => kvp.Key).ToList(),
                    DuplicateConnectionAction.RejectNew);
            }
        }
        
        // Check grace window (recent disconnect/reconnect)
        var recentDisconnect = existingConnections.Any(kvp =>
            (DateTimeOffset.UtcNow - kvp.Value.ConnectedAt).TotalSeconds 
            < _options.DuplicateGraceWindowSeconds);
        
        if (recentDisconnect)
        {
            // Likely a reconnection, allow it
            return new DuplicateConnectionResult(
                false, [], DuplicateConnectionAction.Allow);
        }
        
        // Duplicate detected, apply policy
        var action = _options.Policy switch
        {
            DuplicateConnectionPolicy.Allow => 
                DuplicateConnectionAction.Allow,
            DuplicateConnectionPolicy.RejectNew => 
                DuplicateConnectionAction.RejectNew,
            DuplicateConnectionPolicy.DisconnectPrevious => 
                DuplicateConnectionAction.DisconnectPrevious,
            _ => DuplicateConnectionAction.Allow
        };
        
        logger.LogInformation(
            "Duplicate connection detected for player {Player} from {IP}. " +
            "Existing: {Count}, Action: {Action}",
            playerRef, ipAddress, existingConnections.Count, action);
        
        return new DuplicateConnectionResult(
            true,
            existingConnections.Select(kvp => kvp.Key).ToList(),
            action);
    }
    
    public Task RecordConnectionAsync(
        long handle, 
        string ipAddress, 
        DBRef? playerRef)
    {
        _connections.AddOrUpdate(
            handle,
            new ConnectionInfo(ipAddress, playerRef, DateTimeOffset.UtcNow),
            (_, _) => new ConnectionInfo(ipAddress, playerRef, DateTimeOffset.UtcNow));
        
        return Task.CompletedTask;
    }
    
    public Task RemoveConnectionAsync(long handle)
    {
        _connections.TryRemove(handle, out _);
        return Task.CompletedTask;
    }
}
```

**Integration into Login Flow:**

Location: `SharpMUSH.Implementation/Commands/LoginCommands.cs` (modify existing `connect` command)

```csharp
// After successful authentication, before binding
var duplicateCheck = await _duplicateDetection.CheckDuplicateAsync(
    handle, ipAddress, playerDbRef);

if (duplicateCheck.IsDuplicate)
{
    switch (duplicateCheck.RecommendedAction)
    {
        case DuplicateConnectionAction.RejectNew:
            await notifyService.Notify(handle,
                "You are already connected from another location. " +
                "Please disconnect your other session first.");
            return CallState.Empty;
            
        case DuplicateConnectionAction.DisconnectPrevious:
            foreach (var oldHandle in duplicateCheck.ExistingHandles)
            {
                await notifyService.Notify(oldHandle,
                    "You have been disconnected: " +
                    "New login from a different location.");
                await connectionService.Disconnect(oldHandle);
            }
            await notifyService.Notify(handle,
                "Disconnected your previous connection.");
            break;
            
        case DuplicateConnectionAction.Allow:
            // Notify both
            await notifyService.Notify(handle,
                $"Warning: You have {duplicateCheck.ExistingHandles.Count + 1} " +
                "active connections.");
            foreach (var oldHandle in duplicateCheck.ExistingHandles)
            {
                await notifyService.Notify(oldHandle,
                    "A new connection has been established for your account.");
            }
            break;
    }
}

// Record this connection
await _duplicateDetection.RecordConnectionAsync(
    handle, ipAddress, playerDbRef);
```

#### 2.3 Configuration

**appsettings.json:**

```json
{
  "DuplicateConnection": {
    "Policy": "DisconnectPrevious",
    "CheckIpAddress": true,
    "AllowMultipleIps": true,
    "DuplicateGraceWindowSeconds": 5,
    "MaxConnectionsPerPlayer": 3
  }
}
```

#### 2.4 Telemetry

Add metrics to track duplicate connection events:

```csharp
_telemetryService?.RecordConnectionEvent("duplicate_detected");
_telemetryService?.RecordConnectionEvent("duplicate_rejected");
_telemetryService?.RecordConnectionEvent("duplicate_previous_disconnected");
```

#### 2.5 Testing Strategy

**Unit Tests:**
- `DuplicateConnectionDetectionServiceTests.cs`
  - Test each policy (Allow, RejectNew, DisconnectPrevious)
  - Test IP-based detection
  - Test grace window for reconnections
  - Test max connections limit

**Integration Tests:**
- `DuplicateConnectionIntegrationTests.cs`
  - Connect twice with same credentials
  - Verify each policy behavior
  - Test different IP scenarios
  - Test reconnection within grace window

---

## 3. Connection Backpressure Management

### Problem Statement

Under high load, the system can be overwhelmed by:
- Connection floods (DoS attacks)
- Legitimate traffic spikes
- Slow clients blocking resources

Without backpressure, this leads to:
- Memory exhaustion
- Thread pool starvation
- Cascading failures
- Poor experience for all users

### Solution Design

#### 3.1 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Connection Backpressure Levels                                  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Level 0: NORMAL (< 80% capacity)                               │
│  ├─→ Accept all connections                                     │
│  ├─→ Process all commands normally                              │
│  └─→ No rate limiting                                           │
│                                                                  │
│  Level 1: ELEVATED (80-90% capacity)                            │
│  ├─→ Log warning                                                │
│  ├─→ Start monitoring                                           │
│  ├─→ Rate limit new connections (1 per second per IP)           │
│  └─→ Send warning to admins                                     │
│                                                                  │
│  Level 2: HIGH (90-95% capacity)                                │
│  ├─→ Rate limit new connections (1 per 5 seconds per IP)        │
│  ├─→ Prioritize existing connections                            │
│  ├─→ Defer non-critical operations                              │
│  ├─→ Send alerts to ops team                                    │
│  └─→ Show "Server busy" message to new connections              │
│                                                                  │
│  Level 3: CRITICAL (> 95% capacity)                             │
│  ├─→ Reject new connections                                     │
│  ├─→ Send "Server at capacity" message                          │
│  ├─→ Maintain existing connections only                         │
│  ├─→ Trigger auto-scaling (if configured)                       │
│  └─→ Alert: Critical capacity reached                           │
│                                                                  │
│  Capacity Metrics:                                              │
│  ├─→ Active connections vs Max (10,000 default)                 │
│  ├─→ Kafka consumer lag vs Threshold (1000 messages)            │
│  ├─→ Memory usage vs Max (80% of available)                     │
│  ├─→ CPU usage vs Max (80% sustained)                           │
│  └─→ Redis response time vs Threshold (10ms P95)                │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

#### 3.2 Implementation Components

**Configuration:**

Location: `SharpMUSH.Configuration/BackpressureOptions.cs`

```csharp
public enum BackpressureLevel
{
    Normal,     // < 80% capacity
    Elevated,   // 80-90% capacity
    High,       // 90-95% capacity
    Critical    // > 95% capacity
}

public class BackpressureOptions
{
    /// <summary>
    /// Enable backpressure management
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Maximum concurrent connections before backpressure
    /// </summary>
    public int MaxConnections { get; set; } = 10000;
    
    /// <summary>
    /// Thresholds for each backpressure level (percentage)
    /// </summary>
    public BackpressureThresholds Thresholds { get; set; } = new();
    
    /// <summary>
    /// Rate limiting settings per level
    /// </summary>
    public BackpressureRateLimits RateLimits { get; set; } = new();
    
    /// <summary>
    /// Check backpressure every N seconds
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 5;
}

public class BackpressureThresholds
{
    public double ElevatedPercent { get; set; } = 80.0;
    public double HighPercent { get; set; } = 90.0;
    public double CriticalPercent { get; set; } = 95.0;
}

public class BackpressureRateLimits
{
    /// <summary>
    /// Elevated: Connections per IP per second
    /// </summary>
    public double ElevatedConnectionsPerSecond { get; set; } = 1.0;
    
    /// <summary>
    /// High: Connections per IP per second
    /// </summary>
    public double HighConnectionsPerSecond { get; set; } = 0.2;
    
    /// <summary>
    /// Critical: Reject all new connections
    /// </summary>
    public bool CriticalRejectNew { get; set; } = true;
}
```

**Service: `BackpressureManagementService`**

Location: `SharpMUSH.ConnectionServer/Services/BackpressureManagementService.cs`

```csharp
public interface IBackpressureService
{
    BackpressureLevel CurrentLevel { get; }
    bool ShouldAcceptConnection(string ipAddress);
    Task<ConnectionAcceptance> CheckConnectionAsync(string ipAddress);
}

public record ConnectionAcceptance(
    bool Accept,
    string? RejectionMessage,
    BackpressureLevel CurrentLevel);

public class BackpressureManagementService(
    IConnectionServerService connectionService,
    IOptions<BackpressureOptions> options,
    ILogger<BackpressureManagementService> logger,
    ITelemetryService? telemetryService)
    : BackgroundService, IBackpressureService
{
    private readonly BackpressureOptions _options = options.Value;
    private BackpressureLevel _currentLevel = BackpressureLevel.Normal;
    
    // Track connections per IP for rate limiting
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> 
        _connectionTimestamps = new();
    
    public BackpressureLevel CurrentLevel => _currentLevel;
    
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Backpressure management disabled");
            return;
        }
        
        logger.LogInformation("Starting backpressure monitoring. " +
            "Max connections: {Max}", _options.MaxConnections);
        
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_options.CheckIntervalSeconds), ct);
            
            await CheckBackpressureAsync();
        }
    }
    
    private async Task CheckBackpressureAsync()
    {
        // Get current metrics
        var activeConnections = connectionService.GetAll().Count();
        var capacity = (double)activeConnections / _options.MaxConnections * 100;
        
        // Determine level
        var oldLevel = _currentLevel;
        _currentLevel = capacity switch
        {
            >= var c when c >= _options.Thresholds.CriticalPercent 
                => BackpressureLevel.Critical,
            >= var h when h >= _options.Thresholds.HighPercent 
                => BackpressureLevel.High,
            >= var e when e >= _options.Thresholds.ElevatedPercent 
                => BackpressureLevel.Elevated,
            _ => BackpressureLevel.Normal
        };
        
        // Log level changes
        if (_currentLevel != oldLevel)
        {
            logger.LogWarning(
                "Backpressure level changed: {Old} → {New}. " +
                "Active: {Active}/{Max} ({Capacity:F1}%)",
                oldLevel, _currentLevel, 
                activeConnections, _options.MaxConnections, capacity);
            
            telemetryService?.RecordConnectionEvent(
                $"backpressure_{_currentLevel.ToString().ToLower()}");
            
            // Send alerts based on level
            await HandleLevelChangeAsync(oldLevel, _currentLevel, 
                activeConnections, capacity);
        }
        
        // Cleanup old timestamps (older than 1 minute)
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        foreach (var ip in _connectionTimestamps.Keys.ToList())
        {
            if (_connectionTimestamps.TryGetValue(ip, out var queue))
            {
                while (queue.TryPeek(out var timestamp) && 
                       timestamp < cutoff)
                {
                    queue.TryDequeue(out _);
                }
                
                if (queue.Count == 0)
                {
                    _connectionTimestamps.TryRemove(ip, out _);
                }
            }
        }
    }
    
    public bool ShouldAcceptConnection(string ipAddress)
    {
        return CheckConnectionAsync(ipAddress)
            .GetAwaiter().GetResult().Accept;
    }
    
    public async Task<ConnectionAcceptance> CheckConnectionAsync(
        string ipAddress)
    {
        if (!_options.Enabled)
        {
            return new ConnectionAcceptance(true, null, 
                BackpressureLevel.Normal);
        }
        
        // Critical level: Reject all
        if (_currentLevel == BackpressureLevel.Critical && 
            _options.RateLimits.CriticalRejectNew)
        {
            logger.LogWarning("Rejecting connection from {IP}: " +
                "Critical capacity", ipAddress);
            
            return new ConnectionAcceptance(
                false,
                "Server is at capacity. Please try again later.",
                _currentLevel);
        }
        
        // Check rate limits
        var now = DateTimeOffset.UtcNow;
        var timestamps = _connectionTimestamps.GetOrAdd(
            ipAddress, _ => new Queue<DateTimeOffset>());
        
        // Add current attempt
        timestamps.Enqueue(now);
        
        // Calculate rate
        var windowSeconds = _currentLevel switch
        {
            BackpressureLevel.Elevated => 1.0,
            BackpressureLevel.High => 5.0,
            _ => 0.1 // Normal: effectively no limit
        };
        
        var maxRate = _currentLevel switch
        {
            BackpressureLevel.Elevated => 
                _options.RateLimits.ElevatedConnectionsPerSecond,
            BackpressureLevel.High => 
                _options.RateLimits.HighConnectionsPerSecond,
            _ => double.MaxValue
        };
        
        var windowStart = now.AddSeconds(-windowSeconds);
        var recentAttempts = timestamps
            .Count(t => t >= windowStart);
        
        var currentRate = recentAttempts / windowSeconds;
        
        if (currentRate > maxRate)
        {
            logger.LogWarning("Rate limit exceeded for {IP}: " +
                "{Rate:F2}/s > {Max:F2}/s at level {Level}",
                ipAddress, currentRate, maxRate, _currentLevel);
            
            return new ConnectionAcceptance(
                false,
                "Too many connection attempts. Please wait and try again.",
                _currentLevel);
        }
        
        return new ConnectionAcceptance(true, null, _currentLevel);
    }
    
    private async Task HandleLevelChangeAsync(
        BackpressureLevel oldLevel,
        BackpressureLevel newLevel,
        int activeConnections,
        double capacityPercent)
    {
        // Log to ops/monitoring
        var message = newLevel switch
        {
            BackpressureLevel.Elevated => 
                $"Elevated load: {activeConnections} connections " +
                $"({capacityPercent:F1}% capacity)",
            BackpressureLevel.High => 
                $"High load: {activeConnections} connections " +
                $"({capacityPercent:F1}% capacity)",
            BackpressureLevel.Critical => 
                $"CRITICAL: At capacity with {activeConnections} connections " +
                $"({capacityPercent:F1}%)",
            _ => $"Load normalized: {activeConnections} connections"
        };
        
        logger.LogInformation("Backpressure: {Message}", message);
        
        // TODO: Send to monitoring/alerting system
        // - Elevated: Log only
        // - High: Alert admins
        // - Critical: Page on-call engineer
    }
}
```

**Integration into Connection Acceptance:**

Location: `SharpMUSH.ConnectionServer/ProtocolHandlers/TelnetServer.cs`

```csharp
public override async Task OnConnectedAsync(ConnectionContext connection)
{
    var remoteIp = connection.RemoteEndPoint is IPEndPoint remoteEndpoint
        ? remoteEndpoint.Address.ToString()
        : "unknown";
    
    // Check backpressure before accepting
    var acceptance = await _backpressureService.CheckConnectionAsync(remoteIp);
    
    if (!acceptance.Accept)
    {
        // Send rejection message and close
        var message = $"\r\n{acceptance.RejectionMessage}\r\n";
        await connection.Transport.Output.WriteAsync(
            Encoding.UTF8.GetBytes(message));
        
        connection.Abort();
        
        _logger.LogWarning(
            "Rejected connection from {IP} due to backpressure: {Level}",
            remoteIp, acceptance.CurrentLevel);
        
        return;
    }
    
    // Continue with normal connection flow...
}
```

#### 3.3 Configuration

**appsettings.json:**

```json
{
  "Backpressure": {
    "Enabled": true,
    "MaxConnections": 10000,
    "CheckIntervalSeconds": 5,
    "Thresholds": {
      "ElevatedPercent": 80.0,
      "HighPercent": 90.0,
      "CriticalPercent": 95.0
    },
    "RateLimits": {
      "ElevatedConnectionsPerSecond": 1.0,
      "HighConnectionsPerSecond": 0.2,
      "CriticalRejectNew": true
    }
  }
}
```

#### 3.4 Monitoring

**Prometheus Metrics:**

```csharp
// Add to TelemetryService
private readonly Gauge<int> _backpressureLevel;

_backpressureLevel = _meter.CreateObservableGauge<int>(
    "sharpmush.backpressure.level",
    () => (int)_backpressureService.CurrentLevel,
    description: "Current backpressure level (0=Normal, 1=Elevated, 2=High, 3=Critical)");
```

**Grafana Dashboard Queries:**

```promql
# Backpressure level over time
sharpmush_backpressure_level

# Connection acceptance rate
rate(sharpmush_connection_events_total{event_type="connected"}[5m])

# Connection rejection rate
rate(sharpmush_connection_events_total{event_type="backpressure_rejected"}[5m])

# Capacity utilization
sharpmush_connections_active / on() group_left() sharpmush_config_max_connections * 100
```

#### 3.5 Testing Strategy

**Load Tests:**
- `BackpressureLoadTests.cs`
  - Simulate connection flood (100+ connections/second)
  - Verify rate limiting at each level
  - Verify rejection at critical level
  - Verify recovery when load decreases

**Unit Tests:**
- `BackpressureManagementServiceTests.cs`
  - Test level calculation
  - Test rate limiting logic
  - Test IP-based rate tracking
  - Test configuration options

---

## 4. Circuit Breaker for Kafka/Redis

### Problem Statement

When Kafka or Redis becomes unavailable or slow:
- Requests queue up waiting for timeout
- Thread pool exhaustion
- Cascading failures
- System-wide outage

### Solution Design

#### 4.1 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Circuit Breaker State Machine                                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  CLOSED (Normal Operation)                                      │
│  ├─→ All requests pass through                                  │
│  ├─→ Track failure rate                                         │
│  └─→ If failures > threshold: OPEN                              │
│                                                                  │
│  OPEN (Failing Fast)                                            │
│  ├─→ Reject all requests immediately                            │
│  ├─→ Return cached/default values                               │
│  ├─→ After timeout: HALF_OPEN                                   │
│  └─→ Duration: 30 seconds                                       │
│                                                                  │
│  HALF_OPEN (Testing Recovery)                                   │
│  ├─→ Allow limited requests through                             │
│  ├─→ If success: CLOSED                                         │
│  ├─→ If failure: OPEN                                           │
│  └─→ Test duration: 10 seconds                                  │
│                                                                  │
│  Thresholds:                                                    │
│  ├─→ Failure rate: 50% over 10 requests                         │
│  ├─→ Timeout: 5 seconds                                         │
│  └─→ Recovery period: 30 seconds                                │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

#### 4.2 Implementation

Use **Polly** library for circuit breaker pattern:

```csharp
// Add NuGet package: Polly

public static class CircuitBreakerPolicies
{
    public static IAsyncPolicy<T> CreateKafkaPolicy<T>(
        ILogger logger)
    {
        return Policy<T>
            .Handle<KafkaException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (result, duration) =>
                {
                    logger.LogError("Kafka circuit breaker OPEN. " +
                        "Duration: {Duration}s", duration.TotalSeconds);
                },
                onReset: () =>
                {
                    logger.LogInformation("Kafka circuit breaker CLOSED");
                },
                onHalfOpen: () =>
                {
                    logger.LogWarning("Kafka circuit breaker HALF-OPEN");
                });
    }
    
    public static IAsyncPolicy CreateRedisPolicy(ILogger logger)
    {
        return Policy
            .Handle<RedisException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, duration) =>
                {
                    logger.LogError(ex, "Redis circuit breaker OPEN. " +
                        "Duration: {Duration}s", duration.TotalSeconds);
                },
                onReset: () =>
                {
                    logger.LogInformation("Redis circuit breaker CLOSED");
                },
                onHalfOpen: () =>
                {
                    logger.LogWarning("Redis circuit breaker HALF-OPEN");
                });
    }
}

// Usage in ConnectionService
public class ConnectionService
{
    private readonly IAsyncPolicy _redisPolicy;
    
    public ConnectionService(ILogger logger, ...)
    {
        _redisPolicy = CircuitBreakerPolicies.CreateRedisPolicy(logger);
    }
    
    public async ValueTask Register(...)
    {
        // In-memory always succeeds
        _sessionState.AddOrUpdate(...);
        
        // Redis write with circuit breaker
        if (stateStore != null)
        {
            try
            {
                await _redisPolicy.ExecuteAsync(async () =>
                {
                    await stateStore.SetConnectionAsync(...);
                });
            }
            catch (BrokenCircuitException)
            {
                // Circuit is open, skip Redis write
                _logger.LogWarning("Redis circuit open, skipping write");
            }
        }
    }
}
```

---

## 5. Implementation Roadmap

### Phase 1: Graceful Draining (Week 1)
1. Implement `ConnectionDrainingService`
2. Add configuration options
3. Integrate with Program.cs
4. Add unit tests
5. Add integration tests
6. Document usage

### Phase 2: Duplicate Detection (Week 2)
1. Implement `DuplicateConnectionDetectionService`
2. Add configuration options
3. Integrate with login flow
4. Add telemetry
5. Add unit tests
6. Add integration tests

### Phase 3: Backpressure Management (Week 3)
1. Implement `BackpressureManagementService`
2. Add configuration options
3. Integrate with connection acceptance
4. Add Prometheus metrics
5. Add load tests
6. Create Grafana dashboard

### Phase 4: Circuit Breakers (Week 4)
1. Add Polly package
2. Implement circuit breaker policies
3. Integrate with Kafka producers
4. Integrate with Redis operations
5. Add monitoring
6. Add tests

### Phase 5: Integration & Documentation (Week 5)
1. Integration testing across all features
2. Performance testing
3. Update documentation
4. Create runbooks for operators
5. Training for support team

---

## 6. Monitoring & Alerting

### Prometheus Metrics to Add

```csharp
// Graceful draining
sharpmush.draining.active (gauge: 0 or 1)
sharpmush.draining.remaining_connections (gauge)

// Duplicate connections
sharpmush.duplicate.detected (counter)
sharpmush.duplicate.rejected (counter)
sharpmush.duplicate.disconnected_previous (counter)

// Backpressure
sharpmush.backpressure.level (gauge: 0-3)
sharpmush.backpressure.rejected (counter)
sharpmush.backpressure.capacity_percent (gauge)

// Circuit breakers
sharpmush.circuit.kafka.state (gauge: 0=closed, 1=open, 2=half-open)
sharpmush.circuit.redis.state (gauge: 0=closed, 1=open, 2=half-open)
```

### Alert Rules

```yaml
# Backpressure critical
- alert: BackpressureCritical
  expr: sharpmush_backpressure_level >= 3
  for: 1m
  annotations:
    summary: "Server at critical capacity"

# Circuit breaker open
- alert: CircuitBreakerOpen
  expr: sharpmush_circuit_kafka_state == 1 OR sharpmush_circuit_redis_state == 1
  for: 1m
  annotations:
    summary: "Circuit breaker open - service degraded"

# Excessive duplicates
- alert: ExcessiveDuplicateConnections
  expr: rate(sharpmush_duplicate_detected[5m]) > 10
  annotations:
    summary: "High rate of duplicate connections"
```

---

## 7. Configuration Reference

### Complete appsettings.json

```json
{
  "ConnectionDraining": {
    "EnableGracefulDraining": true,
    "GracePeriodSeconds": 30,
    "WarningIntervalSeconds": 10,
    "ShutdownMessage": "Server is shutting down. Please save and disconnect."
  },
  
  "DuplicateConnection": {
    "Policy": "DisconnectPrevious",
    "CheckIpAddress": true,
    "AllowMultipleIps": true,
    "DuplicateGraceWindowSeconds": 5,
    "MaxConnectionsPerPlayer": 3
  },
  
  "Backpressure": {
    "Enabled": true,
    "MaxConnections": 10000,
    "CheckIntervalSeconds": 5,
    "Thresholds": {
      "ElevatedPercent": 80.0,
      "HighPercent": 90.0,
      "CriticalPercent": 95.0
    },
    "RateLimits": {
      "ElevatedConnectionsPerSecond": 1.0,
      "HighConnectionsPerSecond": 0.2,
      "CriticalRejectNew": true
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

## 8. Testing Strategy Summary

### Unit Tests (Estimated: 40 tests)
- ConnectionDrainingServiceTests (8 tests)
- DuplicateConnectionDetectionServiceTests (12 tests)
- BackpressureManagementServiceTests (10 tests)
- CircuitBreakerTests (10 tests)

### Integration Tests (Estimated: 15 tests)
- GracefulShutdownIntegrationTests (3 tests)
- DuplicateConnectionIntegrationTests (4 tests)
- BackpressureIntegrationTests (4 tests)
- CircuitBreakerIntegrationTests (4 tests)

### Load Tests (Estimated: 5 tests)
- ConnectionFloodTest (test backpressure under load)
- DuplicateConnectionLoadTest (test duplicate detection at scale)
- GracefulShutdownLoadTest (test draining with many connections)
- CircuitBreakerRecoveryTest (test recovery under load)
- EndToEndResilienceTest (all features together)

---

## 9. Rollout Strategy

### Development Environment
1. Enable all features with verbose logging
2. Test with small connection limits (100)
3. Verify logs and metrics

### Staging Environment
1. Enable with moderate limits (1000)
2. Run load tests
3. Verify alerting
4. Test graceful shutdown

### Production Rollout
1. **Week 1:** Circuit breakers only (lowest risk)
2. **Week 2:** Duplicate detection with Policy=Allow
3. **Week 3:** Backpressure with elevated thresholds
4. **Week 4:** Graceful draining
5. **Week 5:** Lower thresholds to production values

### Feature Flags

All features should have enable/disable flags:
```json
{
  "Features": {
    "GracefulDraining": true,
    "DuplicateDetection": true,
    "Backpressure": true,
    "CircuitBreakers": true
  }
}
```

---

## 10. Operational Runbooks

### Handling Backpressure Alerts

**Alert: BackpressureCritical**

1. Check current connection count:
   ```promql
   sharpmush_connections_active
   ```

2. Check if legitimate spike or attack:
   ```promql
   rate(sharpmush_connection_events_total[5m])
   ```

3. If attack: Enable stricter rate limits
4. If legitimate: Scale horizontally (add servers)

### Handling Circuit Breaker Open

**Alert: CircuitBreakerOpen**

1. Identify which service (Kafka or Redis)
2. Check service health
3. Check network connectivity
4. Review error logs
5. If service is healthy, circuit will auto-recover

### Handling Duplicate Connection Flood

**Alert: ExcessiveDuplicateConnections**

1. Check if account compromise
2. Review IP addresses
3. Consider temporary IP ban
4. Notify affected users

---

## 11. Benefits Summary

### Graceful Draining
- ✅ Improved user experience during deployments
- ✅ Zero data loss on shutdown
- ✅ Time for users to save work
- ✅ Reduced support tickets

### Duplicate Detection
- ✅ Prevents account confusion
- ✅ Security: Detects unauthorized access
- ✅ Prevents state synchronization issues
- ✅ Configurable behavior per environment

### Backpressure Management
- ✅ Prevents system overload
- ✅ Protects against DoS attacks
- ✅ Maintains service for existing users
- ✅ Automatic scaling trigger

### Circuit Breakers
- ✅ Fails fast instead of hanging
- ✅ Prevents cascading failures
- ✅ Automatic recovery
- ✅ Maintains partial functionality

---

## 12. Future Enhancements

### Advanced Features
1. **Adaptive Backpressure:** Learn normal patterns, adjust thresholds
2. **Smart Draining:** Prioritize connections by idle time
3. **Connection Migration:** Move connections between servers
4. **Predictive Scaling:** Scale before reaching capacity
5. **Geographic Rate Limiting:** Different limits per region

### Machine Learning Integration
1. **Anomaly Detection:** Identify unusual connection patterns
2. **Capacity Forecasting:** Predict future load
3. **Attack Detection:** ML-based DoS detection
4. **Optimal Threshold Tuning:** Learn best thresholds

---

## Summary

This design document provides a complete blueprint for implementing resilience improvements to SharpMUSH's connection management system. The four main components work together to provide:

1. **Graceful Draining:** Clean shutdowns with user notification
2. **Duplicate Detection:** Prevent and manage multiple connections
3. **Backpressure Management:** Protect system under load
4. **Circuit Breakers:** Fail fast and recover automatically

Each component is:
- ✅ **Configurable:** Feature flags and tunable parameters
- ✅ **Observable:** Comprehensive metrics and logging
- ✅ **Testable:** Unit, integration, and load tests
- ✅ **Production-ready:** Rollout strategy and runbooks

**Estimated Implementation Time:** 5 weeks (1 engineer)
**Estimated Test Coverage:** 60+ tests
**Configuration Complexity:** Low (YAML-based)
**Operational Overhead:** Minimal (mostly automated)
