# SharpMUSH TODO Implementation Plan

**Last Updated:** 2025-12-30  
**Total TODOs:** 235  
**Completed in This PR:** 62 (from original 283)

## Executive Summary

This document provides a comprehensive analysis of all remaining TODOs in the SharpMUSH codebase after fresh analysis. The codebase currently has 235 TODOs distributed across commands (61), functions (70), services (23), parser/visitors (13), and other components.

---

## Quick Stats

| Category | Count | Priority | Est. Effort |
|----------|-------|----------|-------------|
| Commands | 61 | HIGH | 5-8 days |
| Functions | 70 | HIGH | 6-10 days |
| Services | 23 | MEDIUM | 4-6 days |
| Parser/Visitors | 13 | MEDIUM | 3-4 days |
| Handlers | 5 | LOW | 2-3 days |
| Other | 63 | LOW-MED | 3-5 days |
| **TOTAL** | **235** | - | **23-36 days** |

---

## 1. Commands (61 TODOs)

### 1.1 BuildingCommands.cs (9 TODOs)
**Priority:** HIGH | **Effort:** 1-2 days

Core building commands needed for game world creation:
- `@dig` - Room creation
- `@open` - Exit creation  
- `@tel` - Teleportation
- `@set` - Attribute flag setting
- `@link` - Exit linking
- `@wipe` - Attribute deletion
- `@mvattr` - Attribute moving
- `@cpattr` - Attribute copying
- `@parent` - Inheritance setup

**Dependencies:** MoveService quota, AttributeService enhancements

---

### 1.2 GeneralCommands.cs (38 TODOs)
**Priority:** HIGH | **Effort:** 3-5 days

**Queue/Execution (8 TODOs):**
- `@ps`, `@wait`, `@trigger`, `@pemit`, `@remit`
- Critical for MUSH scripting

**Object Manipulation (12 TODOs):**
- `@name`, `@desc`, `@lock`, `@unlock`
- Core gameplay features

**Communication (8 TODOs):**
- `@mail`, `page`, `whisper`
- Player interaction

**Administration (10 TODOs):**
- `@newpassword`, `@boot`, `@toad`, `@pcreate`
- Wizard tools (lower priority)

---

### 1.3 MoreCommands.cs (10 TODOs)
**Priority:** MEDIUM | **Effort:** 1-2 days

Miscellaneous social and utility commands.

---

### 1.4 WizardCommands.cs (4 TODOs)
**Priority:** LOW | **Effort:** 0.5-1 day

Advanced admin commands (wizard-only).

---

## 2. Functions (70 TODOs)

### 2.1 AttributeFunctions.cs (12 TODOs)
**Priority:** HIGH | **Effort:** 2-3 days

- `edefault()` behavior (lines 173, 495)
- Attribute tree traversal (lines 220-350)
- Attribute manipulation (lines 410-550)
- Attribute properties (lines 600-750)

**Critical for:** Scripting and building

---

### 2.2 DbrefFunctions.cs (15 TODOs)
**Priority:** HIGH | **Effort:** 2-3 days

- Object relationships: `parent()`, `zone()`, `owner()` (lines 180-320)
- Object properties: `type()`, `flags()`, `powers()` (lines 350-480)
- Object lists: `children()`, `lcon()`, `lexits()` (lines 520-650)
- Advanced: `followers()`, `following()` (lines 700-820)

**Dependencies:** Database optimization, caching

---

### 2.3 StringFunctions.cs (18 TODOs)
**Priority:** MEDIUM | **Effort:** 2-3 days

- String manipulation: `decompose()`, `escape()`, `secure()` (lines 150-280)
- Pattern matching: `regmatch()`, `regedit()`, `grep()` (lines 320-450)
- Formatting: `ljust()`, `rjust()`, `center()` (lines 480-620)
- Analysis: `edit()`, `translate()`, `tr()` (lines 650-780)

**Most requested:** Pattern matching functions

---

### 2.4 ListFunctions.cs (8 TODOs)
**Priority:** MEDIUM | **Effort:** 1-2 days

Advanced list manipulation - variations on existing patterns.

---

### 2.5 Other Functions (17 TODOs)
**Priority:** LOW-MEDIUM | **Effort:** 2-3 days

- MathFunctions.cs: 5 TODOs
- LogicFunctions.cs: 3 TODOs
- UtilityFunctions.cs: 5 TODOs
- ColorFunctions.cs: 2 TODOs
- ChannelFunctions.cs: 2 TODOs

---

## 3. Services (23 TODOs)

### 3.1 AttributeService.cs (6 TODOs)
**Priority:** HIGH | **Effort:** 1-2 days

1. Lines 133, 233: Return full attribute path
2. Line 335: Function permission checks
3. Line 499: Handle "already set" case
4. Line 531: Handle "not set" case
5. Line 554: Object permissions

**Impact:** Core system - affects many commands

---

### 3.2 ValidateService.cs (3 TODOs)
**Priority:** MEDIUM | **Effort:** 0.5-1 day

1. Line 125: Cache attribute names
2. Line 132: Caching with globbing
3. Line 205: Forbidden names list

**Impact:** Performance + validation

---

### 3.3 DatabaseConversion/PennMUSHDatabaseConverter.cs (6 TODOs)
**Priority:** LOW | **Effort:** 1-2 days

- Line 206: Update God player name/password
- Line 270: Update Room #0 name
- Lines 553, 561: Set parent/zone relationships
- Lines 618, 782: Convert to MarkupStrings

**Timing:** When migration needed

---

### 3.4 Other Services (8 TODOs)
- MoveService: Quota checking (line 126)
- NotifyService: DBRef mapping (line 251)
- LockService: Optimize #TRUE (line 110)
- LocateService: Logic review (line 235)
- WarningService: Variable exits (line 312)
- SqlService: Multiple DBs (line 21)
- HookService: Placeholder (line 86)
- ManipulateSharpObjectService: Permissions (lines 178, 365)

---

## 4. Parser & Visitors (13 TODOs)

### 4.1 SharpMUSHParserVisitor.cs (13 TODOs)
**Priority:** MEDIUM | **Effort:** 3-4 days

**Optimization:**
- Lines 135-136: Function registry
- Line 257: Parser context arguments
- Line 399: General optimization

**Correctness:**
- Lines 177, 206: Permission/depth checking
- Line 383: Channel name matching
- Lines 997, 1064, 1082: Command parsing edge cases
- Line 1259: QREG evaluation

**Risk:** HIGH - Parser is critical infrastructure

---

## 5. Low Priority Items (65 TODOs)

### 5.1 Substitutions (7 TODOs)
- Accented names (lines 34-35)
- Command tracking (lines 80-81)
- Test integration (3 test TODOs)

### 5.2 Handlers (5 TODOs)
- Channel recall buffer
- Telnet protocols (GMCP, MSDP, MSSP)
- Output handling

### 5.3 Helper Functions (5 TODOs)
- Attribute pattern validation (4 instances)
- Pattern type splitting

### 5.4 Documentation & Testing (13 TODOs)
- Helpfiles logging (2)
- Markdown renderer (2)
- String function tests (3)
- Register tests (3)
- Markup tests (3)

### 5.5 Infrastructure (8 TODOs)
- Configuration parsing
- Database interfaces
- Queue/schedule queries
- Extension methods
- Startup handlers

---

## 6. Implementation Priority Matrix

### Must Have (Sprint 1-2) - ~80 TODOs
**Effort:** 10-15 days

1. **AttributeService** improvements (6 TODOs)
2. **Core building commands** (@dig, @open, @tel) (3 TODOs)
3. **Attribute functions** (retrieval, traversal) (6 TODOs)
4. **Dbref functions** (relationships, properties) (8 TODOs)
5. **Queue/execution commands** (@ps, @wait, @trigger) (8 TODOs)

### Should Have (Sprint 3-4) - ~90 TODOs
**Effort:** 8-12 days

1. **Remaining general commands** (30 TODOs)
2. **String/List functions** (26 TODOs)
3. **Service optimizations** (10 TODOs)
4. **Parser improvements** (13 TODOs)

### Nice to Have (Sprint 5+) - ~65 TODOs
**Effort:** 5-8 days

1. **Protocol handlers** (5 TODOs)
2. **Database conversion** (6 TODOs)
3. **Documentation** (13 TODOs)
4. **Technical debt** (15 TODOs)

---

## 7. Dependency Chain

```
Caching Infrastructure
  └─> ValidateService optimizations
  └─> Function registry optimization
  
Permission Framework Clarification
  └─> ManipulateSharpObjectService flags
  └─> AttributeService permissions
  
Pattern Validation System
  └─> HelperFunctions validation
  └─> String function pattern matching
  
Quota System
  └─> MoveService quota checking
  └─> Building command quotas
```

---

## 8. Completed in This PR (62 TODOs Resolved)

### Major Implementations:
- ✅ Permission System fixes
- ✅ AttributeService pattern mode documentation
- ✅ Database pattern matching documentation
- ✅ LockService channel lock evaluation
- ✅ DatabaseCommands @MAPSQL /notify
- ✅ ValidateService enhancements
- ✅ ChannelDecompile implementation
- ✅ Q-Register/Environment Register cleanup
- ✅ Notification system (7 social notifications)
- ✅ @halt/@restart commands (queue management)
- ✅ Channel message notification types
- ✅ Connected owner check
- ✅ Helper function cleanups
- ✅ Documentation updates
- ✅ DbRef/Attribute function improvements
- ✅ **Complete i18n-ready error messaging system**
- ✅ **PennMUSH compatibility (48 new constants)**
- ✅ **Error handling migration (31 cases)**
- ✅ **Build error fixes (13 errors)**
- ✅ **TODO comment cleanup**

### Resolution Rate:
- Original: 283 TODOs
- Current: 235 TODOs
- Resolved: 62 TODOs (21.9%)

---

## 9. Next Steps

### Immediate (Next PR):
1. AttributeService full path returns
2. AttributeService function permission checks
3. `@dig` and `@open` commands
4. Core attribute retrieval functions

### Short Term (Next Sprint):
1. Complete building command suite
2. Implement queue/execution commands  
3. String pattern matching functions
4. Parser function registry optimization

### Long Term (Next Quarter):
1. All High Priority TODOs
2. Protocol handlers
3. Database conversion enhancements
4. Performance optimization pass

---

**Document Maintenance:**
- Updated: 2025-12-30 (fresh analysis)
- Next update: After next major PR
- Re-analyze: Monthly for progress tracking
