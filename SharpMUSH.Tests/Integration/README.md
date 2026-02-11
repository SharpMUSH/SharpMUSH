# Integration Tests

## MessageOrderingIntegrationTests

### What It Tests

Verifies that messages from SharpMUSH maintain perfect sequential ordering through the entire system:
1. Server generates messages via `@dol lnum(1,100)=think %iL`
2. Messages published to Kafka
3. ConnectionServer consumes from Kafka
4. Messages sent to Telnet client via TCP
5. Verifies all 100 messages arrive in order (1, 2, 3, ..., 100)

### Test Architecture

Uses **TUnit test factories** following SharpMUSH test patterns:

- `[ClassDataSource<WebAppFactory>]` - Main server with all infrastructure
- Leverages existing test servers (Redis, Kafka, ArangoDB)
- Marked `[Explicit]` - won't run with other tests
- Shared infrastructure across test session

Pattern matches `HookAndMogrifierIntegrationTests.cs` and other integration tests.

### Prerequisites

1. **Infrastructure running** (docker-compose or standalone):
   ```bash
   docker-compose up -d
   ```

2. **ConnectionServer running separately**:
   ```bash
   dotnet run --project SharpMUSH.ConnectionServer
   ```
   
   Note: ConnectionServer must run separately because its Program class
   is not accessible for WebApplicationFactory pattern.

### Running the Test

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MessageOrderingIntegrationTests/*"
```

### Test Process

1. **Initialization** (via TUnit):
   - WebAppFactory starts main server
   - Shares test infrastructure (Kafka, Redis, ArangoDB)
   - Services available through dependency injection

2. **Execution**:
   - Connects to telnet port 4201 (must be running separately)
   - Handles telnet negotiation
   - Authenticates as wizard
   - Executes: `@dol lnum(1,100)=think %iL`
   - Captures output for up to 15 seconds (2 second idle timeout)

3. **Verification**:
   - Extracts numbers from output using regex
   - Verifies exactly 100 numbers received
   - Verifies sequential order (1, 2, 3, ..., 100)
   - Reports any violations with context

### Expected Results

**If test PASSES**:
```
✓✓✓ SUCCESS: All 100 messages arrived in perfect sequential order (1-100) ✓✓✓
This proves the KafkaFlow implementation maintains message ordering correctly!
```

**If test FAILS**:
```
=== ❌ ORDERING VIOLATIONS (3) ===
Position 7: expected 8, got 9 (context: 6, 7, 9, 10)
Position 8: expected 9, got 10 (context: 7, 9, 10, 11)
Position 82: expected 83, got 82 (context: 81, 82, 82, 84)
```
Provides exact evidence of ordering problems for debugging.

### Troubleshooting

**"Socket error: Connection refused"**:
- ConnectionServer not running
- Start it: `dotnet run --project SharpMUSH.ConnectionServer`
- Wait a few seconds for it to fully initialize

**"Expected 100 messages but got X"**:
- Check ConnectionServer logs
- Verify wizard account exists
- Ensure infrastructure is healthy

**"Ordering violations"**:
- This IS the issue being tested!
- Proves message ordering problem exists
- Use details to investigate root cause (partitions, batching, etc.)

### Why This Approach

✅ Uses established TUnit patterns from SharpMUSH tests
✅ Leverages existing WebAppFactory infrastructure  
✅ Shares test infrastructure (fast, efficient)
✅ Marked Explicit (won't break CI)
✅ Follows same pattern as other integration tests

This provides **objective proof** of ordering behavior through actual end-to-end testing.
