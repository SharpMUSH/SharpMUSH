# Non-Logged-In Connection Behavior

## What Happens When a Player Connects But Hasn't Logged In

When a player connects to the Telnet port but has not yet executed the `connect` command to log in to their character, the protocol-aware output formatting system handles them as follows:

### Connection Establishment

1. **Player Opens Telnet Connection**
   - Client connects to Telnet port
   - `TelnetServer` accepts connection
   - Generates unique connection handle (e.g., `12345`)

2. **ConnectionServer Registration**
   ```csharp
   // ConnectionServerService.RegisterAsync() is called
   var data = new ConnectionData(
       handle: 12345,
       playerRef: null,              // ❌ Not logged in yet
       state: ConnectionState.Connected,
       outputFunction: ...,
       promptOutputFunction: ...,
       encodingFunction: ...,
       disconnectFunction: ...,
       gmcpFunction: null,
       capabilities: new ProtocolCapabilities(),  // ✅ Default capabilities
       preferences: null                          // ❌ No player preferences yet
   );
   ```

3. **Default Protocol Capabilities**
   ```csharp
   new ProtocolCapabilities(
       SupportsAnsi: true,          // ✅ ANSI enabled
       SupportsXterm256: false,     // ❌ 256-color disabled (conservative default)
       SupportsUtf8: true,          // ✅ UTF-8 enabled
       Charset: "UTF-8",
       MaxLineLength: -1
   )
   ```

### Output Transformation for Non-Logged-In Users

When output is sent to a non-logged-in connection (e.g., welcome screen, login prompt):

```csharp
// OutputTransformService.Transform() is called
public byte[] Transform(
    byte[] rawOutput,
    ProtocolCapabilities capabilities,
    PlayerOutputPreferences? preferences)  // ⚠️ preferences = null
{
    var text = Encoding.UTF8.GetString(rawOutput);
    
    // Apply ANSI transformations
    text = ApplyAnsiTransformations(text, capabilities, preferences);
    
    // Convert to target encoding
    var targetEncoding = GetTargetEncoding(capabilities.Charset);
    return targetEncoding.GetBytes(text);
}
```

#### Transformation Logic

```csharp
private string ApplyAnsiTransformations(
    string text,
    ProtocolCapabilities capabilities,
    PlayerOutputPreferences? preferences)
{
    // Check 1: Player preferences for ANSI/COLOR
    if (preferences is { AnsiEnabled: false } or { ColorEnabled: false })
    {
        return StripAnsiCodes(text);  // ❌ Not executed (preferences == null)
    }
    
    // Check 2: Client capability for ANSI
    if (!capabilities.SupportsAnsi)
    {
        return StripAnsiCodes(text);  // ❌ Not executed (SupportsAnsi == true)
    }
    
    // Check 3: XTERM256 support
    if ((preferences != null && !preferences.Xterm256Enabled) || 
        !capabilities.SupportsXterm256)
    {
        return DowngradeXterm256To16Color(text);  // ✅ EXECUTED!
        // Because capabilities.SupportsXterm256 == false
    }
    
    // No transformation needed
    return text;
}
```

### Result: What Gets Displayed

For a non-logged-in connection receiving output like:
```
"\x1b[1;32mWelcome to SharpMUSH!\x1b[0m\nPlease connect.\n\x1b[38;5;196mError\x1b[0m"
```

**Transformations Applied:**
1. ❌ **ANSI stripping**: NOT applied (preferences is null, capabilities.SupportsAnsi is true)
2. ✅ **XTERM256 downgrade**: APPLIED (capabilities.SupportsXterm256 is false)
   - `\x1b[38;5;196m` (256-color red) → `\x1b[31m` (16-color red)
3. ✅ **Encoding**: UTF-8 (default)

**Final Output:**
```
"\x1b[1;32mWelcome to SharpMUSH!\x1b[0m\nPlease connect.\n\x1b[31mError\x1b[0m"
```

### Behavior Summary

| Feature | Non-Logged-In User | Logged-In User |
|---------|-------------------|----------------|
| **ANSI Codes** | ✅ Preserved (colors shown) | Based on ANSI flag |
| **XTERM256** | ⬇️ Downgraded to 16-color | Based on XTERM256 flag |
| **UTF-8** | ✅ Enabled | Based on charset capability |
| **Preferences** | `null` (uses defaults) | Queried from player flags |

### After Login

When the player successfully executes `connect player password`:

1. **Login Success**
   ```csharp
   // SocketCommands.Connect() - line 185
   await SyncPlayerOutputPreferences(handle, player);
   ```

2. **Query Player Flags**
   ```csharp
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

3. **Preferences Updated**
   - `ConnectionData.Preferences` is now set
   - All subsequent output uses player's flag preferences
   - Changes take effect immediately

### Example Timeline

```
Time | Event | Preferences | ANSI Behavior
-----|-------|-------------|---------------
0:00 | Telnet connect | null | ✅ ANSI preserved
0:01 | Receive welcome | null | ✅ ANSI preserved
0:02 | "connect Alice secret" | null | ✅ ANSI preserved
0:03 | Login success | Querying... | ✅ ANSI preserved
0:04 | Flags synced | ANSI=false | ❌ ANSI stripped
0:05 | @set me=ANSI | ANSI=true | ✅ ANSI preserved
```

## Why This Design is Correct

1. **Sensible Defaults**
   - Welcome screens get colors (better UX)
   - Conservative on advanced features (XTERM256 disabled)
   - No crashes when preferences are null

2. **Null-Safe**
   - Code checks `if (preferences is { AnsiEnabled: false })` pattern
   - Only applies preference-based filtering when preferences exist
   - Falls back to capability-based filtering

3. **Immediate Sync**
   - As soon as player logs in, flags are queried
   - Preferences updated before first post-login output
   - Real-time updates when flags change

## Related Code

**Connection Registration:**
- `SharpMUSH.ConnectionServer/Services/ConnectionServerService.cs` (line 20-80)

**Output Transformation:**
- `SharpMUSH.ConnectionServer/Services/OutputTransformService.cs` (line 36-87)

**Login Sync:**
- `SharpMUSH.Implementation/Commands/SocketCommands.cs` (line 185, 377+)

**Flag Change Detection:**
- `SharpMUSH.Server/Handlers/ObjectFlagChangeHandler.cs`
