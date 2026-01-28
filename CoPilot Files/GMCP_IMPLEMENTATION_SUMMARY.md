# WebSocket GMCP Implementation Summary

## Overview
This implementation adds full GMCP (Generic MUD Communication Protocol) support for WebSocket connections, enabling out-of-band (OOB) communication between the server and web-based clients.

## Changes Made

### 1. WebSocket GMCP Protocol Handler
**File**: `SharpMUSH.ConnectionServer/ProtocolHandlers/WebSocketServer.cs`

- Added JSON-based GMCP message parsing
- Implemented `TryParseGMCPMessage` method to detect and parse GMCP messages
- Added GMCP callback function registration for sending messages to clients
- WebSocket connections can now receive and send GMCP messages in JSON format:
  ```json
  {
    "type": "gmcp",
    "package": "Core.Hello",
    "data": "{\"client\":\"WebClient\",\"version\":\"1.0\"}"
  }
  ```

### 2. GMCP Capability Detection
**File**: `SharpMUSH.Server/Consumers/InputMessageConsumers.cs`

- Updated `GMCPSignalConsumer` to set `"GMCP" = "1"` metadata when first GMCP message is received
- This applies to both Telnet and WebSocket connections
- Enables the `oob()` function and `terminfo()` to correctly identify GMCP-capable connections

### 3. Connection Service Updates
**File**: `SharpMUSH.ConnectionServer/Services/ConnectionServerService.cs`

- WebSocket connections can now register a GMCP callback function (optional parameter)
- The callback is used by the GMCPOutputConsumer to send OOB messages to clients

### 4. Documentation
**File**: `CoPilot Files/WEBSOCKET_IMPLEMENTATION.md`

- Added comprehensive GMCP documentation
- Included client examples for JavaScript and .NET
- Documented standard GMCP packages support
- Provided protocol specification

### 5. Tests
**File**: `SharpMUSH.Tests/ConnectionServer/WebSocketGMCPTests.cs`

Added 8 comprehensive tests:
- WebSocket GMCP callback registration
- GMCP metadata setting via GMCPSignalConsumer
- Core.Hello and Core.Supports.Set package handling
- JSON serialization/deserialization
- Invalid JSON handling
- Non-GMCP JSON handling

All tests pass successfully.

## Protocol Specification

### Client to Server (Incoming)
WebSocket clients send GMCP messages as JSON:
```json
{
  "type": "gmcp",
  "package": "Core.Hello",
  "data": {"client": "MyClient", "version": "1.0"}
}
```

The server:
1. Parses the JSON message
2. Extracts the package and data
3. Publishes a `GMCPSignalMessage` to the message bus
4. Sets `GMCP = "1"` metadata on first GMCP message

### Server to Client (Outgoing)
The server sends GMCP messages with data as a JSON object (not a string):
```json
{
  "type": "gmcp",
  "package": "Room.Info",
  "data": {"name": "A Dark Room", "exits": ["north", "south"]}
}
```

Note: The server automatically parses the JSON string from `oob()` and embeds it as an object. This eliminates the need for clients to double-parse the data.

This is triggered by:
1. The `oob()` function in MUSH code
2. Publishing a `SignalGMCPNotification`
3. `TelnetGMCPHandler` publishes `GMCPOutputMessage`
4. `GMCPOutputConsumer` sends via the connection's GMCP callback

## Usage Examples

### JavaScript Client
```javascript
const ws = new WebSocket('ws://localhost:4202/ws');

ws.onopen = () => {
  // Send GMCP Hello
  ws.send(JSON.stringify({
    type: "gmcp",
    package: "Core.Hello",
    data: {client: "WebClient", version: "1.0"}
  }));
};

ws.onmessage = (event) => {
  try {
    const msg = JSON.parse(event.data);
    if (msg.type === 'gmcp') {
      // Data is already an object, no need to parse again
      console.log('GMCP:', msg.package, msg.data);
    }
  } catch {
    console.log('Text:', event.data);
  }
};
```

### MUSH Code
```
think oob(me, Room.Info, json(name, "A Dark Room", exits, json_array(north, south)))
```

### Check GMCP Support
```
think terminfo(me)
# Returns: [..., "gmcp", ...]
```

## Standard GMCP Packages Supported

- **Core.Hello**: Client identification
- **Core.Supports.Set**: Declare supported packages
- **Char.Vitals**: Character health/stats
- **Char.Status**: Character status updates
- **Room.Info**: Room information
- **Comm.Channel**: Communication channels

Custom packages can also be defined and used.

## Benefits

1. **Web Client Support**: Modern web-based MUD/MUSH clients can use GMCP
2. **Unified Protocol**: Same GMCP implementation for both Telnet and WebSocket
3. **Metadata Tracking**: Connections properly advertise GMCP capability
4. **OOB Communication**: Enables rich, structured data exchange
5. **JSON Native**: WebSocket GMCP uses JSON, natural for web clients

## Testing

All 33 WebSocket-related tests pass, including:
- Basic WebSocket connection tests
- GMCP functionality tests
- JSON parsing tests
- Metadata management tests

## Backward Compatibility

- Existing Telnet GMCP functionality unchanged
- WebSocket connections without GMCP continue to work
- OOB() function works with both Telnet and WebSocket
- No breaking changes to existing APIs

## Future Enhancements

Potential improvements:
- GMCP package negotiation
- Version tracking for GMCP packages
- GMCP package documentation in helpfiles
- Client-side JavaScript library for GMCP
- More standard package implementations
