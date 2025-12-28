# SharpMUSH TODO Quick Reference

This is a condensed reference guide for the 283 TODO items found in the codebase. For detailed information, see `TODO_IMPLEMENTATION_PLAN.md`.

---

## Quick Stats

| Category | Count | Percentage |
|----------|-------|------------|
| Commands | 104 | 37% |
| Functions | 58 | 20% |
| Services | 42 | 15% |
| MarkupString & Formatting | 19 | 7% |
| Tests | 17 | 6% |
| Parser & Visitors | 15 | 5% |
| Substitutions | 7 | 2% |
| Database | 7 | 2% |
| Telnet Protocol | 4 | 1% |
| Message Consumers | 3 | 1% |
| Other | 7 | 2% |
| **Total** | **283** | **100%** |

---

## Top 10 High-Impact Items

### 1. Q-Register System
**Impact:** Unblocks ~15 command TODOs  
**Files:** `GeneralCommands.cs` (multiple lines)  
**What:** Implement environment arguments (%0-%9), Q-register management, pattern matching

### 2. AttributeService Pattern Modes
**Impact:** Affects many functions and commands  
**File:** `AttributeService.cs` lines 431-450  
**What:** Wildcard attribute matching, parent checking, full path returns

### 3. $-Command Matching
**Impact:** Critical parser feature  
**File:** `SharpMUSHParserVisitor.cs` line 746  
**What:** Implement command override via $-commands

### 4. Database Pattern Matching
**Impact:** Core attribute functionality  
**File:** `ArangoDatabase.cs` lines 1666-1667, 1702-1703  
**What:** Proper wildcard support for attribute trees

### 5. Permission System Fixes
**Impact:** Security critical  
**File:** `PermissionService.cs` lines 39-91, 174  
**What:** Complete permission implementation, fix logic bugs

### 6. Halt Functionality
**Impact:** Command control  
**File:** `GeneralCommands.cs` line 4774-4775  
**What:** Implement actual halt and @STARTUP triggering

### 7. Lock System Completion
**Impact:** Access control  
**File:** `LockService.cs` line 120  
**What:** Complete unimplemented lock functionality

### 8. Follower/Following Tracking
**Impact:** Social features  
**Files:** `DbrefFunctions.cs` lines 287, 306; `MoreCommands.cs` lines 835, 899  
**What:** Full implementation of follow system

### 9. Channel System Polish
**Impact:** Communication features  
**Files:** Multiple channel command/function files  
**What:** Notifications, standardized methods, message history

### 10. Queue System
**Impact:** Command execution model  
**File:** `GeneralCommands.cs` multiple lines  
**What:** Inline vs queued, PID tracking, WAIT/INDEPENDENT queues

---

## Recommended Implementation Order

### Phase 1: Foundation (2-3 weeks)
```
1. Q-Register System
2. AttributeService Pattern Modes
3. Permission System Fixes
4. Database Pattern Matching
```

### Phase 2: Core (2-3 weeks)
```
1. Command Queue System
2. Follower/Following Tracking
3. Lock System Completion
4. Halt Functionality
```

### Phase 3: Communication (1-2 weeks)
```
1. Channel System Polish
2. SPEAK() Integration
3. Movement Notifications
4. Mail System
```

### Phase 4: Parser (1-2 weeks)
```
1. $-Command Matching
2. Parser Optimizations
3. Context Switching
4. QREG Improvements
```

### Phase 5: Functions (2 weeks)
```
1. Database Query Functions
2. Connection Functions
3. PennMUSH Format Support
4. Quota System
```

### Phase 6: Advanced (2-3 weeks)
```
1. Telnet Protocol Handlers
2. Text File Functions
3. MarkupString Optimizations
```

### Phase 7: Polish (1-2 weeks)
```
1. Fix Failing Tests
2. Add Missing Tests
3. Documentation Updates
```

---

## By File (Top Files by TODO Count)

| File | Count | Top Items |
|------|-------|-----------|
| `GeneralCommands.cs` | 54 | Q-registers, halt, queue, database search |
| `AttributeFunctions.cs` | 8 | grep, trust checking, target attribute |
| `MoreCommands.cs` | 17 | Following system, notifications, money |
| `DbrefFunctions.cs` | 12 | Follower tracking, search, next dbref |
| `AttributeService.cs` | 13 | Pattern modes, parent checking, permissions |
| `SharpMUSHParserVisitor.cs` | 13 | $-commands, optimization, permissions |
| `ConnectionFunctions.cs` | 7 | Get all players, Dark visibility, @poll |
| `UtilityFunctions.cs` | 9 | suggest, text file system integration |
| `WizardCommands.cs` | 7 | Boot behavior, SPEAK(), validation |
| `PennMUSHDatabaseConverter.cs` | 6 | MarkupString conversion, relationships |

---

## Critical Bugs to Address

### High Priority Bugs
1. **DatabaseCommands.cs** lines 186, 201: NOT YET IMPLEMENTED
2. **PermissionService.cs** line 174: Logic bug ('return true or true')
3. **LockService.cs** line 120: NotImplementedException
4. **LocateService.cs** line 235: Logic may be incorrect
5. **ArangoDatabase.cs** lines 1666-1703: Lazy pattern matching implementation

### Test Failures
1. **DatabaseCommandTests.cs** line 240: Infinite loop bug
2. **GeneralCommandTests.cs**: Multiple failing tests (lines 334, 404, 418, 432, 460, 503, 534)
3. **CommunicationCommandTests.cs**: Failing tests (lines 241, 382)
4. **StringFunctionUnitTests.cs**: decompose/decomposeweb issues (lines 254-269)

---

## Quick Navigation by Area

### Want to work on Commands?
→ See `TODO_IMPLEMENTATION_PLAN.md` Section 1 (104 items)
- Start with: Database Commands (NOT IMPLEMENTED)
- Then: General Commands (Q-registers, halt, queue)

### Want to work on Functions?
→ See `TODO_IMPLEMENTATION_PLAN.md` Section 2 (58 items)
- Start with: Attribute Functions (grep, trust)
- Then: Dbref Functions (follower tracking, search)

### Want to work on Services?
→ See `TODO_IMPLEMENTATION_PLAN.md` Section 3 (42 items)
- Start with: PermissionService (critical bugs)
- Then: AttributeService (pattern modes)

### Want to work on Parser?
→ See `TODO_IMPLEMENTATION_PLAN.md` Section 4 (15 items)
- Start with: $-command matching
- Then: Optimization improvements

### Want to fix Tests?
→ See `TODO_IMPLEMENTATION_PLAN.md` Section 7 (17 items)
- Start with: DatabaseCommandTests infinite loop
- Then: GeneralCommandTests failures

### Want to work on Protocol?
→ See `TODO_IMPLEMENTATION_PLAN.md` Section 8 (4 items)
- All 4 telnet handlers need implementation

---

## Dependencies at a Glance

```
Q-Register System
├── Environment arguments (%0-%9)
├── Q-register management (clearregs, localize)
├── Pattern matching (/match)
├── Queue management
└── Stack rewinding

AttributeService Pattern Modes
├── Wildcard attribute matching
├── Parent checking
├── Full path returns
└── Permission memoization

Permission System
├── Secure command execution
├── Access control
├── Trust checking
└── Visibility controls

Queue System
├── Inline vs queued execution
├── PID tracking
├── WAIT and INDEPENDENT queues
└── @STARTUP triggers

Database Improvements
├── Attribute tree support
├── Object searching
├── Quota tracking
└── Statistics queries
```

---

## How to Choose What to Work On

### If you want quick wins:
- Documentation updates (Section 6.3)
- Helper function cleanups (Section 11.1)
- Single-line fixes (various locations)

### If you want high impact:
- Q-Register System (Phase 1)
- AttributeService Pattern Modes (Phase 1)
- Permission System (Phase 1)

### If you want to enable other work:
- Database Pattern Matching (Phase 1)
- Queue System (Phase 2)
- Lock System (Phase 2)

### If you want to improve user experience:
- Channel System (Phase 3)
- Movement Notifications (Phase 3)
- Mail System (Phase 3)

### If you want to fix bugs:
- Permission Service logic bug
- Database Commands NOT IMPLEMENTED
- Test failures

---

## Contact Points for Each Area

Each area has a primary file or set of files:

- **Commands:** `SharpMUSH.Implementation/Commands/`
- **Functions:** `SharpMUSH.Implementation/Functions/`
- **Services:** `SharpMUSH.Library/Services/`
- **Parser:** `SharpMUSH.Implementation/Visitors/`
- **Database:** `SharpMUSH.Database.ArangoDB/`
- **Tests:** `SharpMUSH.Tests/`
- **MarkupString:** `SharpMUSH.MarkupString/`
- **Telnet:** `SharpMUSH.Implementation/Handlers/Telnet/`

---

For complete details on any item, see **TODO_IMPLEMENTATION_PLAN.md**.
