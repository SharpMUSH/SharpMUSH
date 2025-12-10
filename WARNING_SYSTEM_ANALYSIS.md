# PennMUSH Warning System - Complete Analysis

## Executive Summary

The warning system in PennMUSH is a comprehensive topology and integrity checking system that helps builders maintain quality in their MUSH databases. It identifies potential issues with objects, rooms, exits, things, and players through configurable warning checks.

## Warning Types and Bitmask Structure

PennMUSH uses a `uint32_t` bitmask to store warning preferences. Each warning type has a specific bit value:

### Individual Warning Flags

| Flag | Value | Description |
|------|-------|-------------|
| `W_EXIT_UNLINKED` | 0x10 | Find unlinked exits (can be stolen) |
| `W_EXIT_ONEWAY` | 0x1 | Find one-way exits (no return) |
| `W_EXIT_MULTIPLE` | 0x2 | Find multiple exits to same place |
| `W_EXIT_MSGS` | 0x4 | Find exits without messages |
| `W_EXIT_DESC` | 0x8 | Find exits without descriptions |
| `W_THING_MSGS` | 0x100 | Find things without messages |
| `W_THING_DESC` | 0x200 | Find things without descriptions |
| `W_ROOM_DESC` | 0x1000 | Find rooms without descriptions |
| `W_PLAYER_DESC` | 0x10000 | Find players without descriptions |
| `W_LOCK_PROBS` | 0x100000 | Find bad locks |

### Internal Flags (for message checks)
| Flag | Value | Description |
|------|-------|-------------|
| `W_UNLOCKED` | 0x1 | Check unlocked-object warnings |
| `W_LOCKED` | 0x2 | Check locked-object warnings |

### Warning Groups (Convenience)

| Group | Value | Composition |
|-------|-------|-------------|
| `W_NONE` | 0 | No warnings |
| `W_SERIOUS` | 0x110211 | EXIT_UNLINKED + THING_DESC + ROOM_DESC + PLAYER_DESC + LOCK_PROBS |
| `W_NORMAL` | 0x110217 | W_SERIOUS + EXIT_ONEWAY + EXIT_MULTIPLE + EXIT_MSGS |
| `W_EXTRA` | 0x110317 | W_NORMAL + THING_MSGS |
| `W_ALL` | 0x11031F | W_EXTRA + EXIT_DESC |

Default for new players: `W_NORMAL`

## Storage Model

- **Database**: Each object (player, thing, room, exit) stores a `warn_type` (uint32_t) field directly in the object structure
- **Inheritance**: If an object's warnings are `W_NONE` (0), the owner's warnings are used
- **NO_WARN Flag**: Overrides all warning settings - objects with this flag are never checked

## Commands

### @warnings <object>=<warning list>

**Purpose**: Configure which types of warnings should be reported for an object or player

**Permission**: Must control the object

**Syntax**:
```
@warnings me=normal                  # Set to normal warnings
@warnings #1234=exit-msgs thing-desc # Set specific warnings
@warnings #5678=all !exit-desc       # All except exit-desc
@warnings me=none                     # Clear all warnings
```

**Features**:
- Space-separated list of warning names
- Can use individual warnings or groups (`none`, `serious`, `normal`, `extra`, `all`)
- Can negate specific warnings with `!` prefix
- Reports current settings after change

**Available Warning Names**:
- Individual: `exit-unlinked`, `exit-oneway`, `exit-multiple`, `exit-msgs`, `exit-desc`, `thing-msgs`, `thing-desc`, `room-desc`, `my-desc`, `lock-checks`
- Groups: `none`, `serious`, `normal`, `extra`, `all`

### @wcheck <object>

**Purpose**: Check warnings on a specific object

**Permission**: Must own or have `see_all` power

**Behavior**:
- Uses object's warnings if set, otherwise owner's warnings
- Non-owners use their own warning settings
- Reports warnings found and completes

### @wcheck/me

**Purpose**: Check all objects owned by the player

**Permission**: No special permission needed

**Behavior**:
- Iterates through all objects owned by player
- Skips objects with NO_WARN flag
- Reports warnings for each object

### @wcheck/all

**Purpose**: Check all objects in database and notify connected owners

**Permission**: Wizards only

**Behavior**:
- Iterates through entire database
- Only checks objects whose owners are connected and not NO_WARN
- Notifies each owner of warnings found on their objects
- Usually run automatically at intervals (configured via `warn_interval`)

## Warning Check Logic

### Check Order
1. Skip if object is GOING (being deleted)
2. Skip if object has NO_WARN flag
3. Skip if owner has NO_WARN flag
4. Determine which warnings to check:
   - If owner is checking: use object's warnings, fallback to owner's
   - If admin is checking: use admin's warnings

### Room Checks (`W_ROOM_DESC`)
- Missing DESCRIBE attribute

### Exit Checks

**Unlinked Exits (`W_EXIT_UNLINKED`)**:
- Destination is NOTHING (can be stolen)
- Variable exits (dest = AMBIGUOUS) without DESTINATION or EXITTO attribute
- Variable exits with empty DESTINATION/EXITTO

**Messages (`W_EXIT_MSGS`)**:
- On possibly unlocked exits: missing SUCCESS, OSUCCESS, or ODROP
- On possibly locked exits: missing FAILURE

**Description (`W_EXIT_DESC`)**:
- Missing DESCRIBE attribute (skip if DARK)

**Topology (`W_EXIT_ONEWAY`, `W_EXIT_MULTIPLE`)**:
- One-way exits: No return exit from destination to source
- Multiple return exits: More than one exit from destination back to source
- Considers both regular and global (MASTER_ROOM) return exits

### Thing Checks

**Description (`W_THING_DESC`)**:
- Missing DESCRIBE attribute
- **Exception**: Skip things in player inventory

**Messages (`W_THING_MSGS`)**:
- On possibly unlocked things: missing SUCCESS, OSUCCESS, DROP, or ODROP
- On possibly locked things: missing FAILURE

### Player Checks

**Description (`W_PLAYER_DESC`)**:
- Missing DESCRIBE attribute

### Lock Problem Checks (`W_LOCK_PROBS`)

Checks all locks on an object for:
- Invalid object references (non-existent dbrefs)
- References to GOING/garbage objects
- Missing attributes in eval locks
- Indirect locks that aren't present or visible

### Lock Type Detection

To determine which message warnings apply, PennMUSH analyzes lock complexity:

| Lock Type | Detection | Warnings |
|-----------|-----------|----------|
| Unlocked | `TRUE_BOOLEXP` or empty | Check unlocked messages |
| Locked | Simple 2-instruction lock | Check locked messages |
| Complex | Complex expression | Check both types |

## Automatic Checking

**Configuration**: `warn_interval` option (default: 3600 seconds = 1 hour)

**Behavior**:
- Automatically runs `@wcheck/all` at configured intervals
- Only notifies connected owners
- Can be disabled by setting interval to 0
- Scheduled via `sq_register_in()` timer system

## Integration Points

### @clone Command
- Strips Wizard and Royalty flags
- Strips @powers
- **Strips @warnings** (unless wizard uses /preserve switch)

### brief Command
- Shows warning configuration when examining objects
- Format: `Warnings: normal` or `Warnings: exit-msgs thing-desc`

### Object Creation
- New players automatically get `W_NORMAL` warnings
- Other objects default to `W_NONE` (inherit from owner)

## Implementation Details

### Parse Warnings Algorithm

```c
warn_type parse_warnings(dbref player, const char *warnings) {
  warn_type flags = W_NONE;
  warn_type negate_flags = W_NONE;
  
  // Split on spaces
  foreach warning in warnings {
    if (starts_with(warning, '!')) {
      // Negated warning
      negate_flags |= lookup_warning(warning[1..]);
    } else {
      flags |= lookup_warning(warning);
    }
  }
  
  return flags & ~negate_flags;
}
```

### Unparse Warnings Algorithm

```c
const char *unparse_warnings(warn_type warns) {
  // Iterate from largest to smallest groups
  // If a group's bits are all set, add group name and remove those bits
  // This ensures "normal" is shown instead of individual components
  for (each warning in reverse order) {
    if ((warns & warning.flag) == warning.flag) {
      add_to_output(warning.name);
      warns &= ~warning.flag; // Remove subsumed warnings
    }
  }
  return output;
}
```

### Check Topology Function

```c
static void check_topology_on(dbref player, dbref i) {
  if (Going(i) || NoWarn(i)) return;
  
  // Determine flags to use
  if (Owner(player) == Owner(i)) {
    flags = Warnings(i) ? Warnings(i) : Warnings(player);
  } else {
    flags = Warnings(player); // Admin checking
  }
  
  // Generic checks (all types)
  ct_generic(player, i, flags);
  
  // Type-specific checks
  switch (Typeof(i)) {
    case TYPE_ROOM: ct_room(player, i, flags); break;
    case TYPE_THING: ct_thing(player, i, flags); break;
    case TYPE_EXIT: ct_exit(player, i, flags); break;
    case TYPE_PLAYER: ct_player(player, i, flags); break;
  }
}
```

## SharpMUSH Implementation Status

### Completed
✅ **WarningType Enum** (`SharpMUSH.Library/Definitions/WarningType.cs`)
- All 13 warning flags defined
- 4 convenience groups (None, Serious, Normal, Extra, All)
- Proper bit values matching PennMUSH

✅ **WarningTypeHelper Utility**
- `ParseWarnings(string, List<string>?)` - Parse space-separated warnings with negation support
- `UnparseWarnings(WarningType)` - Convert bitmask to string, showing groups when possible
- `GetAllWarningNames()` - List available warning names

✅ **SharpObject Model Update**
- Added `Warnings` property (WarningType) with default `WarningType.None`
- Proper JsonProperty configuration

✅ **IWarningService Interface** (`SharpMUSH.Library/Services/Interfaces/IWarningService.cs`)
- `CheckObjectAsync(checker, target)` - Check single object
- `CheckOwnedObjectsAsync(owner)` - Check all owned objects
- `CheckAllObjectsAsync()` - Full database scan

✅ **WarningService** (`SharpMUSH.Library/Services/WarningService.cs`)
- Basic structure and dependency injection
- Warning determination logic
- Room warning checks (partial)
- Player warning checks (partial)
- Complain/notification method

✅ **Command Updates** (`SharpMUSH.Implementation/Commands/MoreCommands.cs`)
- @warnings command structure (needs compilation fixes)
- @wcheck command with /all and /me switches (needs compilation fixes)

✅ **Dependency Injection**
- WarningService registered in Startup.cs
- Commands constructor updated

### Needs Completion

❌ **WarningService Implementation**
- Exit warning checks (unlinked, messages, description, topology)
- Thing warning checks (description, messages, inventory check)
- Lock problem checking
- Owner object retrieval and checking
- Database iteration for CheckOwned/CheckAll methods

❌ **Compilation Fixes**
- Resolve MarkupString type conversions in commands
- Fix method signature mismatches (attribute service, locate service)
- Add missing extension methods (IsGoing, etc.)
- Handle null reference warnings

❌ **Database Persistence**
- Update database schema migration to include Warnings column
- Persist Warnings changes from @warnings command
- Set default W_NORMAL on new players

❌ **NO_WARN Flag Support**
- Implement flag checks in WarningService
- Skip objects/players with NO_WARN set
- Document NO_WARN behavior

❌ **Configuration**
- Add `warn_interval` configuration option
- Create background task/scheduler for automatic checks
- Support disabling automatic checks (interval = 0)

❌ **Additional Features**
- @clone command warning stripping
- brief command warning display
- Lock type detection for message checks
- Exit topology traversal (one-way, multiple returns)

❌ **Testing**
- Unit tests for WarningTypeHelper
- Unit tests for WarningService checks
- Integration tests for @warnings command
- Integration tests for @wcheck command
- Test NO_WARN flag behavior
- Test warning inheritance

❌ **Documentation**
- Help file for @warnings command
- Help file for @wcheck command
- Help file listing all warning types
- Update @clone documentation
- Update brief command documentation

## Migration Considerations

1. **Existing Objects**: Will have null/0 warnings (treated as W_NONE)
   - Consider migration script to set W_NORMAL on existing players
   - Objects inherit from owners automatically

2. **Database Schema**: New column needed
   ```sql
   ALTER TABLE objects ADD COLUMN warnings INT DEFAULT 0;
   UPDATE objects SET warnings = 0x110217 WHERE type = 'PLAYER';
   ```

3. **Backward Compatibility**:
   - Old databases with no warnings column will need migration
   - Default to W_NONE for safety
   - Document upgrade process

## Testing Strategy

### Unit Tests
1. **WarningTypeHelper**:
   - Parse individual warnings
   - Parse warning groups
   - Parse negated warnings
   - Unparse various combinations
   - Handle unknown warnings

2. **WarningService**:
   - Each check type (room, exit, thing, player)
   - Warning determination logic
   - NO_WARN flag handling
   - Lock type detection

### Integration Tests
1. **@warnings Command**:
   - Set valid warnings
   - Set invalid warnings
   - Negate warnings
   - Clear warnings
   - Permission checks

2. **@wcheck Command**:
   - Check specific object
   - Check owned objects (/me)
   - Check all objects (/all)
   - Permission checks
   - NO_WARN behavior

### Performance Tests
- @wcheck/all on large databases (10k+ objects)
- Memory usage during full database scan
- Background task impact on server performance

## Recommendations

1. **Phase 1: Core Implementation** (Current State)
   - Complete WarningService implementation
   - Fix compilation errors
   - Basic database persistence

2. **Phase 2: Integration**
   - Add configuration system
   - Implement background checking
   - Update @clone command

3. **Phase 3: Polish**
   - Comprehensive testing
   - Documentation
   - Performance optimization

4. **Phase 4: Advanced Features**
   - Lock complexity analysis
   - Exit topology traversal
   - Custom warning types (extensibility)

## References

- **PennMUSH Source**: `/tmp/pennmush/src/warnings.c`, `/tmp/pennmush/hdrs/warn_tab.h`
- **PennMUSH Help**: `SharpMUSH.Documentation/Helpfiles/PennMUSH/penncmd.txt` (lines 3099-3136)
- **SharpMUSH Implementation**: 
  - `SharpMUSH.Library/Definitions/WarningType.cs`
  - `SharpMUSH.Library/Services/WarningService.cs`
  - `SharpMUSH.Implementation/Commands/MoreCommands.cs`
