# Protocol-Aware Output Formatting

## Overview

The ConnectionServer now supports protocol-aware output formatting, which transforms game output based on:
1. **Client protocol capabilities** (ANSI support, character encoding)
2. **Player preferences** (ANSI, COLOR, XTERM256 flags)

This ensures that:
- Players with `ANSI` flag OFF receive plain text (no ANSI codes)
- Clients without ANSI support see clean text (no garbage characters like `^[[31m`)
- XTERM256 codes are downgraded to 16-color ANSI for terminals that don't support them
- UTF-8 characters are converted to ASCII (with `?` substitution) for ASCII-only clients

## Architecture

### Components

1. **ProtocolCapabilities** - Stores client protocol capabilities
2. **PlayerOutputPreferences** - Stores player flag preferences
3. **IOutputTransformService** - Interface for output transformation
4. **OutputTransformService** - Implementation of transformation logic
5. **UpdatePlayerPreferencesMessage** - Kafka message for preference updates

### Data Flow

```
MainProcess (NotifyService)
  → Convert to UTF-8 bytes
  → Publish TelnetOutputMessage/WebSocketOutputMessage to Kafka
  → ConnectionServer receives message
  → OutputTransformService.TransformAsync(bytes, capabilities, preferences)
  → Send transformed bytes to OutputFunction
  → Client receives properly formatted output
```

## Models

### ProtocolCapabilities

```csharp
public record ProtocolCapabilities(
    bool SupportsAnsi = true,          // Basic 16-color ANSI
    bool SupportsXterm256 = false,     // 256-color ANSI
    bool SupportsUtf8 = true,          // UTF-8 encoding
    string Charset = "UTF-8",          // "UTF-8", "ASCII", "LATIN-1"
    int MaxLineLength = -1             // -1 = unlimited
);
```

### PlayerOutputPreferences

```csharp
public record PlayerOutputPreferences(
    bool AnsiEnabled = true,      // ANSI flag
    bool ColorEnabled = true,     // COLOR flag
    bool Xterm256Enabled = false  // XTERM256 flag
);
```

## Transformation Rules

### ANSI Stripping

ANSI codes are stripped when:
- `preferences.AnsiEnabled == false`, OR
- `preferences.ColorEnabled == false`, OR
- `capabilities.SupportsAnsi == false`

**Example:**
```
Input:  "\x1b[31mRed text\x1b[0m"
Output: "Red text"
```

### XTERM256 Downgrade

256-color codes are converted to 16-color when:
- `preferences.Xterm256Enabled == false`, OR
- `capabilities.SupportsXterm256 == false`

**Example:**
```
Input:  "\x1b[38;5;196mBright red\x1b[0m"  (256-color)
Output: "\x1b[31mBright red\x1b[0m"        (16-color)
```

**Color Mapping:**
- Colors 0-15: Direct mapping
- Colors 16-231 (color cube): Map to nearest 16-color based on RGB components
- Colors 232-255 (grayscale): Map to black (0) or white (7)

### Encoding Conversion

Output is converted from UTF-8 to target charset:
- `"UTF-8"` → UTF-8 (no conversion)
- `"ASCII"` → ASCII (non-ASCII chars become `?`)
- `"LATIN-1"` or `"ISO-8859-1"` → Latin-1

**Example:**
```
Input (UTF-8):  "Hello © World"
Output (ASCII): "Hello ? World"
```

## Usage

### Updating Client Capabilities

Currently, all connections use default capabilities (ANSI enabled, UTF-8, no XTERM256).

**Future enhancement:** Protocol negotiation will detect capabilities during connection.

### Updating Player Preferences

Send an `UpdatePlayerPreferencesMessage` via Kafka:

```csharp
await messageBus.Publish(new UpdatePlayerPreferencesMessage(
    Handle: connectionHandle,
    AnsiEnabled: false,    // Disable ANSI
    ColorEnabled: true,
    Xterm256Enabled: false
));
```

The ConnectionServer will immediately apply the new preferences to all subsequent output.

### Integration with MainProcess

The MainProcess is fully integrated with the protocol-aware output formatting system:

1. **On player login (SocketCommands.cs):**
   - Queries player flags: `ANSI`, `COLOR`, `XTERM256`
   - Sends `UpdatePlayerPreferencesMessage` to ConnectionServer
   - Applies to both normal and guest login

2. **On flag change (ObjectFlagChangeHandler.cs):**
   - Detects when player uses `@set me=ANSI` or `@set me=!ANSI`
   - Handles all output preference flags: ANSI, COLOR, XTERM256
   - Sends `UpdatePlayerPreferencesMessage` to ConnectionServer
   - Syncs all active connections for the player

Implementation:
```csharp
// After player login (in SocketCommands.cs)
await SyncPlayerOutputPreferences(handle, player);

// Helper method
private static async Task SyncPlayerOutputPreferences(long handle, SharpObject player)
{
    var ansiEnabled = await player.Flags.Value.AnyAsync(f =>
        string.Equals(f.Name, "ANSI", StringComparison.OrdinalIgnoreCase));
    var colorEnabled = await player.Flags.Value.AnyAsync(f =>
        string.Equals(f.Name, "COLOR", StringComparison.OrdinalIgnoreCase));
    var xterm256Enabled = await player.Flags.Value.AnyAsync(f =>
        string.Equals(f.Name, "XTERM256", StringComparison.OrdinalIgnoreCase));
    
    await MessageBus.Publish(new UpdatePlayerPreferencesMessage(
        handle, ansiEnabled, colorEnabled, xterm256Enabled
    ));
}
```

## Testing

### Unit Tests

All transformation logic is tested in `OutputTransformServiceTests`:

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/OutputTransformServiceTests/*"
```

**Test Coverage:**
- ✅ ANSI preservation when enabled
- ✅ ANSI stripping when disabled (preference)
- ✅ ANSI stripping when disabled (capability)
- ✅ COLOR flag disables ANSI
- ✅ XTERM256 downgrade to 16-color
- ✅ XTERM256 preservation when enabled
- ✅ ASCII encoding conversion
- ✅ UTF-8 preservation
- ✅ Complex ANSI sequence handling
- ✅ Error handling (returns original on error)

### Manual Testing

1. **Test ANSI stripping:**
   ```
   @set me=!ANSI
   say \x1b[31mRed text\x1b[0m
   # Should see: "Red text" (no color)
   
   @set me=ANSI
   say \x1b[31mRed text\x1b[0m
   # Should see: Red text (colored)
   ```

2. **Test XTERM256 downgrade:**
   ```
   @set me=!XTERM256
   say \x1b[38;5;196mBright red\x1b[0m
   # Should see: 16-color approximation
   
   @set me=XTERM256
   say \x1b[38;5;196mBright red\x1b[0m
   # Should see: Full 256-color
   ```

## Performance

The transformation service is designed for minimal overhead:

- **Regex-based ANSI stripping:** Efficient pattern matching
- **In-memory transformations:** No I/O operations
- **Encoding conversion:** Uses built-in .NET encoders
- **Error handling:** Returns original output on error (no data loss)

**Target:** <1ms per message (99th percentile)

## Defaults

All defaults preserve existing behavior:

- `ProtocolCapabilities`: ANSI=true, UTF-8=true, XTERM256=false
- `PlayerOutputPreferences`: null (no preferences set)
- **Effect:** Output is sent unchanged (full ANSI, UTF-8)

## Future Enhancements

### Phase 2: Protocol Negotiation

**Telnet:**
- Negotiate CHARSET via IAC DO/WILL
- Detect terminal type (e.g., "xterm-256color")
- Infer ANSI/XTERM256 support from terminal type

**WebSocket:**
- Include capabilities in connection handshake metadata

### Additional Transformations

- **MXP/Pueblo Support:** Detect and include/strip MXP tags
- **Line Length Limits:** Word-wrap long lines intelligently
- **MCCP:** Implement MUD Client Compression Protocol

## Error Handling

The transformation service includes comprehensive error handling:

1. **Invalid UTF-8:** Returns original bytes
2. **Regex failures:** Returns original text
3. **Encoding errors:** Returns original bytes
4. **Unknown charsets:** Defaults to UTF-8

All errors are logged but do not break the connection.

## API Reference

### IOutputTransformService

```csharp
public interface IOutputTransformService
{
    ValueTask<byte[]> TransformAsync(
        byte[] rawOutput,
        ProtocolCapabilities capabilities,
        PlayerOutputPreferences? preferences
    );
}
```

### IConnectionServerService

```csharp
public interface IConnectionServerService
{
    // ... existing methods
    
    bool UpdatePreferences(
        long handle, 
        PlayerOutputPreferences preferences
    );
}
```

## Troubleshooting

### Output shows garbage characters (e.g., `^[[31m`)

**Cause:** Client doesn't support ANSI but preferences indicate it does.

**Solution:** Update client capabilities or set `@set me=!ANSI`.

### Colors don't appear

**Cause:** ANSI or COLOR flag is disabled.

**Solution:** `@set me=ANSI` and `@set me=COLOR`.

### 256-colors look wrong

**Cause:** XTERM256 flag is disabled, causing downgrade to 16-color.

**Solution:** `@set me=XTERM256`.

### Non-ASCII characters show as `?`

**Cause:** Client uses ASCII charset.

**Solution:** Configure client to use UTF-8 encoding (or accept `?` substitution).

## Code Examples

### Custom Transformation

```csharp
var service = new OutputTransformService(logger);

var input = "\x1b[31mRed\x1b[0m"u8.ToArray();
var capabilities = new ProtocolCapabilities(SupportsAnsi: false);
var preferences = null;

var output = await service.TransformAsync(input, capabilities, preferences);
// output = "Red"u8.ToArray()
```

### Updating Preferences

```csharp
var service = serviceProvider.GetRequiredService<IConnectionServerService>();

service.UpdatePreferences(
    connectionHandle,
    new PlayerOutputPreferences(
        AnsiEnabled: false,
        ColorEnabled: false,
        Xterm256Enabled: false
    )
);
```

## Related Files

**ConnectionServer (Output Transformation):**
- `SharpMUSH.ConnectionServer/Models/ProtocolCapabilities.cs`
- `SharpMUSH.ConnectionServer/Models/PlayerOutputPreferences.cs`
- `SharpMUSH.ConnectionServer/Services/IOutputTransformService.cs`
- `SharpMUSH.ConnectionServer/Services/OutputTransformService.cs`
- `SharpMUSH.ConnectionServer/Consumers/UpdatePlayerPreferencesConsumer.cs`
- `SharpMUSH.ConnectionServer/Services/ConnectionServerService.cs`

**MainProcess (Flag Synchronization):**
- `SharpMUSH.Implementation/Commands/Commands.cs` (IMessageBus integration)
- `SharpMUSH.Implementation/Commands/SocketCommands.cs` (SyncPlayerOutputPreferences)
- `SharpMUSH.Server/Handlers/ObjectFlagChangeHandler.cs` (Flag change detection)

**Messages:**
- `SharpMUSH.Messages/OutputMessages.cs` (UpdatePlayerPreferencesMessage)

**Tests:**
- `SharpMUSH.Tests/ConnectionServer/OutputTransformServiceTests.cs`

## Implementation Summary

### What Was Implemented

1. **Data Models:**
   - `ProtocolCapabilities` - Client protocol support (ANSI, XTERM256, UTF-8, charset)
   - `PlayerOutputPreferences` - Player flag preferences (ANSI, COLOR, XTERM256)
   - Extended `ConnectionData` with capabilities and preferences fields

2. **Transformation Service:**
   - `OutputTransformService` - Transforms output based on capabilities + preferences
   - ANSI code stripping (regex-based)
   - XTERM256 to 16-color downgrade
   - Encoding conversion (UTF-8 ↔ ASCII ↔ Latin-1)
   - Error handling (returns original on failure)

3. **ConnectionServer Integration:**
   - All output consumers apply transformations
   - `UpdatePlayerPreferencesConsumer` handles preference updates
   - Thread-safe preference updates with retry loop

4. **MainProcess Integration:**
   - `SyncPlayerOutputPreferences` method queries flags on login
   - `ObjectFlagChangeHandler` detects flag changes in real-time
   - Automatic sync to all active player connections

### How It Works

**Login Flow:**
```
Player connects → Successful login
  → Query ANSI/COLOR/XTERM256 flags
  → Publish UpdatePlayerPreferencesMessage
  → ConnectionServer updates connection preferences
  → All subsequent output transformed based on preferences
```

**Flag Change Flow:**
```
Player executes: @set me=!ANSI
  → ManipulateSharpObjectService.SetOrUnsetFlag
  → Publishes ObjectFlagChangedNotification
  → ObjectFlagChangeHandler receives notification
  → Queries all output preference flags
  → Publishes UpdatePlayerPreferencesMessage for each connection
  → ConnectionServer updates preferences
  → Output immediately transformed based on new preferences
```

**Output Flow:**
```
NotifyService → UTF-8 bytes → Kafka
  → ConnectionServer receives TelnetOutputMessage
  → OutputTransformService.TransformAsync(bytes, capabilities, preferences)
  → ANSI stripping (if disabled)
  → XTERM256 downgrade (if not supported)
  → Encoding conversion (if needed)
  → Client receives properly formatted output
```

### Testing

**Unit Tests:** 11 tests covering all transformation scenarios
- ANSI preservation/stripping
- XTERM256 preservation/downgrade
- Encoding conversion
- Error handling

**Integration:** Real-time flag synchronization on login and flag changes

**Compatibility:** Backward compatible - defaults preserve existing behavior

