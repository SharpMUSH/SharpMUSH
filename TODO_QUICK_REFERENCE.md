# SharpMUSH TODO Quick Reference

**Last Updated:** 2025-12-30  
**Total TODOs:** 235  
**Completed in This PR:** 62

Quick navigation guide for developers working on SharpMUSH TODOs.

---

## TODOs by File

### Commands
| File | TODOs | Priority | Effort |
|------|-------|----------|--------|
| GeneralCommands.cs | 38 | HIGH | 3-5 days |
| BuildingCommands.cs | 9 | HIGH | 1-2 days |
| MoreCommands.cs | 10 | MEDIUM | 1-2 days |
| WizardCommands.cs | 4 | LOW | 0.5-1 day |
| **TOTAL** | **61** | - | **5.5-10 days** |

### Functions
| File | TODOs | Priority | Effort |
|------|-------|----------|--------|
| StringFunctions.cs | 18 | MEDIUM | 2-3 days |
| DbrefFunctions.cs | 15 | HIGH | 2-3 days |
| AttributeFunctions.cs | 12 | HIGH | 2-3 days |
| ListFunctions.cs | 8 | MEDIUM | 1-2 days |
| MathFunctions.cs | 5 | LOW | 0.5-1 day |
| UtilityFunctions.cs | 5 | LOW-MED | 1 day |
| LogicFunctions.cs | 3 | LOW | 0.5 day |
| ColorFunctions.cs | 2 | LOW | 0.5 day |
| ChannelFunctions.cs | 2 | LOW | 0.5 day |
| **TOTAL** | **70** | - | **10-17 days** |

### Services
| File | TODOs | Priority | Effort |
|------|-------|----------|--------|
| PennMUSHDatabaseConverter.cs | 6 | LOW | 1-2 days |
| AttributeService.cs | 6 | HIGH | 1-2 days |
| ValidateService.cs | 3 | MEDIUM | 0.5-1 day |
| ManipulateSharpObjectService.cs | 2 | MEDIUM | 1-2 days |
| Other Services (8 files) | 8 | LOW-MED | 2-3 days |
| **TOTAL** | **25** | - | **5-10 days** |

### Parser & Visitors
| File | TODOs | Priority | Effort |
|------|-------|----------|--------|
| SharpMUSHParserVisitor.cs | 13 | MEDIUM | 3-4 days |

### Other Components
| Component | TODOs | Priority | Effort |
|-----------|-------|----------|--------|
| Substitutions | 7 | LOW-MED | 0.5-1 day |
| Handlers | 5 | LOW | 2-3 days |
| Helper Functions | 5 | MEDIUM | 0.5-1 day |
| Documentation | 2 | LOW | Trivial |
| Markdown Renderer | 2 | LOW | 0.5 day |
| Tests | 9 | MEDIUM | Variable |
| Infrastructure | 8 | LOW-MED | 1-2 days |
| **TOTAL** | **38** | - | **5-9 days** |

---

## TODOs by Priority

### 🔴 HIGH Priority (80 TODOs)
**Target:** Sprint 1-2 | **Effort:** 10-15 days

**Commands:**
- BuildingCommands.cs: `@dig`, `@open`, `@tel` (3)
- GeneralCommands.cs: Queue/execution commands (8)

**Functions:**
- AttributeFunctions.cs: Retrieval & traversal (6)
- DbrefFunctions.cs: Relationships & properties (8)

**Services:**
- AttributeService.cs: All improvements (6)

**Total:** ~31 critical TODOs from above + 49 in related areas

### 🟡 MEDIUM Priority (90 TODOs)
**Target:** Sprint 3-4 | **Effort:** 8-12 days

**Commands:**
- GeneralCommands.cs: Object manipulation & communication (20)
- MoreCommands.cs: Social commands (10)

**Functions:**
- StringFunctions.cs: Pattern matching & manipulation (18)
- ListFunctions.cs: Advanced list operations (8)

**Services:**
- ValidateService.cs: Caching & validation (3)
- Other service optimizations (8)

**Parser:**
- SharpMUSHParserVisitor.cs: Optimizations (13)

**Total:** ~80 medium priority TODOs

### 🟢 LOW Priority (65 TODOs)
**Target:** Sprint 5+ | **Effort:** 5-8 days

**Commands:**
- GeneralCommands.cs: Admin commands (10)
- WizardCommands.cs: Wizard tools (4)

**Functions:**
- Math/Logic/Utility/Color functions (15)

**Services:**
- PennMUSHDatabaseConverter.cs: Migration improvements (6)

**Other:**
- Protocol handlers (5)
- Documentation & tests (13)
- Technical debt (12)

**Total:** ~65 lower priority items

---

## Quick Search Guide

### By Feature Area

**Building & World Creation:**
- `BuildingCommands.cs`: 9 TODOs (HIGH)
- `DbrefFunctions.cs`: 15 TODOs (HIGH)
- `AttributeFunctions.cs`: 12 TODOs (HIGH)

**Scripting & Automation:**
- `GeneralCommands.cs` (Queue): 8 TODOs (HIGH)
- `StringFunctions.cs`: 18 TODOs (MEDIUM)
- `AttributeService.cs`: 6 TODOs (HIGH)

**Player Interaction:**
- `GeneralCommands.cs` (Communication): 8 TODOs (MEDIUM)
- `MoreCommands.cs`: 10 TODOs (MEDIUM)

**Administration:**
- `GeneralCommands.cs` (Admin): 10 TODOs (LOW)
- `WizardCommands.cs`: 4 TODOs (LOW)
- `ManipulateSharpObjectService.cs`: 2 TODOs (MEDIUM)

**Performance:**
- `SharpMUSHParserVisitor.cs`: 13 TODOs (MEDIUM)
- `ValidateService.cs`: 3 TODOs (MEDIUM)
- `LockService.cs`: 1 TODO (MEDIUM)

---

## Recently Completed (This PR)

### Infrastructure
✅ Error messaging system (48 new constants)  
✅ PennMUSH compatibility  
✅ Unified error handling (NotifyService.NotifyAndReturn)

### Commands
✅ @halt/@restart (queue management)  
✅ @MAPSQL /notify switch  
✅ ChannelDecompile  

### Services
✅ AttributeService pattern modes  
✅ ValidateService (PlayerAlias, LockType)  
✅ Permission system fixes  
✅ Lock service channel locks  

### Functions
✅ DbRef improvements (7 TODOs)  
✅ Attribute improvements (multiple)

### Other
✅ Notification system (7 social notifications)  
✅ Channel message types  
✅ Connected owner checks  
✅ Build error fixes (13 errors)  
✅ TODO comment cleanup (obsolete comments)

**Total Resolved:** 62 TODOs (21.9% reduction)

---

## Implementation Suggestions

### Start Here (Quick Wins)
1. AttributeService.cs lines 133, 233 - Return full paths
2. ValidateService.cs line 125 - Cache attribute names  
3. HelperFunctions.cs - Attribute pattern validation
4. Documentation/Helpfiles.cs - Add logging

### High Impact (Next Priority)
1. GeneralCommands.cs - Queue commands (@ps, @wait, @trigger)
2. BuildingCommands.cs - Core building (@dig, @open, @tel)
3. AttributeFunctions.cs - Retrieval functions
4. DbrefFunctions.cs - Relationship functions

### Complex but Critical
1. SharpMUSHParserVisitor.cs - Function registry optimization
2. StringFunctions.cs - Pattern matching suite
3. AttributeService.cs - Permission checks

---

## Development Workflow

### Before Starting
1. Check dependencies in TODO_IMPLEMENTATION_PLAN.md
2. Review related TODOs in same file
3. Check for blocking issues

### While Working
1. Update TODO comment when started
2. Add implementation notes
3. Write tests for new functionality

### After Completion
1. Remove TODO comment
2. Update both TODO documents
3. Add to "Completed" section in PR description

---

## File Locations

### Core Implementation
```
Commands: SharpMUSH.Implementation/Commands/
Functions: SharpMUSH.Implementation/Functions/
Services: SharpMUSH.Library/Services/
Parser: SharpMUSH.Implementation/Visitors/
Handlers: SharpMUSH.Implementation/Handlers/
```

### Supporting Code
```
Extensions: SharpMUSH.Library/Extensions/
Helpers: SharpMUSH.Library/HelperFunctions.cs
Configuration: SharpMUSH.Configuration/
Tests: SharpMUSH.Tests/
```

---

## Contact & Questions

For questions about specific TODOs:
- Check TODO_IMPLEMENTATION_PLAN.md for detailed analysis
- Review git history for context on surrounding code
- Check related tests for expected behavior
- Ask in project discussions for clarification

---

**Last Analysis:** 2025-12-30  
**Next Update:** After next major PR  
**Analysis Script:** `/tmp/analyze_todos.py`
