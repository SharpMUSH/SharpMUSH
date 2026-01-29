# ANSI TODO Quick Reference

**Last Updated:** 2026-01-29  
**Total TODOs:** 6  
**Estimated Total Effort:** 16-24 hours

---

## Quick Overview

| # | Location | Priority | Effort | Category | Description |
|---|----------|----------|--------|----------|-------------|
| 1 | `Markup.fs:108` | Medium | 4-6h | Architecture | Move ANSI optimization to ANSI.fs module |
| 2 | `ANSI.fs:118` | Low | 6-8h | Feature | Handle ANSI color interpolation with opacity |
| 3 | `MarkupStringModule.fs:49` | High | 4-6h | Performance | Optimize sequential ANSI string initialization |
| 4 | `StringFunctions.cs:1051` | Medium | 3-4h | Bug Fix | Fix ANSI reconstruction ordering in decompose() |
| 5 | `UtilityFunctions.cs:64` | Medium | 6-8h | Architecture | Move ANSI processing to AnsiMarkup module |
| 6 | `StringFunctionUnitTests.cs:257` | High | 2-3h | Bug Fix | Fix 'b' character loss in decompose() |

---

## Implementation Phases

### Phase 1: Bug Fixes (Week 1) - 5-7 hours
**Priority: HIGH**
- Fix decompose() ANSI reconstruction ordering (#4)
- Fix 'b' character bug in decompose() (#6)

### Phase 2: Performance (Week 2) - 4-6 hours
**Priority: MEDIUM**
- Optimize sequential ANSI initialization (#3)

### Phase 3: Architecture (Weeks 3-4) - 10-14 hours
**Priority: MEDIUM**
- Move optimization to ANSI.fs (#1)
- Move ANSI processing to F# module (#5)

### Phase 4: Features (Weeks 5-6) - 6-8 hours
**Priority: LOW**
- Implement ANSI color interpolation (#2)

---

## Key Findings

### Critical Issues
1. **decompose() produces invalid ANSI syntax** - Operations happen in wrong order
2. **'b' character lost in ANSI codes** - Likely related to space replacement

### Performance Opportunities
- Sequential identical ANSI codes generate redundant escape sequences
- Estimated 10-15% size reduction possible with optimization

### Architecture Issues
- ANSI logic scattered between C# and F# code
- Duplicate parsing logic in multiple locations
- Optimization code in wrong module (Markup.fs instead of ANSI.fs)

---

## Dependencies

```
Phase 1 (Bug Fixes)
├── TODO #4: Fix decompose() ordering
└── TODO #6: Fix 'b' character (depends on #4)

Phase 2 (Performance)
└── TODO #3: Sequential optimization (independent)

Phase 3 (Architecture)
├── TODO #1: Move optimization (independent)
└── TODO #5: Move ANSI processing (related to #1)

Phase 4 (Features)
└── TODO #2: Color interpolation (independent)
```

---

## Risk Assessment

| TODO | Risk Level | Mitigation |
|------|-----------|------------|
| #1 | Low | Isolated refactoring, good test coverage |
| #2 | Low | New feature, doesn't affect existing code |
| #3 | Medium | Changes string generation, needs careful testing |
| #4 | Medium | Core function change, comprehensive tests needed |
| #5 | High | Large refactoring, use feature flag + gradual rollout |
| #6 | Low | Bug fix with clear scope |

---

## Success Criteria

### Correctness
- [ ] All test cases in StringFunctionUnitTests.cs passing
- [ ] No character loss in decompose()
- [ ] Valid ANSI syntax in all outputs

### Performance
- [ ] 10-15% reduction in ANSI string size
- [ ] No performance regression
- [ ] Faster string concatenation

### Code Quality
- [ ] ANSI logic centralized
- [ ] No duplicate code
- [ ] Clear module boundaries

---

## Files Affected

### F# Files
- `SharpMUSH.MarkupString/Markup/Markup.fs`
- `SharpMUSH.MarkupString/Markup/ANSILibrary/ANSI.fs`
- `SharpMUSH.MarkupString/MarkupStringModule.fs`

### C# Files
- `SharpMUSH.Implementation/Functions/StringFunctions.cs`
- `SharpMUSH.Implementation/Functions/UtilityFunctions.cs`

### Test Files
- `SharpMUSH.Tests/Functions/StringFunctionUnitTests.cs`

---

## Detailed Documentation

For complete analysis, implementation strategies, and code examples, see:
- **[ANSI_TODO_REPORT.md](./ANSI_TODO_REPORT.md)** - Full technical report

---

## Next Steps

1. **Review this summary and full report**
2. **Create GitHub issues** for each TODO
3. **Schedule Phase 1** (bug fixes) for immediate implementation
4. **Plan subsequent phases** based on priorities and resources

---

*Quick reference guide for ANSI TODO implementation*
