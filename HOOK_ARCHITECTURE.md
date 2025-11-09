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

### To Be Implemented
- [ ] Hook execution in command flow
- [ ] Named register population and access
- [ ] $-command matching for /override and /extend hooks
- [ ] Inline execution handling (queue vs immediate)
- [ ] Q-register management (localize, clearregs, nobreak)
- [ ] Integration with HUH_COMMAND hook
- [ ] Persistence of hooks (currently in-memory only)
- [ ] Unit tests for @hook command
- [ ] Integration tests for hook execution
- [ ] Performance optimization for hook lookup

## Design Decisions

### In-Memory Storage
Hooks are currently stored in memory. For production use, this should be persisted to the database alongside command configuration. The SharpCommand model already has a Hooks dictionary field that could be used for persistence.

### Hook Service as Singleton
The HookService is registered as a singleton to maintain hook state across the application lifetime. This works for the current in-memory implementation but should be reconsidered when adding persistence.

### Minimal Changes to Command Execution
To minimize impact on existing code, hook execution is intended to be added as a wrapper around the existing command invocation rather than modifying individual commands.

## Future Enhancements

1. **Persistence**: Store hooks in database
2. **Hook Introspection**: Add commands to query hook information
3. **Hook Permissions**: Add fine-grained control over who can set hooks
4. **Hook Priorities**: Support multiple hooks of the same type
5. **Hook Debugging**: Add logging and tracing for hook execution
6. **Hook Performance**: Cache hook lookups for frequently-used commands
7. **Alias.cnf Support**: Allow hooks to be configured in configuration files

## References

- PennMUSH `help @hook`, `help @hook2`, `help @hook3`, etc.
- PennMUSH `help @command`
- PennMUSH `help HUH_COMMAND`
- SharpMUSH Documentation: `SharpMUSH.Documentation/Helpfiles/PennMUSH/penncmd.txt`
