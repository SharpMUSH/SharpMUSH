# WebSocket Connection Endpoint

## Overview

SharpMUSH now supports WebSocket connections in addition to traditional Telnet connections. This allows modern web-based clients to connect directly to the MUSH server using the WebSocket protocol.

## Server Configuration

### Configuration File

The ConnectionServer can be configured via `appsettings.json`:

```json
{
  "ConnectionServer": {
    "TelnetPort": 4201,
    "HttpPort": 4202,
    "TelnetDescriptorStart": 0,
    "WebSocketDescriptorStart": 1000000
  }
}
```

Configuration options:
- **TelnetPort**: Port for Telnet connections (default: 4201)
- **HttpPort**: Port for HTTP/WebSocket connections (default: 4202)
- **TelnetDescriptorStart**: Starting descriptor number for Telnet connections (default: 0)
- **WebSocketDescriptorStart**: Starting descriptor number for WebSocket connections (default: 1000000)

### Environment Variables

Configuration can also be set via environment variables:
- `ConnectionServer__TelnetPort`
- `ConnectionServer__HttpPort`
- `ConnectionServer__TelnetDescriptorStart`
- `ConnectionServer__WebSocketDescriptorStart`

### WebSocket Endpoint

The WebSocket server is available at:
- **Endpoint**: `/ws`
- **Port**: Configured via `HttpPort` (default: 4202)
- **Full URL**: `ws://localhost:4202/ws`

### Connection Flow

1. Client initiates WebSocket connection to `/ws`
2. ConnectionServer accepts the WebSocket handshake
3. A unique connection handle is assigned from the configured descriptor start
4. Connection is registered with the ConnectionService
5. Messages flow through the same Kafka/RedPanda message bus as Telnet connections

## Message Types

### Input Messages
- **WebSocketInputMessage**: Sent from ConnectionServer to MainProcess when client sends input
  - `Handle`: Connection identifier
  - `Input`: Text input from the client

### Output Messages
- **WebSocketOutputMessage**: Sent from MainProcess to ConnectionServer to output text
  - `Handle`: Connection identifier
  - `Data`: String data to send to the client

- **WebSocketPromptMessage**: Sent from MainProcess to ConnectionServer to output a prompt
  - `Handle`: Connection identifier
  - `Data`: String prompt to send to the client

## Client Implementation

### WebSocketClientService

A client service is provided in `SharpMUSH.Client` for managing WebSocket connections:

```csharp
// Inject the service
@inject WebSocketClientService WebSocketClient

// Connect to the server
await WebSocketClient.ConnectAsync("ws://localhost:4202/ws");

// Send a message
await WebSocketClient.SendAsync("look");

// Receive messages via event handler
WebSocketClient.MessageReceived += (sender, message) => 
{
    Console.WriteLine($"Received: {message}");
};

// Disconnect
await WebSocketClient.DisconnectAsync();
```

### Test Page

A test page is available at `/websocket-test` in the SharpMUSH.Client application that demonstrates:
- Connecting to the WebSocket server
- Sending messages
- Receiving messages
- Connection state management

## Architecture

### Protocol Handler

`WebSocketServer` (in `SharpMUSH.ConnectionServer/ProtocolHandlers/`) handles:
- WebSocket connection acceptance
- Message reception from clients
- Publishing input messages to the message bus
- Connection lifecycle management

### Message Consumers

- **WebSocketOutputConsumer**: Processes output messages and sends to WebSocket clients
- **WebSocketPromptConsumer**: Processes prompt messages and sends to WebSocket clients

### Connection Service

WebSocket connections are registered with the same `IConnectionServerService` as Telnet connections, allowing:
- Unified connection management
- Shared Redis state store
- Same message routing infrastructure

## Differences from Telnet

1. **Protocol**: WebSocket instead of Telnet
2. **Encoding**: Always UTF-8 (no Telnet negotiation)
3. **GMCP Support**: WebSocket uses JSON-based GMCP messages (see GMCP section below)
4. **Message Format**: Text-based with optional JSON for GMCP

## GMCP Support

WebSocket connections support GMCP (Generic MUD Communication Protocol) using a JSON message format:

### Sending GMCP from Client to Server

Send a JSON message with the following structure:
```json
{
  "type": "gmcp",
  "package": "Core.Hello",
  "data": "{\"client\":\"MyClient\",\"version\":\"1.0\"}"
}
```

- **type**: Must be "gmcp" to identify this as a GMCP message
- **package**: The GMCP package name (e.g., "Core.Hello", "Char.Vitals")
- **data**: JSON string containing the GMCP data (optional)

### Receiving GMCP from Server to Client

The server sends GMCP messages in the same format:
```json
{
  "type": "gmcp",
  "package": "Room.Info",
  "data": "{\"name\":\"A Dark Room\",\"exits\":[\"north\",\"south\"]}"
}
```

### Using OOB() Function

Once a WebSocket client sends a GMCP message, the connection is marked as GMCP-capable and can receive OOB messages:

```javascript
// In MUSH code
think oob(me, Room.Info, json(name, "A Dark Room", exits, json_array(north, south)))

// Client receives:
{
  "type": "gmcp",
  "package": "Room.Info", 
  "data": {"name": "A Dark Room", "exits": ["north", "south"]}
}
```

Note: The `data` field is sent as a JSON object, not a stringified JSON string. This makes it easier for WebSocket clients to work with the data directly without double-parsing.

### Standard GMCP Packages

WebSocket clients can use any standard GMCP packages:
- **Core.Hello**: Client identification
- **Core.Supports.Set**: Declare supported packages
- **Char.Vitals**: Character health/stats
- **Char.Status**: Character status updates
- **Room.Info**: Room information
- **Comm.Channel**: Communication channels

Example client initialization:
```javascript
ws.send(JSON.stringify({
  type: "gmcp",
  package: "Core.Hello",
  data: {client: "WebClient", version: "1.0"}
}));

ws.send(JSON.stringify({
  type: "gmcp",
  package: "Core.Supports.Set",
  data: ["Core 1", "Char 1", "Room 1", "Comm 1"]
}));
```

## Security Considerations

1. **CORS**: Configure CORS policies if accessing from different origins
2. **Authentication**: Implement authentication before sending game commands
3. **Rate Limiting**: Consider rate limiting WebSocket connections
4. **WSS**: Use `wss://` (WebSocket Secure) in production with TLS/SSL
5. **JSON Validation**: GMCP messages are validated before processing

## Future Enhancements

Potential improvements:
- Compression support
- Heartbeat/ping-pong for connection health
- Reconnection with session resumption
- Binary message support

## Example Usage

### JavaScript Client
```javascript
const ws = new WebSocket('ws://localhost:4202/ws');

ws.onopen = () => {
    console.log('Connected to SharpMUSH');
    
    // Send GMCP Hello
    ws.send(JSON.stringify({
        type: "gmcp",
        package: "Core.Hello",
        data: {client: "WebClient", version: "1.0"}
    }));
    
    // Or send regular commands
    ws.send('connect player password');
};

ws.onmessage = (event) => {
    try {
        // Try to parse as JSON (GMCP message)
        const msg = JSON.parse(event.data);
        if (msg.type === 'gmcp') {
            // Data is already an object, no need to parse again
            console.log('GMCP:', msg.package, msg.data);
            return;
        }
    } catch {
        // Not JSON, treat as regular text
        console.log('Server:', event.data);
    }
};

ws.onerror = (error) => {
    console.error('WebSocket error:', error);
};

ws.onclose = () => {
    console.log('Disconnected from SharpMUSH');
};
};
```

### .NET Client
```csharp
using var client = new ClientWebSocket();
await client.ConnectAsync(new Uri("ws://localhost:4202/ws"), CancellationToken.None);

// Send GMCP Hello
var gmcpHello = new
{
    type = "gmcp",
    package = "Core.Hello",
    data = new { client = "DotNetClient", version = "1.0" }
};
var helloBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(gmcpHello));
await client.SendAsync(helloBytes, WebSocketMessageType.Text, true, CancellationToken.None);

// Send a regular command
var bytes = Encoding.UTF8.GetBytes("look");
await client.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

// Receive messages
var buffer = new byte[1024];
while (client.State == WebSocketState.Open)
{
    var result = await client.ReceiveAsync(buffer, CancellationToken.None);
    if (result.MessageType == WebSocketMessageType.Text)
    {
        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
        
        // Try to parse as GMCP
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (doc.RootElement.TryGetProperty("type", out var type) && 
                type.GetString() == "gmcp")
            {
                var package = doc.RootElement.GetProperty("package").GetString();
                // Data is already an object in the JSON, no need to parse again
                var data = doc.RootElement.GetProperty("data");
                Console.WriteLine($"GMCP {package}: {data}");
                continue;
            }
        }
        catch { }
        
        // Regular text message
        Console.WriteLine($"Received: {message}");
    }
}
```

## Testing

Run the tests:
```bash
dotnet test --filter "FullyQualifiedName~WebSocketConsumer"
```

Test manually:
1. Start ConnectionServer: `dotnet run --project SharpMUSH.ConnectionServer`
2. Open browser to: `http://localhost:4202/websocket-test`
3. Click "Connect" to establish WebSocket connection
4. Send messages and observe responses
