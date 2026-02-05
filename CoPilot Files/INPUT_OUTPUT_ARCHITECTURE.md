# SharpMUSH Input/Output Architecture

## System Overview

SharpMUSH uses a distributed architecture with two main processes communicating via Kafka/Redpanda message queues and sharing state via Redis.

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                              CLIENT CONNECTIONS                                      │
│                         (Telnet/WebSocket Clients)                                   │
└─────────────────────────────────────────────────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                         SharpMUSH.ConnectionServer                                   │
│ ┌─────────────────────┐  ┌──────────────────────┐  ┌─────────────────────────────┐ │
│ │   TelnetServer      │  │   WebSocketServer    │  │  ConnectionServerService    │ │
│ │ (Kestrel ProtocolH) │  │ (HTTP Upgrade Handler)│  │  (Connection Registry)      │ │
│ └─────────┬───────────┘  └──────────┬───────────┘  └───────────────┬─────────────┘ │
│           │                          │                              │               │
│           ▼                          ▼                              │               │
│                                                                                      │
│  CONSUMERS (from Kafka):                     PRODUCERS (to Kafka):                  │
│  ┌─────────────────────────┐                ┌──────────────────────────┐            │
│  │ TelnetOutputConsumer    │                │ TelnetInputMessage       │            │
│  │ TelnetPromptConsumer    │                │ GMCPSignalMessage        │            │
│  │ BroadcastConsumer       │                │ MSDPUpdateMessage        │            │
│  │ DisconnectConnection    │                │ NAWSUpdateMessage        │            │
│  │ GMCPOutputConsumer      │                │ ConnectionEstablished    │            │
│  │ UpdatePlayerPrefs       │                │ ConnectionClosedMessage  │            │
│  │ WebSocketOutput/Prompt  │                │ WebSocketInputMessage    │            │
│  └─────────────────────────┘                └──────────────────────────┘            │
└─────────────────────────────────────────────────────────────────────────────────────┘
                    │                                          ▲
                    │                                          │
                    ▼                                          │
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                              Kafka/Redpanda                                          │
│                                                                                      │
│  TOPICS (auto-created, kebab-case from message types):                              │
│  ┌───────────────────────────────────────────────────────────────────────────────┐  │
│  │ INPUT TOPICS (ConnectionServer → Server):                                      │  │
│  │   telnet-input, websocket-input, gmcp-signal, msdp-update, naws-update,       │  │
│  │   connection-established, connection-closed                                    │  │
│  ├───────────────────────────────────────────────────────────────────────────────┤  │
│  │ OUTPUT TOPICS (Server → ConnectionServer):                                     │  │
│  │   telnet-output, telnet-prompt, websocket-output, websocket-prompt,           │  │
│  │   broadcast, disconnect-connection, gmcp-output, msdp-output, mssp-output,    │  │
│  │   update-player-preferences                                                    │  │
│  └───────────────────────────────────────────────────────────────────────────────┘  │
│                                                                                      │
│  Configuration (MessageQueueOptions):                                               │
│  - Partitions: 3 (for parallelism)                                                  │
│  - Max Message: 6MB                                                                 │
│  - Compression: LZ4                                                                 │
│  - Producer Linger: 8ms (batching window)                                           │
│  - Consumer BatchTimeLimit: 8ms                                                     │
│  - Consumer BatchMaxSize: 100 messages                                              │
└─────────────────────────────────────────────────────────────────────────────────────┘
                    │                                          ▲
                    │                                          │
                    ▼                                          │
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                            SharpMUSH.Server (Main Process)                           │
│                                                                                      │
│  CONSUMERS (from Kafka):                     PRODUCERS (to Kafka):                  │
│  ┌─────────────────────────┐                ┌──────────────────────────┐            │
│  │ TelnetInputConsumer     │ ──┐            │ TelnetOutputMessage      │ ◄──┐       │
│  │ GMCPSignalConsumer      │   │            │ TelnetPromptMessage      │    │       │
│  │ MSDPUpdateConsumer      │   │            │ WebSocketOutputMessage   │    │       │
│  │ NAWSUpdateConsumer      │   │            │ BroadcastMessage         │    │       │
│  │ ConnectionEstablished   │   │            │ DisconnectConnection     │    │       │
│  │ ConnectionClosedConsumer│   │            │ GMCPOutputMessage        │    │       │
│  └─────────────────────────┘   │            └──────────────────────────┘    │       │
│                                │                                            │       │
│                                ▼                                            │       │
│  ┌──────────────────────────────────────────────────────────────────────────┴──┐   │
│  │                         ConnectionService                                    │   │
│  │  - Stores connection state in memory                                         │   │
│  │  - Sets output callbacks that publish to Kafka                               │   │
│  │  - Register(), Bind(), Disconnect(), Get()                                   │   │
│  └──────────────────────────────────────────────────────────────────────────────┘   │
│                                │                                                     │
│                                ▼                                                     │
│  ┌──────────────────────────────────────────────────────────────────────────────┐   │
│  │                          TaskScheduler (Quartz)                               │   │
│  │  - WriteUserCommand() schedules command for parsing                           │   │
│  │  - WriteCommandList() for enqueued commands                                   │   │
│  │  - Manages semaphores, delays, @wait                                          │   │
│  └───────────────────────────────────────────────────────────────────────────────┘   │
│                                │                                                     │
│                                ▼                                                     │
│  ┌──────────────────────────────────────────────────────────────────────────────┐   │
│  │                          MUSHCodeParser                                       │   │
│  │  - CommandParse() → parses and executes commands                              │   │
│  │  - CommandListParse() → parses action lists                                   │   │
│  └───────────────────────────────────────────────────────────────────────────────┘   │
│                                │                                                     │
│                                ▼                                                     │
│  ┌──────────────────────────────────────────────────────────────────────────────┐   │
│  │                          NotifyService                                        │   │
│  │  - Notify() → sends output to players                                         │   │
│  │  - Prompt() → sends prompts                                                   │   │
│  │  - AUTOMATIC BATCHING: accumulates messages for 1ms before flush              │   │
│  │  - Publishes TelnetOutputMessage to Kafka                                     │   │
│  └───────────────────────────────────────────────────────────────────────────────┘   │
│                                                                                      │
│  BACKGROUND SERVICES:                                                               │
│  ┌──────────────────────────────────────────────────────────────────────────────┐   │
│  │ StartupHandler              - Initializes uptime data, configurable aliases  │   │
│  │ ConnectionReconciliationSvc - Rebuilds connection state from Redis on start  │   │
│  │ ConnectionLoggingService    - Logs connection activity                        │   │
│  │ HealthMonitoringService     - Health checks and metrics                       │   │
│  │ ScheduledTaskManagement     - Manages periodic tasks (warnings, purges)       │   │
│  │ WarningCheckService         - Checks object warnings                          │   │
│  │ PennMUSHDatabaseConversion  - Converts PennMUSH databases                     │   │
│  └───────────────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────────────┘
                    │                                          ▲
                    │                                          │
                    ▼                                          │
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                                   Redis                                              │
│                                                                                      │
│  ┌───────────────────────────────────────────────────────────────────────────────┐  │
│  │  RedisConnectionStateStore                                                     │  │
│  │  Keys:                                                                         │  │
│  │    sharpmush:conn:{handle} → ConnectionStateData (JSON)                       │  │
│  │    sharpmush:conn:active   → Set of active handle IDs                         │  │
│  │                                                                                │  │
│  │  Data stored:                                                                  │  │
│  │    - Handle, PlayerRef, State (Connected/LoggedIn)                            │  │
│  │    - IpAddress, Hostname, ConnectionType                                      │  │
│  │    - ConnectedAt, LastSeen                                                    │  │
│  │    - Metadata dictionary                                                       │  │
│  └───────────────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

## Detailed Input Flow (Telnet Client → Game Response)

### Step 1: Client Connects (TCP)
```
Telnet Client ──TCP──▶ Kestrel (TelnetServer ConnectionHandler)
                       │
                       ▼
                 TelnetServer.OnConnectedAsync()
                       │
                       ├─ Generates unique descriptor (handle) via DescriptorGeneratorService
                       ├─ Creates TelnetInterpreter with callbacks
                       └─ Registers with ConnectionServerService
```

### Step 2: Connection Registration
```
TelnetServer
    │
    ├─ Calls: connectionService.RegisterAsync(handle, ipAddress, ...)
    │  └─ Registers OutputFunction and PromptOutputFunction callbacks
    │
    ├─ Publishes: ConnectionEstablishedMessage → Kafka topic "connection-established"
    │
    └─ Stores: Connection data in Redis via IConnectionStateStore
```

### Step 3: Client Sends Input
```
Telnet Client ──TCP──▶ TelnetServer (reading loop)
                       │
                       ├─ telnet.InterpretByteArrayAsync(segment)
                       │    └─ OnSubmit callback fires
                       │
                       └─ Publishes: TelnetInputMessage(handle, input) → Kafka topic "telnet-input"
```

### Step 4: Main Server Receives Input
```
Kafka topic "telnet-input"
    │
    ▼
TelnetInputConsumer.HandleAsync(TelnetInputMessage)
    │
    ├─ Validates non-empty input
    │
    └─ Calls: scheduler.WriteUserCommand(handle, command, state)
```

### Step 5: Command Scheduling
```
TaskScheduler.WriteUserCommand()
    │
    ▼
Quartz Scheduler
    │
    ├─ Creates immediate job with identity "handle:{handle}-{guid}"
    │
    └─ Job executes: parser.FromState(state).CommandParse(handle, connectionService, command)
```

### Step 6: Command Parsing & Execution
```
MUSHCodeParser.CommandParse()
    │
    ├─ Parses command syntax
    ├─ Resolves command handler
    ├─ Validates permissions
    └─ Executes command (e.g., @emit, look, say)
         │
         └─ Command calls NotifyService.Notify() to send output
```

### Step 7: Output Generation (NotifyService)
```
NotifyService.Notify(who, what, sender)
    │
    ├─ Converts MString to text with NormalizeLineEnding (\r\n)
    ├─ Encodes to UTF-8 bytes
    │
    └─ AddToBatch(handle, bytes)
         │
         ├─ Accumulates messages in _batchingStates[handle]
         └─ Starts 1ms timer (NOTE: could be increased to 8ms)
              │
              └─ Timer fires: FlushHandle(handle)
                   │
                   └─ Publishes: TelnetOutputMessage(handle, combined) → Kafka topic "telnet-output"
```

## Detailed Output Flow (Server → Telnet Client)

### Step 1: TelnetOutputMessage Published
```
NotifyService (or other code)
    │
    └─ Publishes: TelnetOutputMessage(handle, data) → Kafka topic "telnet-output"
```

### Step 2: ConnectionServer Consumes & Outputs
```
Kafka topic "telnet-output"
    │
    ▼
TelnetOutputConsumer.HandleAsync(TelnetOutputMessage)
    │
    ├─ Gets connection → connectionService.Get(handle)
    ├─ Transforms data via OutputTransformService (ANSI, encoding, preferences)
    │
    └─ Calls: connection.OutputFunction(transformedData)
               │
               └─ ConnectionServerService.ConnectionData.OutputFunction
                    │
                    ▼
                  TelnetServer's registered outputFunction
                    │
                    └─ _semaphoreSlimForWriter.WaitAsync()
                         │
                         ├─ telnetSafeData = telnet.TelnetSafeBytes(data)
                         │    └─ Escapes IAC (0xFF), applies MCCP compression
                         │
                         └─ connection.Transport.Output.WriteAsync(telnetSafeData)
                              │
                              └─ TCP Write to Client
```

## Batching Summary

### Server-Side Only Batching (NotifyService)

Batching occurs only in `NotifyService` on the main server, reducing Kafka message count:
- Location: SharpMUSH.Server
- Purpose: Combine rapid outputs into single Kafka messages
- Config: 1ms timer, accumulates per-handle
- Output: Combined TelnetOutputMessage → Kafka

The ConnectionServer performs **direct output** - no additional batching layer.
Messages arrive pre-batched from Kafka's 8ms consumer batch window.

### Total Expected Latency
- Kafka Producer Linger: 8ms
- NotifyService Batch: 1ms (configurable)
- Kafka Consumer Batch: 8ms
- **Total: ~17ms typical** (acceptable for interactive use, ~59fps equivalent)

## Potential Failure Points for Missing Telnet Output

### 1. Kafka/Redpanda Issues
- [ ] Broker not running at KAFKA_HOST:9092
- [ ] Topic "telnet-output" not created
- [ ] Consumer group offset issues
- [ ] Message serialization/deserialization errors

### 2. Connection Registration Issues
- [ ] Handle mismatch between ConnectionServer and Server
- [ ] ConnectionEstablishedConsumer not registering output callback
- [ ] OutputFunction not properly captured in closure

### 3. ConnectionService State
- [ ] `Get(handle)` returns null on ConnectionServer side
- [ ] Connection registered but output function is null
- [ ] Connection removed before output sent

### 4. Transport Issues
- [ ] connection.Transport.Output.WriteAsync failing
- [ ] Client disconnected but not detected
- [ ] Telnet negotiation interfering

### Debugging Checklist

1. **Enable Debug Logging** - Check for these log messages:
   - `"TelnetOutputConsumer received output for handle {Handle}"`
   - `"OutputFunction called with {ByteCount} bytes for handle {Handle}"`
   - `"Successfully wrote {ByteCount} telnet-safe bytes"`

2. **Verify Kafka Connectivity**
   - Check if topics exist: `telnet-input`, `telnet-output`
   - Monitor consumer lag
   - Check for producer errors

3. **Verify Connection State**
   - Check Redis keys: `sharpmush:conn:*`
   - Verify handle exists in both ConnectionServer and Server

4. **Test Direct Output**
   - Check if TelnetPromptMessage works (same path as TelnetOutputMessage)
