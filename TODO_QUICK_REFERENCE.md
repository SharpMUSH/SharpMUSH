# TODO Resolution Strategy - Quick Reference

**Version**: 1.0  
**Date**: 2026-01-27  
**Total TODOs**: 80 (37 production + 43 test-related)

## TL;DR - Start Here

### Immediate Actions (Week 1)
1. ✅ **Function Resolution Service** - Extract to dedicated service (enables caching)
2. ✅ **Test Infrastructure** - Add attribute setting and connection mocking
3. ✅ **CRON Service** - Move from StartupHandler to dedicated service

### Next Steps (Weeks 2-6)
4. ✅ **Parser Performance** - Reduce allocations, optimize context passing
5. ✅ **Command Indexing** - Cache single-token commands
6. ✅ **Attribute Management** - Retroactive updates, regex validation

### Critical Path
```
Function Resolution → Parser Performance → Attribute Management → Test Infrastructure → DONE
     (1 week)              (2 weeks)            (2 weeks)            (1 week)
```

---

## By The Numbers

| Metric | Value |
|--------|-------|
| Total TODOs | 80 |
| Production Code | 37 |
| Test-Related | 43 |
| Total Effort | 40-60 weeks (single dev) |
| Phases | 4 |
| Critical Path | 6-7 weeks |

---

## Categories at a Glance

### Production Code (37)

| Category | Count | Effort | Priority |
|----------|-------|--------|----------|
| Infrastructure & Services | 10 | 10-15w | High/Med |
| Parser & Execution | 8 | 6-9w | High |
| Command Features | 6 | 5-8w | Medium |
| Function Features | 9 | 5-7w | Low |
| ANSI/Markup System | 6 | 4-6w | Low |

### Test-Related (43)

| Category | Count | Effort | Priority |
|----------|-------|--------|----------|
| Skipped/Failing Tests | 28 | 8-12w | High |
| Test Creation | 15 | 2-3w | Medium |

---

## Top 10 Priority Items

### Must Do First (High Impact, Enables Others)
1. **Function Resolution Service** (1w) - Architectural foundation
2. **Parser Performance Optimizations** (2w) - 10-20% speed improvement
3. **Test Infrastructure** (1w) - Enables test fixes
4. **Command Indexing** (5d) - Faster command lookup
5. **Attribute Management** (2w) - Complete feature set

### Should Do Second (Good ROI)
6. **Parser Features** (2w) - lsargs, Q-registers, stack rewinding
7. **Fix Database Tests** (1w) - Reduce test failures
8. **CRON Service** (1w) - Clean architecture
9. **Channel Matching** (5d) - Better UX
10. **ANSI Integration** (5d) - Code organization

---

## 4 Phases Overview

### Phase 1: Foundation (4-6 weeks)
**Goal**: Establish architectural improvements

- Function Resolution Service
- Parser Performance Optimizations
- CRON Service Extraction
- Test Infrastructure

**Deliverables**: 10-20% performance gain, better architecture

### Phase 2: Performance & Features (4-6 weeks)
**Goal**: Add caching and complete high-value features

- Command Indexing
- Attribute Management
- Parser Features (lsargs, stack rewinding)
- Fix High-Priority Tests

**Deliverables**: Complete attribute system, enhanced parser

### Phase 3: Enhancements (6-8 weeks)
**Goal**: Add remaining features and fix all tests

- ANSI/Markup System improvements
- Database Abstraction (multi-DB support)
- Channel Improvements
- Fix Remaining Tests

**Deliverables**: 100% test pass rate, multi-DB support

### Phase 4: Advanced Features (8-12 weeks)
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

## Effort by Priority

| Priority | TODO Count | Weeks | % of Total |
|----------|-----------|-------|------------|
| **High** | 15 | 10-15 | 19% |
| **Medium** | 30 | 15-22 | 38% |
| **Low** | 20 | 8-12 | 25% |
| **Defer** | 15 | 12-18 | 19% |

---

## Success Criteria

### Phase 1 Complete
- [ ] Function Resolution Service extracted
- [ ] Parser 10-20% faster
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

**Team Timeline**: 8-12 weeks (vs 22-32 weeks solo)

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

4. **What's the effort?**
   - <1 week → Can fit in current phase
   - 1-2 weeks → Plan as dedicated item
   - >2 weeks → Consider breaking into sub-tasks

---

## One-Page Action Plan

### Week 1
- [ ] Extract Function Resolution Service
- [ ] Set up Test Infrastructure
- [ ] Extract CRON Service

### Weeks 2-3
- [ ] Optimize Parser Performance
- [ ] Begin Attribute Management

### Week 4
- [ ] Implement Command Indexing
- [ ] Complete Attribute Management

### Weeks 5-6
- [ ] Implement Parser Features
- [ ] Fix High-Priority Tests

### Weeks 7-12
- [ ] ANSI/Markup improvements
- [ ] Database Abstraction
- [ ] Fix remaining tests

### Weeks 13-20
- [ ] Channel improvements
- [ ] Create missing tests
- [ ] Code cleanup

### Weeks 21-32
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
