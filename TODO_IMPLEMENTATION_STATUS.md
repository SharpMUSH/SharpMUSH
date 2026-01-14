# TODO Implementation Status

## Summary
- **Total TODOs at start**: 55 (before documentation improvements)
- **TODO markers restored**: 48 (per user request)
- **TODOs implemented**: 3
- **Remaining TODOs**: 45

## Implemented (Commit 73830b6)

### 1. ArangoDB Hierarchical Sorting (2 TODOs completed)
**File**: `SharpMUSH.Database.ArangoDB/ArangoDatabase.cs`
- Added `SORT v.LongName ASC` to both attribute pattern query methods
- Ensures hierarchical ordering (parent before children) in results
- **Lines**: 1707, 1747

### 2. Attribute Regex Pattern Separation (1 TODO completed)
**File**: `SharpMUSH.Library/HelperFunctions.cs`
- Created `ObjectWithLiteralAttribute()` for literal names only
- Created `ObjectWithWildcardAttribute()` for wildcard patterns (*, ?)
- Created `ObjectWithRegexAttribute()` for full regex syntax
- Improves type safety and validation clarity
- **Line**: 354

## Remaining TODO Categories (45 items)

### Major Feature Implementations (15 TODOs)
These require significant new subsystems:
- Pattern matching engine for @switch/@trigger (@match switch)
- Websocket/out-of-band communication (HTML, JSON)
- Money/penny transfer system
- Text file system integration (stext function)
- Attribute validation system (regex patterns, enum lists)
- Mail ID mapping system (per-player inbox numbers)
- Multi-database support (PostgreSQL, SQLite, etc.)

### Architectural Refactoring (12 TODOs)
These require design changes:
- Function lookup caching at startup
- Move function resolution to dedicated service
- Parser stack rewinding mechanism
- CRON/scheduled task service extraction
- Single-token command indexing
- Channel name fuzzy matching
- Parser state optimization (reduce allocations)

### Complex Implementations (10 TODOs)
These have clear requirements but complex implementation:
- Retroactive attribute flag updates across all objects
- NOBREAK switch for @break/@assert propagation control
- lsargs (list-style arguments) support
- Q-register evaluation string handling
- Attribute/access definition checking system
- Semaphore attribute flag validation
- Room/obj format for @remit command
- obj/attr syntax for function evaluation
- Attribute information display from table

### Performance Optimizations (5 TODOs)
- Depth checking placement optimization
- ParseContext as arguments (reduce allocations)
- ANSI string initialization optimization
- Parsed message alternatives
- Query result caching

### Informational/Optional (3 TODOs)
- Password compatibility note (already well-documented)
- SPEAK() function piping (optional enhancement)
- Attribute pattern return type consideration

## Recommendations

### High Priority (Should Implement Soon)
1. **Function caching** - Significant performance impact
2. **Semaphore attribute validation** - Prevents runtime errors
3. **Attribute table queries** - Enables attribute info display

### Medium Priority (Plan for Future Sprints)
1. **Pattern matching engine** - Enables multiple commands
2. **Parser optimizations** - General performance benefit
3. **Channel name matching improvements**

### Low Priority (Nice to Have)
1. **Websocket/OOB** - Modern feature, but not critical
2. **Money system** - Can be deferred
3. **SPEAK() piping** - Optional text processing

### Deferred (Architectural Changes)
1. **Multi-database support** - Major infrastructure change
2. **Service refactoring** - Can be done incrementally
3. **Parser stack rewinding** - Complex, unclear if needed

## Build Status
✅ All changes build successfully with no warnings or errors
✅ No test failures introduced
