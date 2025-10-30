# Critical Implementation Priorities

This document summarizes the most critical missing implementations identified in the inconsistency analysis.

## Top 20 Missing Commands (By Priority)

### Tier 1: Core Building (Cannot build without these)
1. `@CREATE` - Create new objects (BuildingCommands.cs:29)
2. `@DESTROY` - Destroy objects (BuildingCommands.cs:179)
3. `@DIG` - Create rooms (BuildingCommands.cs:216)
4. `@OPEN` - Create exits (BuildingCommands.cs:358)
5. `@LINK` - Link exits to destinations (BuildingCommands.cs:259)
6. `@SET` - Set attributes (GeneralCommands.cs:457)

### Tier 2: Essential Building
7. `@CHOWN` - Change ownership (BuildingCommands.cs:138)
8. `@TELEPORT` - Move objects (GeneralCommands.cs:635)
9. `@LOCK` - Set locks (GeneralCommands.cs:267)
10. `@UNLOCK` - Remove locks (GeneralCommands.cs:440)

### Tier 3: Critical Admin
11. `@PCREATE` - Create players (WizardCommands.cs:38)
12. `@BOOT` - Kick players (WizardCommands.cs:91)
13. `@DUMP` - Save database (WizardCommands.cs:293)
14. `@SHUTDOWN` - Stop server (WizardCommands.cs:467)

### Tier 4: Building Utilities
15. `@CLONE` - Clone objects (BuildingCommands.cs:324)
16. `@WIPE` - Clear attributes (GeneralCommands.cs:724)
17. `@CPATTR` - Copy attributes (AttributeCommands.cs:17)
18. `@EDIT` - Edit attributes (GeneralCommands.cs:1274)

### Tier 5: Communication
19. `@CEMIT` - Emit to channel (ChannelCommands.cs:20)
20. `@LEMIT` - Emit to room (GeneralCommands.cs:253)

## Top 30 Missing Functions (By Priority)

### Tier 1: Core Object Operations (Needed by everything)
1. `loc()` - Get object location (DbrefFunctions.cs:18)
2. `owner()` - Get object owner (DbrefFunctions.cs:66)
3. `parent()` - Get parent object (DbrefFunctions.cs:74)
4. `room()` - Get containing room (DbrefFunctions.cs:82)
5. `match()` - Match object by name (DbrefFunctions.cs:34)
6. `locate()` - Locate object (DbrefFunctions.cs:26)

### Tier 2: Essential Object Queries
7. `lexits()` - List exits (DbrefFunctions.cs:217)
8. `lcon()` - List contents (DbrefFunctions.cs:201)
9. `lplayers()` - List players (DbrefFunctions.cs:225)
10. `lthings()` - List things (DbrefFunctions.cs:233)
11. `zone()` - Get zone (DbrefFunctions.cs:106)

### Tier 3: Attribute Operations
12. `lattr()` - List attributes (AttributeFunctions.cs:148)
13. `nattr()` - Count attributes (AttributeFunctions.cs:189)
14. `grep()` - Search attributes (AttributeFunctions.cs:45)
15. `grepi()` - Case-insensitive grep (AttributeFunctions.cs:53)
16. `xattr()` - Cross-object attribute list (AttributeFunctions.cs:242)
17. `xget()` - Cross-object get (AttributeFunctions.cs:249)

### Tier 4: Object Utilities
18. `create()` - Create object (UtilityFunctions.cs:1155)
19. `dig()` - Create room (UtilityFunctions.cs:1181)
20. `open()` - Create exit (UtilityFunctions.cs:1189)
21. `clone()` - Clone object (UtilityFunctions.cs:1147)
22. `tel()` - Teleport (UtilityFunctions.cs:1221)
23. `set()` - Set attribute (UtilityFunctions.cs:1205)

### Tier 5: Information Functions
24. `money()` - Get money (InformationFunctions.cs:173)
25. `quota()` - Get quota (InformationFunctions.cs:189)
26. `controls()` - Check control (UtilityFunctions.cs:352)
27. `visible()` - Check visibility (UtilityFunctions.cs:433)

### Tier 6: Connection Functions
28. `idle()` - Get idle time (ConnectionFunctions.cs:118)
29. `doing()` - Get doing message (ConnectionFunctions.cs:94)
30. `conn()` - Get connection info (ConnectionFunctions.cs:45)

## Implementation Dependencies

### Dependency Chain for Building System

```
Layer 1 (Foundation):
  loc(), owner(), parent() → Required by almost everything
  get(), set() → Required for attributes
  hasattr(), hasflag() → Required for validation

Layer 2 (Core Building):
  @CREATE, @SET → Depends on Layer 1
  match(), locate() → Depends on loc(), owner()
  @DIG, @OPEN → Depends on @CREATE

Layer 3 (Linking):
  @LINK, @UNLINK → Depends on Layer 2
  lexits(), lcon() → Depends on loc()
  room(), zone() → Depends on parent(), loc()

Layer 4 (Advanced):
  @CHOWN, @TELEPORT → Depends on Layer 3
  @CLONE → Depends on @CREATE, attribute copying
  @DESTROY → Depends on validation functions
```

## Quick Win Opportunities

These are relatively simple implementations that would provide immediate value:

1. **`loc()`** - Get location of object (single property read with validation)
2. **`owner()`** - Get owner of object (single property read)
3. **`parent()`** - Get parent of object (single property read)
4. **`hasattr()`** - Check if attribute exists (simple lookup)
5. **`hasflag()`** - Check if flag is set (simple check)

## Test Coverage Needed

All implementations should include tests for:
- ✅ Valid inputs
- ✅ Invalid inputs (null, empty, wrong type)
- ✅ Permission checks
- ✅ Non-existent objects
- ✅ Edge cases
- ✅ Integration with other functions

## Documentation Requirements

Each implementation needs:
- ✅ XML code comments
- ✅ Entry in penncmd.md or pennfunc.md
- ✅ Syntax description
- ✅ Examples (at least 2)
- ✅ Related command/function links
- ✅ PennMUSH compatibility notes

## Weekly Implementation Goals

### Week 1-2: Foundation (Quick Wins)
- Implement: `loc()`, `owner()`, `parent()`, `room()`, `hasattr()`, `hasflag()`
- Write comprehensive tests
- Document all functions
- **Goal:** 6 functions implemented

### Week 3-4: Core Building
- Implement: `@CREATE`, `@SET`, `match()`, `locate()`
- Write comprehensive tests
- Document all commands/functions
- **Goal:** 2 commands, 2 functions implemented

### Week 5-6: Building System
- Implement: `@DIG`, `@OPEN`, `@LINK`, `lexits()`, `lcon()`
- Write comprehensive tests
- Document all commands/functions
- **Goal:** 3 commands, 2 functions implemented

### Week 7-8: Advanced Building
- Implement: `@DESTROY`, `@CHOWN`, `@TELEPORT`, `zone()`
- Write comprehensive tests
- Document all commands/functions
- **Goal:** 3 commands, 1 function implemented

## Success Metrics

- ✅ All Tier 1 items implemented and tested
- ✅ Test coverage ≥ 80% for new code
- ✅ All implementations documented
- ✅ PennMUSH compatibility verified
- ✅ Integration tests pass
- ✅ No new bugs introduced

## Notes

- This represents the bare minimum for a functional MUSH server
- Additional functionality should be prioritized based on user feedback
- Some items may have hidden dependencies not listed here
- Coordinate with team before starting implementation
- Create GitHub issues for tracking progress
