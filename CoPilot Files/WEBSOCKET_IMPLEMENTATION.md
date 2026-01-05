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
3. **Features**: No GMCP, MSDP, MSSP, or NAWS (window size negotiation)
4. **Message Format**: Text-based (no binary negotiation)

## Security Considerations

1. **CORS**: Configure CORS policies if accessing from different origins
2. **Authentication**: Implement authentication before sending game commands
3. **Rate Limiting**: Consider rate limiting WebSocket connections
4. **WSS**: Use `wss://` (WebSocket Secure) in production with TLS/SSL

## Future Enhancements

Potential improvements:
- JSON-based message protocol for structured data
- Support for WebSocket subprotocols (e.g., GMCP over WebSocket)
- Compression support
- Heartbeat/ping-pong for connection health
- Reconnection with session resumption

## Example Usage

### JavaScript Client
```javascript
const ws = new WebSocket('ws://localhost:4202/ws');

ws.onopen = () => {
    console.log('Connected to SharpMUSH');
    ws.send('connect player password');
};

ws.onmessage = (event) => {
    console.log('Server:', event.data);
};

ws.onerror = (error) => {
    console.error('WebSocket error:', error);
};

ws.onclose = () => {
    console.log('Disconnected from SharpMUSH');
};
```

### .NET Client
```csharp
using var client = new ClientWebSocket();
await client.ConnectAsync(new Uri("ws://localhost:4202/ws"), CancellationToken.None);

// Send a message
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
