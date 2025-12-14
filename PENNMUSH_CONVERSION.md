# PennMUSH Database Converter

## Overview

This document describes the PennMUSH database conversion system implemented in SharpMUSH. The converter allows importing existing PennMUSH databases into SharpMUSH format.

## Architecture

The conversion system consists of several key components:

### 1. PennMUSH Data Models

Located in `SharpMUSH.Library/Services/DatabaseConversion/`:

- **PennMUSHObject.cs**: Represents a single object from a PennMUSH database with all its properties (name, location, flags, attributes, etc.)
- **PennMUSHAttribute.cs**: Represents an attribute on a PennMUSH object
- **PennMUSHDatabase.cs**: Container for an entire parsed PennMUSH database

### 2. Database Parser

**PennMUSHDatabaseParser.cs** reads the text-based PennMUSH database format and converts it into in-memory `PennMUSHDatabase` objects.

The parser handles:
- Database header and version information
- Object records with all properties
- Multi-line attributes
- Locks and permissions
- Flags and powers

### 3. Database Converter

**PennMUSHDatabaseConverter.cs** implements `IPennMUSHDatabaseConverter` and converts parsed PennMUSH objects into SharpMUSH objects by:

1. **First Pass**: Creating all objects (players, rooms, things, exits) without relationships
2. **Second Pass**: Establishing relationships (locations, homes, exits, parent/zone relationships)
3. **Third Pass**: Creating attributes on objects
4. **Fourth Pass**: Setting up locks

The converter returns a `ConversionResult` with statistics and any errors/warnings encountered.

#### ANSI and Pueblo Escape Sequence Handling

During attribute conversion, the converter strips raw ANSI escape sequences and HTML tags from attribute values. PennMUSH stores these as literal escape sequences in the database file.

**Current behavior:**
- **ANSI CSI sequences** are removed (e.g., `ESC[31m` for red text, `ESC[1m` for bold, `ESC[0m` for reset)
- **ANSI 256-color codes** are removed (e.g., `ESC[38;5;196m` for foreground color)
- **ANSI RGB color codes** are removed (e.g., `ESC[38;2;255;0;0m` for RGB foreground)
- **ANSI OSC sequences** are removed (operating system commands)
- **HTML/Pueblo tags** are removed (e.g., `<b>`, `</b>`, `<color red>`, `</color>`)
- Raw text content is preserved

**Future enhancement (TODO):**
The converter should be enhanced to convert these escape sequences to SharpMUSH's native MarkupString format, preserving the intended formatting:
- Parse ANSI SGR (Select Graphic Rendition) codes and map to MarkupString colors/styles
- Convert ANSI 256-color palette codes to MarkupString colors
- Convert ANSI RGB color codes to MarkupString colors
- Map text styles (bold, underline, inverse, etc.) to MarkupString formatting
- Convert Pueblo HTML tags to equivalent MarkupString formatting

### 4. Background Service

**PennMUSHDatabaseConversionService.cs** is a background service that runs on server startup and performs database conversion if configured.

## Configuration

The converter is controlled via environment variables:

- **PENNMUSH_DATABASE_PATH**: Path to the PennMUSH database file to convert. If not set, conversion is skipped.
- **PENNMUSH_CONVERSION_STOP_ON_FAILURE**: If set to "true", the application will stop if conversion fails.

## Usage

### Automatic Conversion on Startup

Set the environment variables before starting the server:

```bash
export PENNMUSH_DATABASE_PATH="/path/to/pennmush.db"
export PENNMUSH_CONVERSION_STOP_ON_FAILURE="true"  # Optional
dotnet run --project SharpMUSH.Server
```

The conversion will run automatically when the server starts.

### Programmatic Usage

```csharp
// Inject the converter service
var converter = serviceProvider.GetRequiredService<IPennMUSHDatabaseConverter>();

// Convert a database file
var result = await converter.ConvertDatabaseAsync("/path/to/pennmush.db");

if (result.IsSuccessful)
{
    Console.WriteLine($"Converted {result.TotalObjects} objects in {result.Duration}");
    Console.WriteLine($"  Players: {result.PlayersConverted}");
    Console.WriteLine($"  Rooms: {result.RoomsConverted}");
    Console.WriteLine($"  Things: {result.ThingsConverted}");
    Console.WriteLine($"  Exits: {result.ExitsConverted}");
}
else
{
    Console.WriteLine($"Conversion failed with {result.Errors.Count} errors");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"  - {error}");
    }
}
```

## PennMUSH Database Format

PennMUSH databases are text-based files with a specific structure:

### Header
```
V:<version>
+FLAGS|<flag definitions>
+POWERS|<power definitions>
```

### Object Record Format
```
!<dbref>
<name>
<location>
<contents>
<exits>
<link>
<next>
<lock>
<owner>
<parent>
<pennies>
<flags>
<powers>
<warnings>
<creation_time>
<modification_time>
<attribute1_header>
<attribute1_value>
<attribute2_header>
<attribute2_value>
...
```

### Object Types
- Type 0: Room
- Type 1: Thing
- Type 2: Exit
- Type 3: Player

### Attribute Format
```
<name>^owner^flags^deref_count
<value line 1>
<value line 2>
...
```

### Password Format (for players)
```
V:ALGO:HASH:TIMESTAMP
```
Example: `2:SHA1:abXYZ123...:1234567890`

Where:
- V: Version (usually 2)
- ALGO: Hash algorithm (SHA1 for PennMUSH)
- HASH: Salted hash (first 2 chars are salt)
- TIMESTAMP: Unix timestamp when password was set

## Current Implementation Status

### Implemented
- ✅ PennMUSH database parser
- ✅ Data models for PennMUSH objects
- ✅ Converter service interface
- ✅ Complete converter implementation
- ✅ Background service for automatic conversion
- ✅ Service registration in Startup.cs
- ✅ Conversion result reporting
- ✅ Object creation (players, rooms, things, exits)
- ✅ Relationship establishment (locations, exit destinations)
- ✅ Attribute creation with flags
- ✅ Lock creation
- ✅ ANSI and Pueblo escape sequence stripping from attributes

### To Do
- ⏳ Convert ANSI escape sequences to MarkupStrings (currently stripped)
- ⏳ Handle home location setting for players/things
- ⏳ Handle parent relationships
- ⏳ Handle zone relationships
- ⏳ Handle ownership transfer after initial creation
- ⏳ Handle password migration (SHA1 to modern hashing)
- ⏳ Handle flags and powers mapping
- ⏳ Add unit tests
- ⏳ Add integration tests with sample databases
- ⏳ Add progress reporting during long conversions
- ⏳ Add validation and error recovery

## Future Enhancements

1. **Incremental Conversion**: Support converting only changed objects
2. **Dry Run Mode**: Preview conversion without making changes
3. **Conversion Reports**: Generate detailed reports of what was converted
4. **Validation**: Pre-conversion validation of database integrity
5. **Rollback**: Ability to rollback a failed conversion
6. **TinyMUX Support**: Extend to support TinyMUX database format as well
7. **Migration Tools**: Tools to help fix common issues during migration
8. **GUI**: Web-based interface for managing conversions

## References

- [PennMUSH Documentation](http://download.pennmush.org/)
- [PennMUSH GitHub](https://github.com/pennmush/pennmush)
- [TinyMUX Omega Converter](https://github.com/brazilofmux/tinymux/tree/master/convert) - Reference implementation

## See Also

- `SharpMUSH.Library/Services/DatabaseConversion/` - Conversion code
- `SharpMUSH.Server/Services/PennMUSHDatabaseConversionService.cs` - Background service
- `SharpMUSH.Server/Startup.cs` - Service registration
