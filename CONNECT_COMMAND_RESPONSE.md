# Connect Command Response Documentation

## Question
What is the actual plain string response when a user types `connect #1` via telnet?

## Answer
**`"Connected!"`**

This is the ONLY guaranteed response. Additional output may appear based on server configuration (see "Additional Output" section below).

## Evidence

### 1. Source Code Implementation

**File**: `SharpMUSH.Implementation/Commands/SocketCommands.cs`

The CONNECT command implementation (lines 66-186) shows:

```csharp
[SharpCommand(Name = "CONNECT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
    MaxArgs = 2, ParameterNames = ["player", "password"])]
public static async ValueTask<Option<CallState>> Connect(IMUSHCodeParser parser, SharpCommandAttribute _2)
{
    // ... validation and authentication logic ...
    
    // Line 183: Successful connection response
    await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Connected!");
    Logger?.LogDebug("Successful login and binding for {@person}", foundDB.Object);
    return new CallState(playerDbRef);
}
```

### 2. Guest Login Implementation

**File**: `SharpMUSH.Implementation/Commands/SocketCommands.cs`

The HandleGuestLogin method (line 345) also returns the same message:

```csharp
await NotifyService!.Notify(handle, "Connected!");
```

### 3. Test Verification

**File**: `SharpMUSH.Tests/Commands/GuestLoginTests.cs`

Multiple tests verify the "Connected!" response (lines 90-94, 132-135, 171-174):

```csharp
// Should receive "Connected!" message
await NotifyService
    .Received()
    .Notify(Arg.Is<long>(h => h == guestHandle), 
        Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Connected")));
```

### 4. Integration Test Example

**File**: `SharpMUSH.Tests/Performance/ActualPerformanceValidation.cs`

Shows the expected workflow (lines 38-42):

```csharp
// Login as #1
await writer.WriteLineAsync("connect #1");
var loginResponse = await ReadUntilPrompt(reader);
Console.WriteLine("Login response:");
Console.WriteLine(loginResponse);
```

## Player #1 Details

**File**: `SharpMUSH.Database.ArangoDB/Migrations/Migration_CreateDatabase.cs`

Player #1 is created during database initialization (lines 913-938):

```csharp
/* Create Player One */
var playerOneObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.Objects, new
{
    _key = 1.ToString(),
    Name = "God",
    Type = DatabaseConstants.TypePlayer,
    CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
});

var playerOnePlayer = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.Players, new
{
    Aliases = Array.Empty<string>(),
    PasswordHash = string.Empty,  // No password required
    Quota = 999999 // God has unlimited quota
});
```

**Key Details**:
- **DBRef**: `#1`
- **Name**: `"God"`
- **Password**: Empty (no password required)
- **Command**: `connect #1` (without password works because PasswordHash is empty)

## Error Responses

If connection fails, the command returns different messages:

**File**: `SharpMUSH.Implementation/Commands/SocketCommands.cs`

```csharp
// Already connected (line 73)
await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Huh?  (Type \"help\" for help.)");

// Player not found (line 135)
await NotifyService!.Notify(handle, "Could not find that player.");

// Invalid password (line 157)
await NotifyService!.Notify(handle, "Invalid Password.");
```

## Complete Flow

1. User connects via telnet to port 4201
2. User types: `connect #1`
3. ConnectionServer receives the command
4. Command is published to Kafka (`telnet-input` topic)
5. Server processes the command via `SocketCommands.Connect()`
6. Server validates:
   - User is not already connected
   - Player #1 exists
   - Password is valid (empty password matches empty PasswordHash)
7. Server binds the connection handle to DBRef #1
8. **Server sends: `"Connected!"`** (via NotifyService → Kafka → ConnectionServer → telnet)
9. Server triggers `PLAYER\`CONNECT` event (may execute additional MUSH code)
10. If configured, server may display Connect MOTD or execute event handler code

## Running Without Kafka

**Note**: The system requires Kafka for ConnectionServer ↔ Server communication. However, unit tests can run without full Kafka setup by using:

- **Test Infrastructure**: `WebAppFactory.cs` creates isolated test environment
- **Test Containers**: Uses Docker containers for Redpanda (Kafka-compatible) automatically
  - `RedPandaTestServer` starts a containerized Redpanda instance
  - Configured with dynamic port mapping to avoid conflicts
  - Automatically creates required Kafka topics: `telnet-input`, `telnet-output`, `telnet-prompt`, etc.
- **Mock Services**: Tests use `NSubstitute` to mock `INotifyService`
- **In-Memory Testing**: Commands can be tested directly without telnet/Kafka layers

**File**: `SharpMUSH.Tests/WebAppFactory.cs` (lines 197-246)

The test infrastructure creates Kafka topics automatically and handles failures gracefully.

### Running Tests

Tests automatically spin up all required infrastructure via Testcontainers:

```bash
# Run all tests (automatically starts ArangoDB, Redpanda, MySQL, Redis, Prometheus)
dotnet test

# Or run tests directly
cd SharpMUSH.Tests
dotnet run

# Run specific test class
dotnet run -- --treenode-filter "/*/*/GuestLoginTests/*"

# Run specific test method
dotnet run -- --treenode-filter "/*/*/GuestLoginTests/ConnectGuest_BasicLogin_Succeeds"
```

**Infrastructure Started Automatically**:
- ArangoDB (database)
- Redpanda (Kafka-compatible messaging)
- MySQL (SQL database)
- Redis (connection tracking)
- Prometheus (metrics)

All containers are started via Testcontainers.NET and torn down after tests complete.

## Summary

**The telnet response to `connect #1` is:**

```
Connected!
```

This is a plain ASCII string followed by a newline, sent via the telnet connection after successful authentication and binding.

### Additional Output (Optional)

After "Connected!", the server may send additional output depending on configuration:

1. **Event Handler Code** - If `event_handler` is configured and has a `PLAYER\`CONNECT` attribute:
   - The attribute code is executed with arguments: `%0`=objid, `%1`=connection count, `%2`=descriptor
   - Any output from this code is sent to the player

2. **Connect MOTD** - If configured via `@motd/connect`, this message is displayed
   - Can be set with: `@motd/connect <message>`
   - Retrieved via: `motd()` function
   - Cleared with: `@motd/connect/clear`

**Example Event Handler**:
```
@create Event Handler
@config/set event_handler=[num(Event Handler)]
&PLAYER`CONNECT Event Handler=@pemit %#=Welcome back, [name(%0)]! This is connection #%1.
```

This would display:
```
Connected!
Welcome back, God! This is connection #1.
```

**References**:
- Event System: `SharpMUSH.Library/Services/EventService.cs`
- Connect MOTD: `SharpMUSH.Library/ExpandedObjectData/MotdData.cs`
- Event Handler Interface: `SharpMUSH.Library/Services/Interfaces/IEventService.cs`
