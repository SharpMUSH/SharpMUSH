# Manual Test Verification for Out-of-Order Response Fix

## Issue Being Fixed
When running `@dolist lnum(1,100)=think %i0` via Telnet, responses would arrive out of order with extra newlines.

## Root Cause
Messages from the same connection were using random GUIDs as Kafka partition keys, causing them to be distributed across different partitions and potentially delivered out of order.

## The Fix
Modified `KafkaMessageBus.cs` to use the connection `Handle` (connection ID) as the partition key, ensuring all messages from the same connection go to the same Kafka partition.

## Manual Test Procedure

### Prerequisites
1. Docker and Docker Compose installed
2. .NET 10 SDK installed
3. Telnet client installed

### Steps to Verify

#### 1. Start Infrastructure Services
```bash
cd /home/runner/work/SharpMUSH/SharpMUSH
docker compose up -d redpanda redis arangodb
```

Wait for services to be healthy (about 15-20 seconds):
```bash
docker ps
```

#### 2. Build the Applications
```bash
dotnet build
```

#### 3. Start SharpMUSH.Server
In a new terminal:
```bash
export KAFKA_HOST=localhost
export KAFKA_PORT=9092
export REDIS_CONNECTION=localhost:6379
export ARANGO_CONNECTION_STRING="Server=http://localhost:8529;User=root;Password=password;"
cd SharpMUSH.Server
dotnet run
```

Wait for server to start (look for "Application started" message).

#### 4. Start SharpMUSH.ConnectionServer
In another new terminal:
```bash
export KAFKA_HOST=localhost
export KAFKA_PORT=9092
export REDIS_CONNECTION=localhost:6379
cd SharpMUSH.ConnectionServer
dotnet run
```

Wait for ConnectionServer to start (look for "Listening on port 4201" message).

#### 5. Connect via Telnet
In another terminal:
```bash
telnet localhost 4201
```

#### 6. Connect as Player #1
```
connect #1
```

#### 7. Test the Fix
Run the problematic command:
```
@dolist lnum(1,100)=think %i0
```

### Expected Results

**BEFORE THE FIX:**
- Numbers would appear out of order (e.g., 3, 1, 5, 2, 4, ...)
- Extra newlines between some responses
- Inconsistent delivery order

**AFTER THE FIX:**
- Numbers appear in sequential order: 1, 2, 3, 4, ..., 100
- No extra newlines
- Consistent, ordered delivery

### Verification Method

The test can be automated with:
```bash
(echo "connect #1"; sleep 1; echo "@dolist lnum(1,100)=think %i0"; sleep 5) | telnet localhost 4201 > output.txt
```

Then verify order:
```bash
# Extract just the numbers
grep -oP '^\d+$' output.txt > numbers.txt

# Check if they're in order (should show no differences)
seq 1 100 > expected.txt
diff expected.txt numbers.txt
```

If `diff` shows no output, the numbers are in perfect order = **FIX VERIFIED** ✓

## Why the Fix Works

### Technical Explanation

1. **Kafka Partition Routing**: Kafka uses the message key to determine which partition a message goes to. Messages with the same key are guaranteed to go to the same partition.

2. **Partition Ordering**: Within a single Kafka partition, messages are strictly FIFO (First-In-First-Out) ordered.

3. **The Solution**: By using the connection `Handle` (a unique identifier for each client connection) as the partition key:
   - All messages for connection Handle=42 → Partition X
   - All messages for connection Handle=43 → Partition Y  
   - Within Partition X, messages arrive in the order they were sent

4. **Result**: The 100 messages from `@dolist lnum(1,100)=think %i0` all have the same Handle, so they:
   - All go to the same partition
   - Are guaranteed FIFO ordering
   - Arrive at the client in order 1, 2, 3, ..., 100

### Code Changes

**Before (KafkaMessageBus.cs):**
```csharp
var kafkaMessage = new Message<string, string>
{
    Key = Guid.NewGuid().ToString(), // Random - breaks ordering!
    Value = messageJson
};
```

**After (KafkaMessageBus.cs):**
```csharp
var kafkaMessage = new Message<string, string>
{
    Key = GetPartitionKey(message), // Uses Handle for output messages
    Value = messageJson
};

private static string GetPartitionKey<T>(T message) where T : class
{
    var handleProperty = _handlePropertyCache.GetOrAdd(typeof(T), type =>
    {
        var prop = type.GetProperty("Handle");
        return prop?.PropertyType == typeof(long) ? prop : null;
    });
    
    if (handleProperty != null)
    {
        var handle = (long?)handleProperty.GetValue(message);
        if (handle.HasValue)
        {
            return handle.Value.ToString(); // All messages with same Handle → same partition
        }
    }
    
    return Guid.NewGuid().ToString(); // Fallback for non-Handle messages
}
```

## Cleanup

After testing, stop the services:
```bash
docker compose down
```
