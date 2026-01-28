# Telnet GMCP (Out-of-Band) Implementation

## Overview

This document describes the implementation of OOB (Out-of-Band) communication in SharpMUSH using GMCP (Generic MUD Communication Protocol) for **Telnet connections only**.

**Important**: GMCP is a Telnet-specific protocol extension. WebSocket connections use different mechanisms (`wsjson()` and `wshtml()` functions) for OOB communication.

## GMCP Protocol

GMCP is a Telnet protocol extension that allows MUD servers and clients to exchange structured data outside of the normal text stream. It's defined in the Telnet protocol using subnegotiation.

### Telnet GMCP Flow

1. **Negotiation**: Client and server negotiate GMCP support via Telnet protocol
2. **Connection**: TelnetNegotiationCore library handles the GMCP protocol
3. **Metadata**: First GMCP message sets `GMCP = "1"` in connection metadata
4. **Communication**: Data flows via GMCPSignalMessage and GMCPOutputMessage

## Architecture

### Components

#### TelnetServer (ConnectionServer)
- Uses TelnetNegotiationCore library's GMCPProtocol plugin
- Registers GMCP callback that publishes GMCPSignalMessage
- Located in: `SharpMUSH.ConnectionServer/ProtocolHandlers/TelnetServer.cs`

```csharp
.AddPlugin<GMCPProtocol>().OnGMCPMessage(async data =>
    await _publishEndpoint.Publish(new GMCPSignalMessage(nextPort, data.Package, data.Info), ct))
```

#### GMCPSignalConsumer (Server)
- Consumes GMCPSignalMessage from Kafka/RedPanda
- Sets `GMCP = "1"` metadata on first GMCP message
- Stores GMCP packages in connection metadata
- Located in: `SharpMUSH.Server/Consumers/InputMessageConsumers.cs`

```csharp
public Task HandleAsync(GMCPSignalMessage message, CancellationToken cancellationToken = default)
{
    // Set GMCP capability flag on first GMCP message
    connectionService.Update(message.Handle, "GMCP", "1");
    
    // Store GMCP package and info in connection metadata
    connectionService.Update(message.Handle, $"GMCP_{message.Package}", message.Info);
    
    // Handle specific GMCP packages
    HandleGMCPPackage(message.Handle, message.Package, message.Info);
    
    return Task.CompletedTask;
}
```

#### oob() Function (Server)
- MUSH function that sends OOB data to players
- Checks for `GMCP = "1"` metadata to identify GMCP-capable connections
- Publishes SignalGMCPNotification for each GMCP connection
- Located in: `SharpMUSH.Implementation/Functions/JSONFunctions.cs`

```csharp
await foreach (var connection in ConnectionService!.Get(located.Object().DBRef))
{
    if (connection.Metadata.GetValueOrDefault("GMCP", "0") != "1")
    {
        continue;
    }

    await Mediator!.Publish(new SignalGMCPNotification(
        connection.Handle,
        package,
        message));
    
    sentCount++;
}
```

#### TelnetGMCPHandler (Server)
- Handles SignalGMCPNotification
- Publishes GMCPOutputMessage to send to client
- Located in: `SharpMUSH.Implementation/Handlers/Telnet/TelnetGMCPHandler.cs`

```csharp
public async ValueTask Handle(SignalGMCPNotification notification, CancellationToken cancellationToken)
{
    // Publish GMCP output message to ConnectionServer for delivery to the client
    await publisher.Publish(
        new GMCPOutputMessage(notification.handle, notification.Module, notification.Writeback),
        cancellationToken);
}
```

#### GMCPOutputConsumer (ConnectionServer)
- Consumes GMCPOutputMessage from Kafka/RedPanda
- Calls connection's GMCPFunction to send to Telnet client
- Located in: `SharpMUSH.ConnectionServer/Consumers/GMCPOutputConsumer.cs`

```csharp
public async Task HandleAsync(GMCPOutputMessage message, CancellationToken cancellationToken = default)
{
    var connection = connectionService.Get(message.Handle);
    
    if (connection?.GMCPFunction == null)
    {
        logger.LogWarning("Connection {Handle} does not support GMCP", message.Handle);
        return;
    }

    await connection.GMCPFunction(message.Module, message.Message);
}
```

### Message Flow

#### Client to Server (Incoming)
```
Telnet Client
  ↓ GMCP data via Telnet protocol
TelnetServer (ConnectionServer)
  ↓ GMCPSignalMessage via Kafka
GMCPSignalConsumer (Server)
  → Sets GMCP = "1" metadata
  → Stores package data
```

#### Server to Client (Outgoing)
```
oob() function
  ↓ SignalGMCPNotification via Mediator
TelnetGMCPHandler (Server)
  ↓ GMCPOutputMessage via Kafka
GMCPOutputConsumer (ConnectionServer)
  ↓ GMCPFunction callback
Telnet Client receives GMCP data
```

## Usage

### MUSH Code

Send OOB data to a player:
```
think oob(me, Room.Info, json(name, "Dark Room", exits, json_array(north, south)))
```

Returns the number of connections that received the message.

### Standard GMCP Packages

- **Core.Hello**: Client identification
- **Core.Supports.Set**: Declare supported packages
- **Char.Vitals**: Character health/stats
- **Char.Status**: Character status updates
- **Room.Info**: Room information
- **Comm.Channel**: Communication channels

### Checking GMCP Support

Use terminfo() to check if a connection supports GMCP:
```
think terminfo(me)
```

Returns a list including "gmcp" if the connection supports it.

## WebSocket vs Telnet

**Telnet OOB Communication:**
- Uses GMCP protocol (Telnet extension)
- Metadata: `GMCP = "1"`
- Function: `oob(player, package, json_data)`
- Protocol-level support via TelnetNegotiationCore

**WebSocket OOB Communication:**
- Uses `wsjson()` and `wshtml()` functions
- Separate from GMCP protocol
- Currently placeholder implementation
- Will use WebSocketOutputMessage when implemented

## Testing

GMCP functionality is tested in:
- `SharpMUSH.Tests/Functions/JsonFunctionUnitTests.cs` - Tests oob() function
- Connection metadata tests verify "GMCP" flag is set correctly

## Security

- Permission checks: Users need wizard status, Send_OOB power, or must target themselves
- JSON validation: Message parameter must be valid JSON
- Connection validation: Only sends to GMCP-capable connections

## References

- TelnetNegotiationCore library: Handles GMCP protocol at Telnet level
- GMCP Specification: http://www.gammon.com.au/gmcp
- PennMUSH Compatibility: oob() function matches PennMUSH behavior
