# SharpMUSH pemit() Compatibility with PennMUSH

## Overview

This document details the compatibility analysis and implementation of the `pemit()` and `nspemit()` functions in SharpMUSH to match PennMUSH behavior.

## Issue Reference

**Issue**: Check SharpMUSH compatibility with PennMUSH `pemit` function  
**PennMUSH Source**: https://github.com/pennmush/pennmush/blob/e15120473393b196e0ff135a9e30c463484c1d08/src/funmisc.c#L138

## Implementation Summary

### Functions Implemented

1. **`pemit(recipients, message)`** - Private emit to specified recipients
2. **`nspemit(recipients, message)`** - Nospoof variant of pemit

### Key Features

✅ **Port-based messaging**: Detects integer lists (e.g., "1234 5678") and routes to connection ports  
✅ **Object-based messaging**: Handles dbrefs (#1, #2) and player names  
✅ **Multiple recipients**: Space-separated lists supported  
✅ **Nospoof support**: Permission checking and appropriate notification type  
✅ **Side effects validation**: Checks configuration and returns error if disabled  
✅ **Empty return**: Side effect functions return empty string

## Compatibility Analysis

### Matching Behaviors

| Feature | PennMUSH | SharpMUSH | Status |
|---------|----------|-----------|--------|
| Integer list detection | `is_integer_list()` | `IsIntegerList()` | ✅ Compatible |
| Port messaging | `do_pemit_port()` | `NotifyService.Notify(ports)` | ✅ Compatible |
| Object messaging | `do_pemit()` | `NotifyService.Notify(dbref)` | ✅ Compatible |
| Nospoof variant | `nspemit()` | `nspemit()` | ✅ Compatible |
| Nospoof permissions | `Can_Nspemit()` | `PermissionService.CanNoSpoof()` | ✅ Compatible |
| Side effects check | `!FUNCTION_SIDE_EFFECTS` | `Configuration.FunctionSideEffects` | ✅ Compatible |
| Multiple recipients | `PEMIT_LIST` flag | Loop over recipients | ✅ Compatible |
| Return value | Empty (implicit) | `CallState.Empty` | ✅ Compatible |

### Known Gap

⚠️ **Command Permission Check**: PennMUSH verifies executor can use `@pemit` command via `command_check_byname()`. This is not implemented as it depends on SharpMUSH's permission model design.

**Impact**: Low to Medium - Could allow unauthorized messaging if command-level permissions are enforced  
**Recommendation**: Add if SharpMUSH requires command permission verification

## Code Changes

### Files Modified

1. **`SharpMUSH.Implementation/Functions/CommunicationFunctions.cs`**
   - Implemented `PrivateEmit()` method for `pemit()` function
   - Implemented `NoSpoofPrivateEmit()` method for `nspemit()` function  
   - Added `IsIntegerList()` helper method
   - Added side effects configuration checks
   - Added `FunctionFlags.HasSideFX` flag

2. **`SharpMUSH.Tests/Functions/CommunicationFunctionUnitTests.cs`**
   - Enabled pemit and nspemit tests by removing `[Skip]` attributes

## Technical Details

### Integer List Detection

```csharp
private static bool IsIntegerList(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return false;
    
    var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return tokens.Length > 0 && tokens.All(token => long.TryParse(token, out _));
}
```

Matches PennMUSH's `is_integer_list()` behavior:
- Returns true only if ALL space-separated tokens are valid integers
- Used to distinguish port lists from object/player lists

### Message Routing Logic

```
pemit(recipients, message):
  1. Check if side effects enabled → error if disabled
  2. Parse recipients string
  3. If IsIntegerList(recipients):
       → Parse as port numbers
       → Send via NotifyService.Notify(long[] ports, ...)
     Else:
       → Parse as object/player names
       → Resolve each recipient
       → Send via NotifyService.Notify(dbref, ...)
  4. Return empty string
```

### Nospoof Variant

```csharp
// Check nospoof permission
var notificationType = await PermissionService.CanNoSpoof(executor)
    ? INotifyService.NotificationType.NSAnnounce  // Has permission
    : INotifyService.NotificationType.Announce;    // No permission
```

## Testing

### Test Cases

- `pemit(#1, test message)` - Send to object #1
- `nspemit(#1, test)` - Nospoof variant

Tests verify:
- Functions execute without errors
- Return empty string (side effect function)
- Notification service is called appropriately

## Conclusion

The SharpMUSH implementation of `pemit()` and `nspemit()` is **highly compatible** with PennMUSH, implementing all core functionality:

- ✅ Correct recipient parsing and routing
- ✅ Port-based and object-based messaging  
- ✅ Nospoof support with permissions
- ✅ Configuration and error handling
- ✅ Multiple recipient handling

The implementation follows SharpMUSH's architectural patterns while maintaining behavioral compatibility with PennMUSH for user-facing functionality.

## References

- PennMUSH pemit: https://github.com/pennmush/pennmush/blob/e15120473393b196e0ff135a9e30c463484c1d08/src/funmisc.c#L138
- SharpMUSH Function Flags: `SharpMUSH.Library/Definitions/FunctionFlags.cs`
- Configuration: `SharpMUSH.Configuration/Options/FunctionOptions.cs`
