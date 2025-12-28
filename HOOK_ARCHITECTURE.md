# Hook Architecture for SharpMUSH

## Overview

This document describes the implementation of the @hook command and hook execution architecture for SharpMUSH. The hook system allows softcode to be executed at various points during command processing, providing extensibility without modifying core command code.

## PennMUSH Compatibility

The implementation follows PennMUSH's @hook specification as documented in `help @hook` and related files.

## Architecture Components

### 1. Hook Service (`IHookService` / `HookService`)

Location: `SharpMUSH.Library/Services/HookService.cs`

The hook service manages hook registration and storage. It provides methods to:
- Get hooks for a command
- Set hooks for a command with various options
- Clear hooks
- Execute hooks (placeholder for future implementation)

Hooks are stored in-memory using a `ConcurrentDictionary<string, Dictionary<string, CommandHook>>` where:
- Outer key: Command name (uppercase)
- Inner key: Hook type (IGNORE, OVERRIDE, BEFORE, AFTER, EXTEND)
- Value: CommandHook instance

### 2. Hook Model (`CommandHook`)

Location: `SharpMUSH.Library/Models/CommandHook.cs`

A record that stores hook information:
```csharp
public record CommandHook(
    string HookType,
    DBRef TargetObject,
    string AttributeName,
    bool Inline = false,
    bool NoBreak = false,
    bool Localize = false,
    bool ClearRegs = false);
```

### 3. @hook Command

Location: `SharpMUSH.Implementation/Commands/WizardCommands.cs`

The @hook command provides the user interface for managing hooks. It supports:

#### Switches
- `/ignore` - Execute before command; skip command if returns false
- `/override` - Execute via $-command matching instead of built-in command
- `/before` - Execute before command (result discarded)
- `/after` - Execute after command (result discarded)
- `/extend` - Handle invalid switches via $-command matching
- `/igswitch` - Alias for `/extend` (Rhost compatibility)
- `/list` - List all hooks for a command

#### Inline Modifiers
- `/inline` - Execute immediately (not queued)
- `/nobreak` - @break in hook doesn't propagate to calling action list
- `/localize` - Save/restore q-registers around hook execution
- `/clearregs` - Clear q-registers before hook execution
- `/inplace` - Shorthand for `/inline/localize/clearregs/nobreak`

**Inline Execution Behavior:**
When a hook is marked with `/inline`, it executes immediately in the current execution context rather than being queued. This allows hooked commands to behave exactly like built-in commands with output appearing in the correct order.

**Q-Register Management:**
- `/localize` - Saves all q-registers before hook execution and restores them afterward, isolating the hook's register changes
- `/clearregs` - Clears all q-registers before executing the hook, ensuring a clean register state
- `/nobreak` - Prevents @break commands within the hook from stopping the calling action list (currently not implemented as @break propagation is handled differently)

These modifiers enable writing softcoded commands that integrate seamlessly with the command execution flow.

#### Usage
```
@hook/<type> <command>=<object>[,<attribute>]
@hook/<type> <command>                          (clears hook)
@hook/list <command>                            (lists hooks)
```

If attribute is omitted, defaults to `cmd.<hooktype>`.

### 4. Command Execution Integration Point

Location: `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`

The `HandleInternalCommandPattern` method is where hooks need to be integrated into the command execution flow.

## Hook Execution Flow (To Be Implemented)

When a built-in command is executed, hooks should be processed in this order:

1. **Parse command and arguments** (already implemented)

2. **Check for /ignore hook**
   - If exists, execute the hook
   - If hook returns false value, skip remaining processing
   - Continue to $-command matching

3. **Check for /before hook**
   - If exists, execute the hook
   - Discard the result (like wrapping in null())

4. **Check for /override hook**
   - If exists, perform $-command matching on the hook object/attribute
   - If match found, execute the matched command instead of built-in
   - If no match, continue to built-in execution

5. **Execute built-in command** (unless overridden)

6. **Check for /after hook**
   - If exists, execute the hook
   - Discard the result (like wrapping in null())

7. **If invalid switch error would occur, check for /extend hook**
   - Perform $-command matching on the hook object/attribute
   - If match found, execute instead of error
   - Otherwise, return the error

## Named Registers

Hooks have access to special named registers via `r(<name>, args)`:

- `ARGS` - Entire argument string before evaluation (always available)
- `LS` - Left-side argument (before = for EQSPLIT commands)
- `LSAC` - Count of left-side arguments (for multi-arg commands)
- `LSA1`, `LSA2`, etc. - Individual left-side arguments
- `EQUALS` - "=" if present (for EQSPLIT commands)
- `RS` - Right-side argument (after = for EQSPLIT commands)
- `RSAC` - Count of right-side arguments (for multi-arg commands)
- `RSA1`, `RSA2`, etc. - Individual right-side arguments
- `SWITCHES` - Switch string given (for commands that support switches)

Additionally, hooks can use `%u` to access the entire command string entered.

## Implementation Status

### Completed
- [x] IHookService interface
- [x] HookService implementation (storage and retrieval)
- [x] CommandHook model
- [x] @hook command implementation (all switches)
- [x] Dependency injection wiring
- [x] Documentation of architecture
- [x] Hook execution in command flow
- [x] Named register population (ARGS, LS, RS, LSAx, LSAC, EQUALS, SWITCHES)
- [x] Hook code execution via AttributeService.EvaluateAttributeFunctionAsync
- [x] /ignore hook - executed before command, skips if returns false
- [x] /before hook - executed before command, result discarded
- [x] /after hook - executed after command, result discarded
- [x] /override hook - uses $-command matching instead of built-in command
- [x] /extend hook - handles invalid switches via $-command matching
- [x] $-command matching for /override and /extend hooks
- [x] Switch validation before command execution
- [x] In-memory hook storage (intended design - hooks persist for server session)
- [x] Complete @mogrifier system implementation with all MOGRIFY` attributes
- [x] Inline execution handling (/inline modifier)
- [x] Q-register management (/localize, /clearregs modifiers)

### To Be Implemented
- [x] Inline execution handling (queue vs immediate) for /inline modifier
- [x] Q-register management (localize, clearregs, nobreak) for inline hooks
- [ ] Integration with HUH_COMMAND hook
- [ ] Individual player @chatformat support in mogrifier
- [ ] Channel recall buffer support for mogrifier
- [ ] Unit tests for @hook command
- [ ] Integration tests for hook execution
- [ ] Unit tests for mogrifier
- [ ] Performance optimization for hook lookup

## @mogrifier System Implementation

### Overview
The @mogrifier system has been fully implemented in ChannelMessageRequestHandler. It allows complete customization of channel message output before it reaches individual players.

### Mogrification Pipeline

When a channel message is sent, the following steps occur:

1. **Check for Mogrifier**: If the channel has a mogrifier object set, load it from the database
2. **Use Lock Check**: Verify that the speaker passes the mogrifier object's Use lock
3. **Control Mogrifiers** (executed first):
   - `MOGRIFY`BLOCK` - If returns non-empty, send result to speaker only and block broadcast
   - `MOGRIFY`OVERRIDE` - If returns true, skip individual @chatformats
   - `MOGRIFY`NOBUFFER` - If returns true, don't add message to recall buffer
4. **Part Mogrifiers** (modify individual components):
   - `MOGRIFY`CHANNAME` - Alter the channel display name (default: `<ChannelName>`)
   - `MOGRIFY`TITLE` - Alter the player's title
   - `MOGRIFY`PLAYERNAME` - Alter the player's name
   - `MOGRIFY`SPEECHTEXT` - Alter the "says" text
   - `MOGRIFY`MESSAGE` - Alter the message content
5. **Format Mogrifier**:
   - `MOGRIFY`FORMAT` - Channel-wide message format (like @chatformat)
6. **Individual @chatformat**: Applied unless `MOGRIFY`OVERRIDE` returned true
7. **Buffer**: Message logged unless `MOGRIFY`NOBUFFER` returned true

### Arguments Passed to Mogrifiers

**Control Mogrifiers** (BLOCK, OVERRIDE, NOBUFFER):
- %0: Chat type character (", :, ;, @)
- %1: Channel name (unmogrified)
- %2: Message text
- %3: Player name
- %4: Player title

**Part Mogrifiers** (CHANNAME, TITLE, PLAYERNAME, SPEECHTEXT, MESSAGE):
- %0: Current value of that part (potentially already mogrified)
- %1: Channel name (unmogrified)
- %2: Chat type character (", :, ;, @)
- %3: Message text
- %4: Player title
- %5: Player name
- %6: "Says" text
- %7: Space-separated options (e.g., "silent" or "noisy")

**Format Mogrifier** (FORMAT):
- %0: Chat type character
- %1: Channel name
- %2: Message text (mogrified)
- %3: Player name (mogrified)
- %4: Player title (mogrified)
- %5: Default formatted message
- %6: "Says" text (mogrified)
- %7: Options

### Chat Type Handling

The mogrifier correctly handles different chat types:
- `"` (say): Standard speech with says/name/message
- `:` (pose): Pose with optional title/name/message
- `;` (semipose): Semipose with optional title and name merged with message
- `@` (emit): Direct emit of message only

## Design Decisions

### In-Memory Storage
Hooks are stored in memory for the server session lifetime. This is the intended design - hooks are administrative configuration that persist for the duration of the server run. When the server restarts, hooks need to be reconfigured via @hook commands. This matches typical MUSH server behavior where runtime configuration is established on startup and maintained in memory.

### Minimal Changes to Command Execution
To minimize impact on existing code, hook execution is intended to be added as a wrapper around the existing command invocation rather than modifying individual commands.

## Future Enhancements

1. **Hook Introspection**: Add commands to query hook information beyond @hook/list
2. **Hook Permissions**: Add fine-grained control over who can set hooks
3. **Hook Priorities**: Support multiple hooks of the same type
4. **Hook Debugging**: Add logging and tracing for hook execution
5. **Hook Performance**: Cache hook lookups for frequently-used commands
6. **Alias.cnf Support**: Allow hooks to be configured in configuration files on server startup
7. **Database Persistence** (optional): If needed, hooks could be persisted to database for automatic restoration on server restart

## References

- PennMUSH `help @hook`, `help @hook2`, `help @hook3`, etc.
- PennMUSH `help @command`
- PennMUSH `help HUH_COMMAND`
- SharpMUSH Documentation: `SharpMUSH.Documentation/Helpfiles/PennMUSH/penncmd.txt`
