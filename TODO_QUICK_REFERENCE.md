# TODO Resolution Strategy - Quick Reference

**Version**: 1.0  
**Date**: 2026-01-27  
**Total TODOs**: 80 (37 production + 43 test-related)

## TL;DR - Start Here

### Immediate Actions
1. ✅ **Function Resolution Service** - Extract to dedicated service (enables caching)
2. ✅ **Test Infrastructure** - Add attribute setting and connection mocking
3. ✅ **CRON Service** - Move from StartupHandler to dedicated service

### Next Steps
4. ✅ **Parser Performance** - Reduce allocations, optimize context passing
5. ✅ **Command Indexing** - Cache single-token commands
6. ✅ **Attribute Management** - Retroactive updates, regex validation

### Critical Path
```
Function Resolution → Parser Performance → Attribute Management → Test Infrastructure
```

---

## By The Numbers

| Metric | Value |
|--------|-------|
| Total TODOs | 80 |
| Production Code | 37 |
| Test-Related | 43 |
| Phases | 4 |

---

## Categories at a Glance

### Production Code (37)

| Category | Count | Priority |
|----------|-------|----------|
| Infrastructure & Services | 10 | High/Med |
| Parser & Execution | 8 | High |
| Command Features | 6 | Medium |
| Function Features | 9 | Low |
| ANSI/Markup System | 6 | Low |

### Test-Related (43)

| Category | Count | Priority |
|----------|-------|----------|
| Skipped/Failing Tests | 28 | High |
| Test Creation | 15 | Medium |

---

## Top 10 Priority Items

### Must Do First (High Impact, Enables Others)
1. **Function Resolution Service** - Architectural foundation
2. **Parser Performance Optimizations** - Significant speed improvement
3. **Test Infrastructure** - Enables test fixes
4. **Command Indexing** - Faster command lookup
5. **Attribute Management** - Complete feature set

### Should Do Second (Good ROI)
6. **Parser Features** - lsargs, Q-registers, stack rewinding
7. **Fix Database Tests** - Reduce test failures
8. **CRON Service** - Clean architecture
9. **Channel Matching** - Better UX
10. **ANSI Integration** - Code organization

---

## 4 Phases Overview

### Phase 1: Foundation
**Goal**: Establish architectural improvements

- Function Resolution Service
- Parser Performance Optimizations
- CRON Service Extraction
- Test Infrastructure

**Deliverables**: Performance gains, better architecture

### Phase 2: Performance & Features
**Goal**: Add caching and complete high-value features

- Command Indexing
- Attribute Management
- Parser Features (lsargs, stack rewinding)
- Fix High-Priority Tests

**Deliverables**: Complete attribute system, enhanced parser

### Phase 3: Enhancements
**Goal**: Add remaining features and fix all tests

- ANSI/Markup System improvements
- Database Abstraction (multi-DB support)
- Channel Improvements
- Fix Remaining Tests

**Deliverables**: 100% test pass rate, multi-DB support

### Phase 4: Advanced Features
**Goal**: Implement complex subsystems

- Websocket/OOB Subsystem (4 TODOs)
- Text File System
- Economy System
- Final Polish

**Deliverables**: Modern features, 100% TODO resolution

---

## Key Dependencies

### No Dependencies (Can Start Now)
- Database Abstraction
- Text File System
- Pueblo Escape Stripping
- Channel Name Matching
- CRON Service
- Economy System
- SPEAK() Integration
- pcreate() Enhancement
- Most Markup System items

### Depends on Function Resolution Service
- Parser Performance Optimizations
- Command Indexing
- Test Infrastructure

### Depends on Parser Performance
- Attribute Management (partially)
- Some Parser Features

### Depends on Test Infrastructure
- All Test Fixes
- Test Creation

---

## Risk Assessment

### 🔴 High Risk
- **Websocket/OOB Subsystem** - Protocol complexity, client compatibility
- **Multi-Database Support** - Breaking changes, migration complexity
- **Parser Performance** - Risk of breaking existing functionality

### 🟡 Medium Risk
- **Attribute Management** - Data consistency, retroactive updates
- **Text File System** - Security vulnerabilities
- **Test Infrastructure** - Breaking existing tests

### 🟢 Low Risk
- **ANSI/Markup Improvements** - Isolated changes
- **Command Enhancements** - Additive features
- **Function Additions** - New functionality

---

## Priority Distribution

| Priority | TODO Count | % of Total |
|----------|-----------|------------|
| **High** | 15 | 19% |
| **Medium** | 30 | 38% |
| **Low** | 20 | 25% |
| **Defer** | 15 | 19% |

---

## Success Criteria

### Phase 1 Complete
- [ ] Function Resolution Service extracted
- [ ] Parser performance improved
- [ ] Test infrastructure supports mocking
- [ ] CRON service separated

### Phase 2 Complete
- [ ] Commands indexed and cached
- [ ] Attribute management complete
- [ ] Parser features implemented
- [ ] <10 failing tests

### Phase 3 Complete
- [ ] All tests passing
- [ ] Multi-database support
- [ ] ANSI system optimized
- [ ] Channel matching improved

### Phase 4 Complete
- [ ] Websocket support available
- [ ] Text file system working
- [ ] Economy system implemented
- [ ] **Zero TODOs remaining**

---

## Parallel Work Streams (for teams)

With 3-5 developers, work can proceed in parallel:

### Stream 1: Core Architecture
Developer focused on parser and function infrastructure

### Stream 2: Services
Developer focused on CRON, channels, database abstraction

### Stream 3: Features
Developer focused on attributes, parser features, string functions

### Stream 4: Tests
Developer focused on test infrastructure and fixes

### Stream 5: ANSI/Markup
Developer focused on markup system improvements

---

## Quick Decision Tree

**Starting a new TODO?**

1. **Does it have dependencies?**
   - Yes → Are dependencies complete? (If no, do dependencies first)
   - No → Proceed

2. **What's the priority?**
   - High → Do now (Phase 1-2)
   - Medium → Do soon (Phase 2-3)
   - Low → Do later (Phase 3-4)
   - Defer → Schedule for Phase 4

3. **What's the risk?**
   - High → Need careful planning, extra testing
   - Medium → Standard approach, good test coverage
   - Low → Can implement quickly

---

## One-Page Action Plan

### Phase 1: Foundation
- [ ] Extract Function Resolution Service
- [ ] Set up Test Infrastructure
- [ ] Extract CRON Service
- [ ] Optimize Parser Performance
- [ ] Begin Attribute Management

### Phase 2: Performance & Features
- [ ] Implement Command Indexing
- [ ] Complete Attribute Management
- [ ] Implement Parser Features
- [ ] Fix High-Priority Tests

### Phase 3: Enhancements
- [ ] ANSI/Markup improvements
- [ ] Database Abstraction
- [ ] Fix remaining tests
- [ ] Channel improvements
- [ ] Create missing tests

### Phase 4: Advanced Features
- [ ] Websocket subsystem
- [ ] Text file system
- [ ] Economy system
- [ ] Final polish

---

## Resources

- **Full Analysis**: `TODO_DEPENDENCY_ANALYSIS.md`
- **Visual Graphs**: `TODO_DEPENDENCY_GRAPH.md`
- **Previous Work**: `FINAL_TODO_STATUS.md`, `TODO_FINAL_ANALYSIS.md`
- **Original List**: `grep -r "TODO" --include="*.cs" --include="*.fs"`

---

## Contact & Updates

**Document Owner**: Development Team  
**Last Review**: 2026-01-27  
**Next Review**: After Phase 1 completion  
**Update Frequency**: After each phase

---

*For detailed analysis, dependency graphs, and implementation strategies, see the full documentation.*
