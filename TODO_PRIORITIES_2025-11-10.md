# TODO Priorities - November 10, 2025

**Total TODOs:** 282  
**Focus:** Action items for completing SharpMUSH to 100%

---

## 🚨 CRITICAL - Must Address (10 items)

### 1. SEVERE Optimization
**CommandDiscoveryService.cs:37** - Attribute scanning optimization  
**Impact:** Performance at scale  
**Priority:** 🔴 CRITICAL  
**Effort:** High  
Scanning all attributes for each command match is inefficient and will not scale.

### 2. Core Security
**PermissionService.cs:37** - Implement attribute-based permission controls  
**Impact:** Security model completeness  
**Priority:** 🔴 CRITICAL  
**Effort:** Medium-High  
Required for proper attribute-level permissions.

### 3. Safety Issue
**SQLFunctions.cs:138** - DANGER: mapsql() transformation bug  
**Impact:** Data integrity  
**Priority:** 🔴 CRITICAL  
**Effort:** Medium  
Potential attribute corruption risk needs immediate attention.

### 4. Service Completions (3 items)
- **LockService.cs:120** - Complete lock service (NotImplementedException)
- **IMoveService.cs:3** - Implement move service interface
- **HookService.cs:77** - Replace placeholder hook implementation

**Priority:** 🔴 CRITICAL  
**Effort:** High  
Core service infrastructure must be complete.

### 5. Pattern Modes (2 items)
- **AttributeService.cs:354** - Implement pattern modes
- **AttributeService.cs:371** - Implement pattern modes

**Priority:** 🔴 CRITICAL  
**Effort:** Medium  
Required for advanced attribute operations.

### 6. Async Issues
**LocateService.cs:220** - Fix async implementation  
**Priority:** 🟠 HIGH  
**Effort:** Medium  
Potential race conditions or deadlocks.

---

## 🟠 HIGH PRIORITY - Should Address (15 items)

### Utility Functions Needing Implementation

These are stubs that need full implementation for feature completeness:

1. **UtilityFunctions.cs:1034** - grep() functionality (attribute searching)
2. **UtilityFunctions.cs:245** - atrlock() (lock operations)
3. **UtilityFunctions.cs:337** - clone() (object cloning)
4. **UtilityFunctions.cs:420** - dig() (room creation)
5. **UtilityFunctions.cs:532** - itext() (text file system)
6. **UtilityFunctions.cs:569** - link() (exit linking)
7. **UtilityFunctions.cs:680** - open() (exit creation)
8. **UtilityFunctions.cs:794** - render() (code evaluation)

**Priority:** 🟠 HIGH  
**Effort:** Medium each  
**Total:** 8 functions to implement

### Infrastructure TODOs

9. **AttributeFunctions.cs:1895** - Zone infrastructure implementation  
10. **InformationFunctions.cs:173** - PID tracking system  
11. **InformationFunctions.cs:449** - WAIT and INDEPENDENT queue handling  
12. **InformationFunctions.cs:467** - Database-wide object counting  
13. **DbrefFunctions.cs:290** - Follower tracking system  
14. **CommunicationFunctions.cs:350** - Zone emission support  
15. **AttributeService.cs:461** - Object permissions

**Priority:** 🟠 HIGH  
**Effort:** Medium-High each

---

## 🟡 MEDIUM PRIORITY - Optimizations (10 items)

### Performance Improvements

1. **PermissionService.cs:40,89** - Optimize for list operations (2 items)
2. **ValidateService.cs:144** - Cache by name
3. **LockService.cs:110** - Optimize #TRUE calls (no cache needed)
4. **BooleanExpressionParser.cs:11** - Cache evaluation optimization
5. **AttributeHelpers.cs:13** - Cache attribute configuration
6. **SharpMUSHParserVisitor.cs:243,337,375,412** - Various optimizations (4 items)

**Priority:** 🟡 MEDIUM  
**Effort:** Low-Medium each  
**Impact:** Performance improvement, not correctness

---

## 🟢 LOW PRIORITY - Testing & Enhancement (175 items)

### Testing TODOs (19 items)

**Files needing test attention:**
- RegistersUnitTests.cs (3) - Server integration tests
- JsonFunctionUnitTests.cs (2) - Attribute setting, connection mocking
- DbrefFunctionUnitTests.cs (1) - Tel() implementation
- ListFunctionUnitTests.cs (4) - Edge cases
- StringFunctionUnitTests.cs (4) - Decompose functions
- MathFunctionUnitTests.cs (1) - Return value fix
- CommandUnitTests.cs (1) - Eval vs noparse
- DatabaseCommandTests.cs (1) - Loop bug investigation
- InsertAt.cs (1) - Optimize case investigation
- RoomsAndMovementTests.cs (1) - Add tests

**Priority:** 🟢 LOW-MEDIUM  
**Effort:** Low-Medium  
**Impact:** Test coverage completeness

### Enhancement TODOs (166 items)

These are improvements, not bugs:
- Code quality improvements
- Better error messages
- Edge case handling
- Feature refinements
- Standardization
- Validation improvements

**Priority:** 🟢 LOW  
**Effort:** Low each  
**Impact:** Quality of life, not functionality

### Documentation (6 items)

- Logging improvements (2)
- Logic review notes (2)
- User documentation (2)

**Priority:** 🟢 LOW  
**Effort:** Low

### Refactoring (1 item)

- SharpMUSHParserVisitor.cs:188 - Context reconsideration

**Priority:** 🟢 LOW  
**Effort:** Low

---

## 📊 TODO Summary by File (Top 20)

| File | Total | Critical | High | Medium | Low |
|------|-------|----------|------|--------|-----|
| **UtilityFunctions.cs** | 24 | 0 | 22 | 0 | 2 |
| **UtilityCommands.cs** | 18 | 0 | 13 | 0 | 5 |
| **ConnectionFunctions.cs** | 11 | 0 | 5 | 0 | 6 |
| **MoreCommands.cs** | 10 | 0 | 2 | 0 | 8 |
| **DbrefFunctions.cs** | 9 | 0 | 5 | 0 | 4 |
| **PermissionService.cs** | 6 | 2 | 1 | 2 | 1 |
| **HelperFunctions.cs** | 5 | 0 | 0 | 0 | 5 |
| **StringFunctions.cs** | 5 | 0 | 0 | 0 | 5 |
| **Substitutions.cs** | 4 | 0 | 0 | 0 | 4 |
| **InformationFunctions.cs** | 4 | 0 | 4 | 0 | 0 |
| **CommunicationFunctions.cs** | 4 | 0 | 2 | 0 | 2 |
| **ChannelFunctions.cs** | 4 | 0 | 0 | 0 | 4 |
| **ChannelCommands.cs** | 4 | 0 | 0 | 0 | 4 |
| **WizardCommands.cs** | 4 | 0 | 0 | 0 | 4 |
| **StringFunctionUnitTests.cs** | 4 | 0 | 0 | 0 | 4 |
| **ListFunctionUnitTests.cs** | 4 | 0 | 0 | 0 | 4 |
| **ArangoDatabase.cs** | 4 | 0 | 2 | 0 | 2 |
| **AttributeService.cs** | 4 | 2 | 1 | 0 | 1 |
| **LockService.cs** | 2 | 1 | 0 | 1 | 0 |
| **ValidateService.cs** | 2 | 0 | 0 | 1 | 1 |

---

## 🎯 Recommended Action Plan

### Phase 1: Critical Items (1-2 weeks)

**Week 1 - Core Infrastructure:**
1. Fix CommandDiscoveryService optimization (SEVERE)
2. Complete Permission Service attribute controls
3. Fix mapsql() safety issue
4. Complete Lock Service
5. Implement Move Service interface
6. Replace Hook Service placeholder
7. Implement Attribute Pattern Modes
8. Fix LocateService async issues

**Estimated Effort:** 60-80 hours  
**Impact:** Core system stability and security

### Phase 2: High Priority Items (1-2 weeks)

**Week 2-3 - Utility Functions & Infrastructure:**
1. Implement 8 utility functions (grep, atrlock, clone, dig, itext, link, open, render)
2. Implement zone infrastructure
3. Implement PID tracking
4. Implement queue handling improvements
5. Implement follower tracking
6. Complete object counting
7. Fix object permissions

**Estimated Effort:** 80-100 hours  
**Impact:** Feature completeness

### Phase 3: Optimizations (1 week)

**Week 4 - Performance:**
1. Address all 10 optimization TODOs
2. Implement caching strategies
3. Optimize list operations
4. Parser visitor optimizations

**Estimated Effort:** 20-30 hours  
**Impact:** Performance improvements

### Phase 4: Polish (Ongoing)

**Testing & Enhancement:**
- Address testing TODOs as time permits
- Enhance features based on usage feedback
- Documentation improvements
- Code refactoring

**Estimated Effort:** Ongoing  
**Impact:** Quality of life

---

## 📈 Progress Tracking

### Success Metrics

**Phase 1 Complete When:**
- ✅ 0 CRITICAL TODOs remain
- ✅ All core services implemented
- ✅ Security model complete
- ✅ No SEVERE optimizations pending

**Phase 2 Complete When:**
- ✅ All HIGH priority TODOs addressed
- ✅ Utility functions implemented
- ✅ Infrastructure complete

**Phase 3 Complete When:**
- ✅ All MEDIUM priority optimizations done
- ✅ Performance targets met

**Project Complete When:**
- ✅ All 36 commands implemented
- ✅ All CRITICAL and HIGH TODOs resolved
- ✅ 90%+ of MEDIUM TODOs resolved
- ✅ 1,200+ tests passing
- ✅ 0 build warnings/errors

---

## 🔍 TODO Health Indicators

### Positive Signs ✅
- Only 10 critical items
- Most TODOs (58.9%) are enhancements, not bugs
- Testing coverage is good (only 6.7% test TODOs)
- Optimization TODOs are identified and manageable
- Documentation needs are minimal

### Areas Needing Attention ⚠️
- 80 major implementation TODOs (but many are known stubs)
- Command Discovery optimization is SEVERE
- Some core service completions pending
- Zone infrastructure affects multiple features

### Overall Assessment 🎉
**The TODO list is healthy and manageable.** Most items are enhancements or known stubs. Critical items are few and well-defined. The project is in excellent shape for completion.

---

**Last Updated:** November 10, 2025  
**Next Review:** After Phase 1 completion or at 90% feature completion  
**Status:** 🎯 **CLEAR PATH TO COMPLETION**
