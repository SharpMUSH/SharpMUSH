# Color Transformation Based on Capabilities

## Overview

The protocol-aware output formatting system in SharpMUSH transforms colors based on two key factors:
1. **Client Capabilities** - What the client's terminal can support
2. **Player Preferences** - What the player has enabled via flags

This document explains the complete color transformation pipeline and decision-making process.

---

## Transformation Pipeline

```
Raw Output (UTF-8 bytes with ANSI codes)
    ↓
1. Decode to UTF-8 string
    ↓
2. Apply ANSI Transformations (based on capabilities & preferences)
    ↓
3. Apply Charset Transformations
    ↓
4. Encode to target charset bytes
    ↓
Transformed Output (ready for client)
```

---

## Capability Model

### ProtocolCapabilities

Describes what the client's terminal can handle:

```csharp
public record ProtocolCapabilities(
    bool SupportsAnsi = true,        // Basic 16-color ANSI codes (ESC[31m, etc.)
    bool SupportsXterm256 = false,   // 256-color codes (ESC[38;5;Nm)
    bool SupportsUtf8 = true,        // UTF-8 character encoding
    string Charset = "UTF-8",        // Target charset: "UTF-8", "ASCII", "LATIN-1"
    int MaxLineLength = -1           // Maximum line length (-1 = unlimited)
);
```

**Default Values (Conservative):**
- ✅ ANSI enabled (most terminals support basic colors)
- ❌ XTERM256 disabled (requires explicit support)
- ✅ UTF-8 enabled (modern default)

### PlayerOutputPreferences

Describes what the player wants (from MUSH flags):

```csharp
public record PlayerOutputPreferences(
    bool AnsiEnabled = true,         // @set me=ANSI or @set me=!ANSI
    bool ColorEnabled = true,        // @set me=COLOR or @set me=!COLOR
    bool Xterm256Enabled = false     // @set me=XTERM256 or @set me=!XTERM256
);
```

---

## Color Transformation Decision Tree

```
┌─────────────────────────────────────────┐
│ Input: Text with ANSI/XTERM256 codes    │
└──────────────────┬──────────────────────┘
                   │
                   ▼
    ┌──────────────────────────────┐
    │ Check Player Preferences     │
    └──────────┬───────────────────┘
               │
               ├─── AnsiEnabled = false? ──────┐
               │                                │
               ├─── ColorEnabled = false? ─────┤
               │                                ▼
               │                         ┌─────────────┐
               │                         │ Strip ALL   │
               │                         │ ANSI Codes  │
               │                         └─────────────┘
               ▼                                │
    ┌──────────────────────────────┐          │
    │ Check Client Capabilities    │          │
    └──────────┬───────────────────┘          │
               │                               │
               ├─── SupportsAnsi = false? ────┤
               │                               │
               ▼                               │
    ┌──────────────────────────────┐          │
    │ Check XTERM256 Support       │          │
    └──────────┬───────────────────┘          │
               │                               │
               ├─── Xterm256Enabled = false ──┐
               │    (preference)?               │
               │                                │
               ├─── SupportsXterm256 = false ──┤
               │    (capability)?               ▼
               │                         ┌──────────────┐
               │                         │ Downgrade    │
               │                         │ XTERM256 to  │
               │                         │ 16-color     │
               │                         └──────────────┘
               ▼                                │
    ┌──────────────────────────────┐          │
    │ No Transformation Needed     │          │
    │ (Preserve original)          │          │
    └──────────────────────────────┘          │
                   │                            │
                   └────────────────────────────┘
                                │
                                ▼
                   ┌─────────────────────────┐
                   │ Apply Charset Transform  │
                   │ (UTF-8, ASCII, Latin-1)  │
                   └─────────────────────────┘
```

---

## Transformation Logic

### 1. Strip All ANSI Codes

**When:**
- Player preference: `AnsiEnabled = false` OR `ColorEnabled = false`
- OR client capability: `SupportsAnsi = false`

**How:**
Uses regex to remove all ANSI escape sequences:
```csharp
// Pattern: ESC[ followed by parameters and a command letter
Regex: @"\x1b\[[0-9;]*[a-zA-Z]"
```

**Example:**
```
Input:  "\x1b[1;31mRed Bold Text\x1b[0m Normal"
Output: "Red Bold Text Normal"
```

### 2. Downgrade XTERM256 to 16-Color

**When:**
- Player preference: `Xterm256Enabled = false` (but ANSI still enabled)
- OR client capability: `SupportsXterm256 = false` (but `SupportsAnsi = true`)

**How:**
Converts 256-color codes to their closest 16-color equivalent using a color mapping algorithm.

**Example:**
```
Input:  "\x1b[38;5;196mBright Red\x1b[0m"  (256-color #196)
Output: "\x1b[39mBright Red\x1b[0m"        (16-color #9 = bright red)
```

### 3. Preserve Original

**When:**
- All checks pass (ANSI enabled, XTERM256 supported if present)

**Example:**
```
Input:  "\x1b[38;5;196mBright Red\x1b[0m"
Output: "\x1b[38;5;196mBright Red\x1b[0m"  (unchanged)
```

---

## XTERM256 to 16-Color Mapping Algorithm

The 256-color palette is divided into three sections:

### Section 1: Standard Colors (0-15)
**Direct mapping** - These colors already exist in the 16-color palette.

```
0-15 → 0-15 (identity mapping)
```

| 256-Color | 16-Color | Name |
|-----------|----------|------|
| 0 | 0 | Black |
| 1 | 1 | Red |
| 2 | 2 | Green |
| 3 | 3 | Yellow |
| 4 | 4 | Blue |
| 5 | 5 | Magenta |
| 6 | 6 | Cyan |
| 7 | 7 | White |
| 8 | 8 | Bright Black (Gray) |
| 9 | 9 | Bright Red |
| 10 | 10 | Bright Green |
| 11 | 11 | Bright Yellow |
| 12 | 12 | Bright Blue |
| 13 | 13 | Bright Magenta |
| 14 | 14 | Bright Cyan |
| 15 | 15 | Bright White |

### Section 2: Color Cube (16-231)

216-color cube organized as 6×6×6 RGB values.

**Algorithm:**
```csharp
// Extract RGB components (each 0-5)
cubeIndex = color256 - 16
r = (cubeIndex / 36) % 6
g = (cubeIndex / 6) % 6
b = cubeIndex % 6

// Determine brightness
bright = (r + g + b) > 6

// Find dominant color
if (r > g && r > b) → Red (bright ? 9 : 1)
if (g > r && g > b) → Green (bright ? 10 : 2)
if (b > r && b > g) → Blue (bright ? 12 : 4)
if (r == g && r > b) → Yellow (bright ? 11 : 3)
if (r == b && r > g) → Magenta (bright ? 13 : 5)
if (g == b && g > r) → Cyan (bright ? 14 : 6)
else → Grayscale (bright ? 7 : 0)
```

**Example Mappings:**
```
196 (bright red) → 9 (bright red)
  cubeIndex = 180, r=5, g=0, b=0 → bright red

46 (bright green) → 10 (bright green)
  cubeIndex = 30, r=0, g=5, b=0 → bright green

226 (bright yellow) → 11 (bright yellow)
  cubeIndex = 210, r=5, g=5, b=0 → bright yellow
```

### Section 3: Grayscale (232-255)

24 shades of gray from dark to light.

**Algorithm:**
```csharp
gray = color256 - 232  // 0-23

if (gray < 8)  → 0  (Black)
if (gray < 20) → 7  (White)
else           → 15 (Bright White)
```

**Example Mappings:**
```
232 (darkest gray) → 0 (Black)
244 (medium gray) → 7 (White)
255 (lightest gray) → 15 (Bright White)
```

---

## ANSI Code Format Reference

### 16-Color ANSI Codes

**Foreground:**
```
ESC[30m - Black          ESC[90m - Bright Black (Gray)
ESC[31m - Red            ESC[91m - Bright Red
ESC[32m - Green          ESC[92m - Bright Green
ESC[33m - Yellow         ESC[93m - Bright Yellow
ESC[34m - Blue           ESC[94m - Bright Blue
ESC[35m - Magenta        ESC[95m - Bright Magenta
ESC[36m - Cyan           ESC[96m - Bright Cyan
ESC[37m - White          ESC[97m - Bright White
```

**Background:**
```
ESC[40m - Black          ESC[100m - Bright Black
ESC[41m - Red            ESC[101m - Bright Red
ESC[42m - Green          ESC[102m - Bright Green
ESC[43m - Yellow         ESC[103m - Bright Yellow
ESC[44m - Blue           ESC[104m - Bright Blue
ESC[45m - Magenta        ESC[105m - Bright Magenta
ESC[46m - Cyan           ESC[106m - Bright Cyan
ESC[47m - White          ESC[107m - Bright White
```

### 256-Color ANSI Codes

**Format:**
```
Foreground: ESC[38;5;Nm  (where N = 0-255)
Background: ESC[48;5;Nm  (where N = 0-255)
```

**Example:**
```
ESC[38;5;196m - Foreground color #196 (bright red)
ESC[48;5;46m  - Background color #46 (bright green)
```

---

## Real-World Transformation Examples

### Example 1: Player Disables ANSI

**Scenario:**
- Player: `@set me=!ANSI`
- Client: Supports ANSI (doesn't matter)

**Input:**
```
"Welcome to \x1b[1;32mSharpMUSH\x1b[0m!\n\x1b[31mType 'help' for help.\x1b[0m"
```

**Transformation:**
```
ApplyAnsiTransformations():
  preferences.AnsiEnabled = false → Strip all ANSI codes
```

**Output:**
```
"Welcome to SharpMUSH!\nType 'help' for help."
```

### Example 2: Client Doesn't Support 256-Color

**Scenario:**
- Player: ANSI=true, XTERM256=true
- Client: SupportsAnsi=true, SupportsXterm256=false

**Input:**
```
"\x1b[38;5;196mError:\x1b[0m Invalid command"
```

**Transformation:**
```
ApplyAnsiTransformations():
  capabilities.SupportsXterm256 = false → Downgrade XTERM256
  Map 196 → 9 (bright red)
```

**Output:**
```
"\x1b[39mError:\x1b[0m Invalid command"
```

### Example 3: Full 256-Color Support

**Scenario:**
- Player: ANSI=true, XTERM256=true
- Client: SupportsAnsi=true, SupportsXterm256=true

**Input:**
```
"\x1b[38;5;46mSuccess!\x1b[0m \x1b[38;5;196mError!\x1b[0m"
```

**Transformation:**
```
ApplyAnsiTransformations():
  All checks pass → No transformation
```

**Output:**
```
"\x1b[38;5;46mSuccess!\x1b[0m \x1b[38;5;196mError!\x1b[0m"
```

### Example 4: ASCII-Only Client

**Scenario:**
- Player: ANSI=true
- Client: SupportsAnsi=false

**Input:**
```
"\x1b[1;32mGreen Bold\x1b[0m Text with © symbol"
```

**Transformation:**
```
ApplyAnsiTransformations():
  capabilities.SupportsAnsi = false → Strip all ANSI codes

ApplyCharsetTransformations():
  capabilities.Charset = "ASCII" → Convert to ASCII
  
GetTargetEncoding():
  Encoding.ASCII.GetBytes() → © becomes ?
```

**Output:**
```
"Green Bold Text with ? symbol"
```

---

## Behavior Matrix

| Player ANSI | Player COLOR | Player XTERM256 | Client ANSI | Client XTERM256 | Result |
|-------------|--------------|-----------------|-------------|-----------------|--------|
| ❌ | - | - | ✅ | ✅ | Strip all ANSI |
| ✅ | ❌ | - | ✅ | ✅ | Strip all ANSI |
| ✅ | ✅ | - | ❌ | - | Strip all ANSI |
| ✅ | ✅ | ❌ | ✅ | ✅ | Downgrade XTERM256 to 16-color |
| ✅ | ✅ | ✅ | ✅ | ❌ | Downgrade XTERM256 to 16-color |
| ✅ | ✅ | ✅ | ✅ | ✅ | Preserve all (no transformation) |

**Legend:**
- ✅ = Enabled/Supported
- ❌ = Disabled/Not Supported
- `-` = Don't care (any value)

---

## Priority Order

The transformation system checks conditions in this order:

1. **Player Preferences (Highest Priority)**
   - If player disables ANSI or COLOR → strip all ANSI codes
   - If player disables XTERM256 → downgrade to 16-color

2. **Client Capabilities**
   - If client doesn't support ANSI → strip all ANSI codes
   - If client doesn't support XTERM256 → downgrade to 16-color

3. **Default (Lowest Priority)**
   - Preserve original output

**Why this order?**
- Player choice is respected first (they control their experience)
- Client limitations are respected second (technical constraints)
- Default is to preserve quality when possible

---

## Code Reference

**Main transformation logic:**
```csharp
// SharpMUSH.ConnectionServer/Services/OutputTransformService.cs

public byte[] Transform(
    byte[] rawOutput,
    ProtocolCapabilities capabilities,
    PlayerOutputPreferences? preferences)
{
    var text = Encoding.UTF8.GetString(rawOutput);
    
    // Apply ANSI transformations
    text = ApplyAnsiTransformations(text, capabilities, preferences);
    
    // Convert to target charset
    var targetEncoding = GetTargetEncoding(capabilities.Charset);
    return targetEncoding.GetBytes(text);
}
```

**ANSI transformation decision tree:**
```csharp
private string ApplyAnsiTransformations(
    string text,
    ProtocolCapabilities capabilities,
    PlayerOutputPreferences? preferences)
{
    // Priority 1: Player preferences for ANSI/COLOR
    if (preferences is { AnsiEnabled: false } or { ColorEnabled: false })
    {
        return StripAnsiCodes(text);
    }
    
    // Priority 2: Client ANSI capability
    if (!capabilities.SupportsAnsi)
    {
        return StripAnsiCodes(text);
    }
    
    // Priority 3: XTERM256 support
    if ((preferences != null && !preferences.Xterm256Enabled) || 
        !capabilities.SupportsXterm256)
    {
        return DowngradeXterm256To16Color(text);
    }
    
    // No transformation needed
    return text;
}
```

---

## Testing Color Transformations

### Unit Tests

Run the transformation service tests:
```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/OutputTransformServiceTests/*"
```

**Test coverage includes:**
- ANSI preservation when enabled
- ANSI stripping when disabled
- XTERM256 downgrade to 16-color
- Color mapping accuracy
- Encoding conversion

### Manual Testing

**Test ANSI stripping:**
```
# Disable ANSI
@set me=!ANSI
say \x1b[31mThis should have no color\x1b[0m

# Enable ANSI
@set me=ANSI
say \x1b[31mThis should be red\x1b[0m
```

**Test XTERM256 downgrade:**
```
# Disable XTERM256
@set me=!XTERM256
say \x1b[38;5;196mThis should be 16-color red\x1b[0m

# Enable XTERM256
@set me=XTERM256
say \x1b[38;5;196mThis should be 256-color red\x1b[0m
```

---

## Performance Considerations

**Regex Operations:**
- ANSI stripping uses compiled regex (`[GeneratedRegex]`)
- XTERM256 downgrade uses compiled regex
- Minimal performance overhead (<1ms per message)

**Memory:**
- String operations create temporary copies
- Single pass through text for each transformation
- Error handling returns original bytes (no data loss)

**Optimization:**
- Early return when no transformation needed
- Lazy evaluation of transformations
- No transformation when preferences match capabilities

---

## Related Documentation

- **Main Documentation:** `PROTOCOL_OUTPUT_FORMATTING.md`
- **Non-Logged-In Behavior:** `NON_LOGGED_IN_CONNECTION_BEHAVIOR.md`
- **Code Files:**
  - `SharpMUSH.ConnectionServer/Services/OutputTransformService.cs`
  - `SharpMUSH.ConnectionServer/Models/ProtocolCapabilities.cs`
  - `SharpMUSH.ConnectionServer/Models/PlayerOutputPreferences.cs`
