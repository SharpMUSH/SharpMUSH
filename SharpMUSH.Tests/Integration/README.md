# Integration Tests

## MessageOrderingIntegrationTests

This test provides **definitive proof** of message ordering behavior in the SharpMUSH system.

### What It Tests

The test verifies that when executing `@dol lnum(1,100)=think %iL`, all 100 messages arrive at the telnet client in perfect sequential order (1, 2, 3, ..., 100).

This tests the **entire message flow**:
1. Command execution in SharpMUSH.Server
2. Message production to Kafka via KafkaFlow
3. Message consumption from Kafka in SharpMUSH.ConnectionServer
4. Batch processing via TelnetOutputBatchMiddleware
5. TCP delivery to telnet client

### Prerequisites

Before running the test, you must:

1. **Start Infrastructure**:
   ```bash
   docker-compose up -d
   ```
   This starts Kafka/Redpanda, Redis, and ArangoDB.

2. **Build the Solution**:
   ```bash
   dotnet build
   ```
   This builds SharpMUSH.Server and SharpMUSH.ConnectionServer executables.

### Running the Test

The test is marked `[Explicit]` so it **does NOT run** with the regular test suite. You must run it explicitly:

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MessageOrderingIntegrationTests/*"
```

### Test Process

1. **Setup** (10 seconds):
   - Starts SharpMUSH.Server process
   - Starts SharpMUSH.ConnectionServer process
   - Waits for servers to initialize

2. **Execution**:
   - Connects to telnet port (4201)
   - Authenticates as wizard
   - Executes `@dol lnum(1,100)=think %iL`
   - Captures output for up to 15 seconds

3. **Verification**:
   - Extracts numbers from output
   - Verifies exactly 100 messages received
   - Verifies sequential order (1-100)

4. **Cleanup**:
   - Kills both server processes

### Expected Results

**✅ SUCCESS**: All 100 messages arrive in perfect order
```
✅✅✅ SUCCESS! All 100 messages received in perfect order 1-100!
This proves the KafkaFlow implementation maintains message ordering correctly.
```

**❌ FAILURE**: Messages arrive out of order
```
❌ Found N ordering violations:
  - Position X: Expected Y, got Z. Context: [...]
```

### Troubleshooting

**"Could not find solution directory"**
- The test must be run from within the SharpMUSH solution directory tree

**"SharpMUSH.Server not found at: ..."**
- Run `dotnet build` to build the servers first

**"Failed to connect to localhost:4201"**
- Infrastructure might not be running: `docker-compose up -d`
- Servers might not have started yet: increase `StartupDelayMs` in test code
- Check server logs for errors

**"Expected 100 messages but got X"**
- Some messages were lost or not captured
- Increase capture timeout in test code
- Check Kafka/Redis/ArangoDB are running and accessible

**Messages out of order**
- This is the actual issue the test is designed to detect
- The test output will show exact violations with context
- This proves there is a message ordering problem in the system

### Why This Test Matters

This test provides **objective, reproducible proof** of message ordering behavior:
- No assumptions
- No speculation
- No relying on documentation
- Actual end-to-end behavior testing

If this test **PASSES**, the system works correctly.
If this test **FAILS**, we have definitive proof of the issue with exact details.
