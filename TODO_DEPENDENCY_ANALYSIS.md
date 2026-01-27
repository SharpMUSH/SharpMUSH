# TODO Items - Dependency Analysis and Resolution Strategy

**Generated**: 2026-01-27  
**Total TODO Items**: 80 (37 production code + 43 test-related)

## Executive Summary

This document provides a comprehensive analysis of all remaining TODO items in the SharpMUSH codebase, organized by category, dependency relationships, and recommended implementation order. The analysis includes a dependency graph showing which TODOs must be completed before others, enabling a strategic approach to resolving all remaining items.

---

## Production Code TODOs (37 items)

### Category 1: Infrastructure & Core Services (10 items)
These are foundational items that other TODOs depend on.

#### 1.1 Database Abstraction Layer
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: High  
**Priority**: Medium

- **SharpMUSH.Library/Services/SqlService.cs:21**
  - Support multiple SQL database types (PostgreSQL, SQLite, etc.)
  - **Rationale**: Currently limited to specific database, needs abstraction
  - **Effort**: 2-3 weeks (requires abstraction layer, connection pooling, migrations)
  - **Blocks**: None directly, but enables multi-database deployments

#### 1.2 Function Resolution Service
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: High

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:350**
  - Move function resolution to a dedicated Library Service
  - **Rationale**: Better separation of concerns, enables caching
  - **Effort**: 1 week (service extraction, dependency injection setup)
  - **Blocks**: Function caching implementation
  - **Benefits**: Cleaner architecture, testability, performance optimization foundation

#### 1.3 Text File System
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: High  
**Priority**: Low

- **SharpMUSH.Implementation/Functions/UtilityFunctions.cs:1529**
  - stext() requires text file system integration
  - **Rationale**: File-based content management for help files, text storage
  - **Effort**: 2-4 weeks (file abstraction, security model, quota system)
  - **Blocks**: None
  - **Security Considerations**: Path validation, access control, quota enforcement

#### 1.4 CRON/Scheduled Task Service
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Medium

- **SharpMUSH.Server/StartupHandler.cs:19**
  - Move CRON/scheduled task management to dedicated service
  - **Rationale**: Separation of concerns, better testability
  - **Effort**: 1 week (service extraction, configuration model)
  - **Blocks**: None
  - **Benefits**: Cleaner startup code, reusable scheduling infrastructure

#### 1.5 Database Conversion
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Low  
**Priority**: Low

- **SharpMUSH.Library/Services/DatabaseConversion/PennMUSHDatabaseConverter.cs:823**
  - Implement proper Pueblo escape stripping
  - **Rationale**: Compatibility with PennMUSH database imports
  - **Effort**: 2-3 days (regex patterns, testing with real data)
  - **Blocks**: None

#### 1.6 API Design Improvements
**Count**: 2 TODOs  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.Library/ISharpDatabase.cs:145**
  - Return type for attribute pattern queries needs reconsideration
  - **Rationale**: Current return type may not be optimal for all use cases
  - **Effort**: 3-5 days (API redesign, migration of consumers)
  - **Blocks**: None
  - **Breaking Change**: Yes, requires consumer updates

- **SharpMUSH.Library/Requests/QueueCommandListRequest.cs:7,24** (2 instances)
  - Return the new PID for output/tracking
  - **Rationale**: Commands need to track queued process IDs
  - **Effort**: 1 week (IRequest interface changes, handler updates)
  - **Blocks**: None
  - **Breaking Change**: Possibly, depends on implementation approach

#### 1.7 Channel Name Matching
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:676**
  - Improve channel name matching with fuzzy/partial matching
  - **Rationale**: Better UX, abbreviation support
  - **Effort**: 3-5 days (matching algorithm, ambiguity handling)
  - **Blocks**: None

---

### Category 2: Parser & Execution Engine (8 items)
Parser optimizations and enhancements.

#### 2.1 Parser Performance Optimizations
**Count**: 3 TODOs  
**Dependencies**: Function Resolution Service (1.2) for best results  
**Complexity**: Medium-High  
**Priority**: High

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:530**
  - Pass ParserContexts directly as arguments instead of creating new instances
  - **Rationale**: Reduce allocations, improve performance
  - **Effort**: 1-2 weeks (parser refactoring, testing)
  - **Blocks**: None
  - **Benefits**: 10-20% performance improvement (estimated)

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:1388**
  - Implement parsed message alternative for better performance
  - **Rationale**: Avoid re-parsing messages
  - **Effort**: 1 week (caching strategy, invalidation)
  - **Blocks**: None
  - **Benefits**: Faster message handling

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:470**
  - Depth checking is done here before argument refinement (informational)
  - **Rationale**: Note about current implementation order
  - **Effort**: None (documentation only)
  - **Type**: Informational comment

#### 2.2 Parser Feature Enhancements
**Count**: 3 TODOs  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Medium

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:1301**
  - Investigate if single-token commands should support argument splitting
  - **Rationale**: Research needed to determine correct behavior
  - **Effort**: 2-3 days (research, testing, decision)
  - **Blocks**: None
  - **Type**: Investigation/Research

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:1369**
  - Implement lsargs (list-style arguments) support
  - **Rationale**: Feature compatibility with PennMUSH
  - **Effort**: 1 week (parser changes, testing)
  - **Blocks**: None

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:1530**
  - Handle Q-registers containing evaluation strings properly
  - **Rationale**: Deferred evaluation support
  - **Effort**: 1-2 weeks (evaluation context management)
  - **Blocks**: None

#### 2.3 Parser State Management
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Medium

- **SharpMUSH.Implementation/Commands/GeneralCommands.cs:6025**
  - Implement parser stack rewinding for better state management
  - **Rationale**: Better handling of loop iterations and control flow
  - **Effort**: 1 week (state design, testing)
  - **Blocks**: None

#### 2.4 Command Indexing
**Count**: 1 TODO  
**Dependencies**: Function Resolution Service (1.2) for consistency  
**Complexity**: Low  
**Priority**: Medium

- **Mentioned in parser context** (related to single-token commands)
  - Cache/index single-token commands for faster lookup
  - **Rationale**: Performance optimization
  - **Effort**: 3-5 days (index structure, caching)
  - **Blocks**: None

---

### Category 3: Command Features (6 items)
Specific command enhancements.

#### 3.1 Attribute Management
**Count**: 3 TODOs  
**Dependencies**: Attribute Metadata System (needed for full implementation)  
**Complexity**: Medium-High  
**Priority**: Medium

- **SharpMUSH.Implementation/Commands/GeneralCommands.cs:6220**
  - Retroactive flag updates to existing attribute instances
  - **Rationale**: When attribute flags change, update all instances
  - **Effort**: 1 week (bulk update queries, testing)
  - **Blocks**: None

- **SharpMUSH.Implementation/Commands/GeneralCommands.cs:6313**
  - Attribute validation via regex patterns
  - **Rationale**: Enforce attribute value constraints
  - **Effort**: 1 week (validation framework, error handling)
  - **Blocks**: None
  - **Depends On**: Would benefit from attribute metadata system

- **Related**: Attribute information display (mentioned in FINAL_TODO_STATUS.md)
  - Full attribute metadata display
  - **Effort**: 1 week (query system, formatting)

#### 3.2 Economy System
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.Implementation/Commands/MoreCommands.cs:1874**
  - Money/penny transfer system
  - **Rationale**: In-game economy feature
  - **Effort**: 1-2 weeks (transaction system, balance tracking, audit log)
  - **Blocks**: None

#### 3.3 SPEAK() Integration
**Count**: 3 TODOs  
**Dependencies**: None  
**Complexity**: Low  
**Priority**: Low

- **SharpMUSH.Implementation/Commands/WizardCommands.cs:684, 705, 2371**
  - Could pipe message through SPEAK() function for text processing
  - **Rationale**: Optional enhancement for message formatting
  - **Effort**: 1-2 days (function integration, testing)
  - **Blocks**: None
  - **Type**: Nice-to-have enhancement

---

### Category 4: Function Features (9 items)
Function implementations and enhancements.

#### 4.1 Websocket/Out-of-Band Communication
**Count**: 4 TODOs  
**Dependencies**: Websocket infrastructure (new subsystem)  
**Complexity**: Very High  
**Priority**: Low

- **SharpMUSH.Implementation/Functions/HTMLFunctions.cs:99, 158, 212**
  - Actual websocket/out-of-band HTML communication (3 instances)
  - **Rationale**: Modern client communication protocol
  - **Effort**: 4-6 weeks (protocol design, client library, server infrastructure)
  - **Blocks**: None

- **SharpMUSH.Implementation/Functions/JSONFunctions.cs:456**
  - Actual websocket/out-of-band JSON communication
  - **Rationale**: GMCP-style protocol support
  - **Effort**: Included in websocket infrastructure effort
  - **Blocks**: None

**Note**: These 4 TODOs should be implemented together as a single websocket subsystem.

#### 4.2 Utility Function Enhancements
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Low  
**Priority**: Low

- **SharpMUSH.Implementation/Functions/UtilityFunctions.cs:27**
  - pcreate() returns #1234:timestamp format
  - **Rationale**: Include creation timestamp in output
  - **Effort**: 1-2 days (format change, backward compatibility option)
  - **Blocks**: None

#### 4.3 String Function Enhancements
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.Implementation/Functions/StringFunctions.cs:1026**
  - Apply attribute function to each character using MModule.apply2
  - **Rationale**: Functional programming approach
  - **Effort**: 2-3 days (F# integration, testing)
  - **Blocks**: None

---

### Category 5: ANSI/Markup System (6 items)
F# markup system improvements.

#### 5.1 ANSI Module Integration
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.Implementation/Functions/UtilityFunctions.cs:64**
  - Move ANSI color processing to AnsiMarkup module
  - **Rationale**: Better code organization, consolidation
  - **Effort**: 3-5 days (refactoring, testing)
  - **Blocks**: None

#### 5.2 ANSI Performance Optimizations
**Count**: 2 TODOs  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.MarkupString/MarkupStringModule.fs:49**
  - Optimize ANSI strings to avoid re-initializing same tag sequentially
  - **Rationale**: Performance improvement
  - **Effort**: 1 week (optimization algorithm, testing)
  - **Blocks**: None

- **SharpMUSH.Implementation/Functions/StringFunctions.cs:1051**
  - ANSI reconstruction after text replacements
  - **Rationale**: Preserve ANSI codes correctly
  - **Effort**: 3-5 days (reconstruction algorithm)
  - **Blocks**: None

#### 5.3 ANSI Feature Enhancements
**Count**: 3 TODOs  
**Dependencies**: None  
**Complexity**: Low-Medium  
**Priority**: Low

- **SharpMUSH.MarkupString/Markup/ANSILibrary/ANSI.fs:118**
  - Handle ANSI colors
  - **Rationale**: Complete ANSI color support
  - **Effort**: 1 week (color handling, testing)
  - **Blocks**: None

- **SharpMUSH.MarkupString/Markup/ANSILibrary/ANSI.fs:154**
  - Clear needs to affect span correctly
  - **Rationale**: Proper ANSI clear handling
  - **Effort**: 3-5 days (span management)
  - **Blocks**: None

- **SharpMUSH.MarkupString/Markup/Markup.fs:108**
  - Move to ANSI.fs (code organization)
  - **Rationale**: Better module organization
  - **Effort**: 1-2 days (refactoring)
  - **Blocks**: None

---

### Category 6: Markup System - Other (3 items)
Non-ANSI markup improvements.

#### 6.1 Markup Type System
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Low  
**Priority**: Low

- **SharpMUSH.MarkupString/MarkupStringModule.fs:35**
  - Consider using built-in option type
  - **Rationale**: Code simplification
  - **Effort**: 1-2 days (refactoring)
  - **Blocks**: None

#### 6.2 Markup Composition
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.MarkupString/MarkupStringModule.fs:680**
  - Improve function composition
  - **Rationale**: Better functional programming patterns
  - **Effort**: 3-5 days (refactoring, testing)
  - **Blocks**: None

#### 6.3 Column Rendering
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Low  
**Priority**: Low

- **SharpMUSH.MarkupString/ColumnModule.fs:27**
  - Turn string into Markup
  - **Rationale**: Type consistency
  - **Effort**: 1-2 days (conversion function)
  - **Blocks**: None

#### 6.4 Markup Case Handling
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.MarkupString/Markup/Markup.fs:125**
  - Implement a case that turns...
  - **Rationale**: Additional transformation support
  - **Effort**: 2-3 days (implementation, testing)
  - **Blocks**: None

---

## Test-Related TODOs (43 items)

### Category 7: Skipped/Failing Tests (15 items)
Tests that need investigation or fixing.

#### 7.1 Database Tests
**Count**: 3 TODOs  
**Dependencies**: Bug fixes needed  
**Complexity**: Medium  
**Priority**: High

- **SharpMUSH.Tests/Database/MotdDataTests.cs:71**
  - Failing test - needs investigation
  - **Effort**: 1-3 days per test

- **SharpMUSH.Tests/Database/ExpandedDataTests.cs:49**
  - Failing Behavior. Needs Investigation
  - **Effort**: 1-3 days

- **SharpMUSH.Tests/Database/FilteredObjectQueryTests.cs:64**
  - Debug owner filter - AQL query needs adjustment
  - **Effort**: 2-5 days (query optimization)

#### 7.2 Function Tests
**Count**: 8 TODOs  
**Dependencies**: Various infrastructure improvements  
**Complexity**: Low-Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Functions/JsonFunctionUnitTests.cs:74, 88**
  - Implement attribute setting in test infrastructure (1)
  - Implement connection mocking in test infrastructure (1)
  - **Effort**: 3-5 days (test infrastructure)

- **SharpMUSH.Tests/Functions/DbrefFunctionUnitTests.cs:91**
  - Enable when tel() is implemented
  - **Effort**: Depends on tel() implementation

- **SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs:73, 81, 82, 100**
  - Various test issues (4 items)
  - **Effort**: 1-2 days each

- **SharpMUSH.Tests/Functions/MailFunctionUnitTests.cs:137**
  - Failing test - needs investigation
  - **Effort**: 1-2 days

- **SharpMUSH.Tests/Functions/ChannelFunctionUnitTests.cs:134**
  - Failing test - needs investigation
  - **Effort**: 1-2 days

#### 7.3 String Function Tests
**Count**: 4 TODOs  
**Dependencies**: decompose/decomposeweb fixes  
**Complexity**: Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Functions/StringFunctionUnitTests.cs:254, 257, 266, 269**
  - Fix decompose and related functions
  - **Effort**: 1 week (requires function fixes)

#### 7.4 Command Tests
**Count**: 9 TODOs  
**Dependencies**: Various  
**Complexity**: Low-Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Commands/GeneralCommandTests.cs** (6 skipped tests)
  - Various test implementations needed
  - **Effort**: 1-2 days each

- **SharpMUSH.Tests/Commands/CommunicationCommandTests.cs** (2 tests)
  - Test fixes needed
  - **Effort**: 1-2 days each

- **SharpMUSH.Tests/Commands/RoomsAndMovementTests.cs:13**
  - Add Tests (entire test class empty)
  - **Effort**: 1-2 weeks (comprehensive test suite)

#### 7.5 Other Tests
**Count**: 6 TODOs  
**Dependencies**: Various  
**Complexity**: Low-Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Substitutions/RegistersUnitTests.cs:25, 26, 27**
  - Requires full server Integration (3 items)
  - **Effort**: 1 week (integration test infrastructure)

- **SharpMUSH.Tests/Commands/CommandUnitTests.cs:32**
  - Need eval vs noparse evaluation
  - **Effort**: 2-3 days

- **SharpMUSH.Tests/Commands/ConfigCommandTests.cs:19**
  - Skipped test
  - **Effort**: 1-2 days

- **SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:240**
  - Bug: loops around somehow
  - **Effort**: 2-5 days

#### 7.6 Parser Tests
**Count**: 5 TODOs  
**Dependencies**: NotifyService integration  
**Complexity**: Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Parser/RecursionAndInvocationLimitTests.cs** (5 skipped tests)
  - Need to check NotifyService for recursion errors
  - **Effort**: 1 week (test infrastructure changes)

#### 7.7 Markup Tests
**Count**: 3 TODOs  
**Dependencies**: Markup system fixes  
**Complexity**: Low-Medium  
**Priority**: Low

- **SharpMUSH.Tests/Markup/Data/InsertAt.cs:20**
  - Investigate Optimize handling
  - **Effort**: 2-3 days

- **SharpMUSH.Tests/Markup/Data/Align.cs:199, 212**
  - Failing tests (2 items)
  - **Effort**: 2-3 days each

#### 7.8 Documentation Tests
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Low  
**Priority**: Low

- **SharpMUSH.Tests/Documentation/MarkdownToAsciiRendererTests.cs:166**
  - Link URL storage in markup
  - **Effort**: Informational note

---

## Dependency Graph

### Foundation Layer (No Dependencies)
These can be started immediately and independently:

```
[Database Abstraction] → Multi-database support
[Text File System] → stext() function
[Pueblo Escape] → Database conversion
[Channel Matching] → Better UX
[CRON Service] → Cleaner architecture
[SPEAK() Integration] → Optional enhancement (3 items)
[Economy System] → Game feature
[pcreate() Enhancement] → Small improvement
[API Design] → PID return, query return types
[Markup System] → 10 small improvements
```

### Parser Layer (Depends on Foundation)
```
[Function Resolution Service] ────┐
                                   ├──→ [Function Caching]
[Parser Performance] ──────────────┘
    ├── ParserContext arguments
    ├── Parsed message alternative
    └── Depth checking note

[Parser Features]
    ├── Single-token investigation
    ├── lsargs support
    ├── Q-register evaluation
    └── Parser stack rewinding

[Command Indexing] ← [Function Resolution Service]
```

### Command/Function Layer (Depends on Parser)
```
[Attribute Management]
    ├── Retroactive updates
    ├── Regex validation
    └── Information display

[Websocket Subsystem] ────┐
    ├── HTML functions (3) │
    └── JSON functions (1) │──→ Modern client support
                           │
[String Functions] ────────┘
    ├── MModule.apply2
    └── ANSI reconstruction

[ANSI Integration] ← [Markup System]
```

### Test Layer (Depends on All)
```
[Test Infrastructure]
    ├── Attribute setting
    ├── Connection mocking
    └── NotifyService integration

[Test Fixes]
    ├── Database tests (3)
    ├── Function tests (8)
    ├── Command tests (9)
    ├── Parser tests (5)
    └── Markup tests (3)

[Test Creation]
    └── RoomsAndMovement suite
```

### Critical Path (Highest Impact)
```
1. [Function Resolution Service] → 2. [Parser Performance] → 3. [Function Caching]
                                                              ↓
4. [Attribute Management] ← [Test Infrastructure] ← 5. [Parser Features]
```

---

## Implementation Strategy

### Phase 1: Foundation (4-6 weeks)
**Goal**: Establish architectural improvements and core services

**Priority Items**:
1. **Function Resolution Service** (1 week)
   - Highest impact on architecture
   - Enables function caching
   - Improves testability

2. **Parser Performance Optimizations** (2 weeks)
   - ParserContext argument passing
   - Parsed message alternative
   - High performance impact (10-20% improvement)

3. **CRON Service Extraction** (1 week)
   - Clean architecture
   - Better separation of concerns

4. **Test Infrastructure** (1 week)
   - Attribute setting
   - Connection mocking
   - Enables fixing failing tests

**Deliverables**:
- Cleaner architecture
- 10-20% parser performance improvement
- Better testability
- Foundation for caching

### Phase 2: Performance & Features (4-6 weeks)
**Goal**: Add caching and complete high-value features

**Priority Items**:
1. **Command Indexing** (3-5 days)
   - Faster command lookup
   - Depends on Function Resolution Service

2. **Attribute Management** (2 weeks)
   - Retroactive flag updates
   - Regex validation
   - Information display

3. **Parser Features** (2 weeks)
   - lsargs support
   - Parser stack rewinding
   - Single-token investigation

4. **Fix High-Priority Tests** (1 week)
   - Database tests (3)
   - Critical function tests

**Deliverables**:
- Complete attribute management
- Enhanced parser capabilities
- Reduced failing test count
- Better command performance

### Phase 3: Enhancements (6-8 weeks)
**Goal**: Add remaining features and fix all tests

**Priority Items**:
1. **ANSI/Markup System** (2 weeks)
   - Integration improvements
   - Performance optimizations
   - Feature completeness

2. **Database Abstraction** (2-3 weeks)
   - Multi-database support
   - Connection pooling

3. **Channel Improvements** (1 week)
   - Fuzzy/partial matching
   - Better UX

4. **Fix Remaining Tests** (2 weeks)
   - All function tests
   - Command tests
   - Parser tests
   - Create RoomsAndMovement tests

**Deliverables**:
- Complete test coverage
- Multi-database support
- Polished ANSI system
- Better user experience

### Phase 4: Advanced Features (8-12 weeks)
**Goal**: Implement complex subsystems

**Priority Items**:
1. **Websocket/OOB Subsystem** (4-6 weeks)
   - Protocol design
   - Server infrastructure
   - Client library
   - HTML functions (3)
   - JSON functions (1)

2. **Text File System** (2-4 weeks)
   - File abstraction
   - Security model
   - stext() function

3. **Economy System** (1-2 weeks)
   - Transaction infrastructure
   - Balance tracking
   - Audit logging

4. **Remaining Enhancements** (2 weeks)
   - SPEAK() integration
   - pcreate() format
   - String function improvements

**Deliverables**:
- Modern websocket support
- File-based content system
- Complete economy feature
- 100% TODO resolution

---

## Prioritization Matrix

### High Priority (Do First)
**Criteria**: High impact, enables other work, architectural improvement

1. Function Resolution Service (week 1)
2. Parser Performance Optimizations (weeks 2-3)
3. Test Infrastructure (week 4)
4. Command Indexing (week 5)
5. Attribute Management (weeks 6-7)

### Medium Priority (Do Second)
**Criteria**: Moderate impact, feature completeness, bug fixes

1. Parser Features (lsargs, Q-registers, stack rewinding)
2. ANSI/Markup System improvements
3. Database test fixes
4. Channel name matching
5. CRON Service extraction

### Low Priority (Do Later)
**Criteria**: Nice-to-have, optional features, minor improvements

1. SPEAK() integration (3 items)
2. Economy system
3. pcreate() enhancement
4. String function MModule.apply2
5. Markup type system improvements

### Defer/Long-term (Do Last)
**Criteria**: Major architectural work, requires external dependencies

1. Websocket/OOB subsystem (4 items)
2. Text file system
3. Multi-database support
4. API design changes (breaking changes)

---

## Effort Estimates

### By Category
| Category | Item Count | Total Effort | Avg per Item |
|----------|-----------|--------------|--------------|
| Infrastructure | 10 | 10-15 weeks | 1-1.5 weeks |
| Parser | 8 | 6-9 weeks | 0.75-1 week |
| Commands | 6 | 5-8 weeks | 0.8-1.3 weeks |
| Functions | 9 | 5-7 weeks | 0.5-0.8 weeks |
| ANSI/Markup | 10 | 4-6 weeks | 0.4-0.6 weeks |
| Tests (fix) | 28 | 8-12 weeks | 0.3-0.4 weeks |
| Tests (create) | 15 | 2-3 weeks | 0.1-0.2 weeks |
| **Total** | **80** | **40-60 weeks** | **0.5-0.75 weeks** |

### By Phase
| Phase | Duration | TODO Count | Completion % |
|-------|----------|-----------|--------------|
| Phase 1: Foundation | 4-6 weeks | 10 | 13% |
| Phase 2: Performance | 4-6 weeks | 15 | 32% |
| Phase 3: Enhancements | 6-8 weeks | 30 | 70% |
| Phase 4: Advanced | 8-12 weeks | 25 | 100% |
| **Total** | **22-32 weeks** | **80** | **100%** |

### By Priority
| Priority | TODO Count | Effort | % of Total |
|----------|-----------|--------|------------|
| High | 15 | 10-15 weeks | 19% |
| Medium | 30 | 15-22 weeks | 38% |
| Low | 20 | 8-12 weeks | 25% |
| Defer | 15 | 12-18 weeks | 19% |

---

## Risk Assessment

### High Risk Items
1. **Websocket/OOB Subsystem**
   - Risk: Protocol complexity, client compatibility
   - Mitigation: Start with simple protocol, incremental rollout
   - Impact: High (modern client support)

2. **Multi-Database Support**
   - Risk: Breaking changes, migration complexity
   - Mitigation: Careful abstraction design, backward compatibility
   - Impact: High (deployment flexibility)

3. **Parser Performance Changes**
   - Risk: Breaking existing functionality
   - Mitigation: Comprehensive testing, incremental changes
   - Impact: High (core functionality)

### Medium Risk Items
1. **Attribute Management Changes**
   - Risk: Data consistency, retroactive updates
   - Mitigation: Transactional updates, rollback capability
   - Impact: Medium (game data integrity)

2. **Text File System**
   - Risk: Security vulnerabilities, path traversal
   - Mitigation: Thorough security review, sandboxing
   - Impact: Medium (security)

3. **Test Infrastructure Changes**
   - Risk: Breaking existing tests
   - Mitigation: Backward compatibility, gradual migration
   - Impact: Low (test-only)

### Low Risk Items
1. **ANSI/Markup Improvements** - Isolated changes
2. **Command Enhancements** - Additive features
3. **Function Additions** - New functionality

---

## Success Metrics

### Code Quality
- [ ] All 37 production TODOs resolved
- [ ] All 43 test TODOs resolved
- [ ] Zero skipped tests
- [ ] Test coverage > 80%
- [ ] No regression in existing functionality

### Performance
- [ ] 10-20% parser performance improvement (Phase 1)
- [ ] Function lookup latency < 1ms (Phase 2)
- [ ] Command execution overhead < 5% (Phase 2)

### Architecture
- [ ] Clear service boundaries
- [ ] Dependency injection throughout
- [ ] No circular dependencies
- [ ] Testable components

### Features
- [ ] Websocket support (Phase 4)
- [ ] Multi-database support (Phase 3)
- [ ] Complete attribute management (Phase 2)
- [ ] Text file system (Phase 4)

---

## Maintenance Plan

### Post-Implementation
1. **Documentation Updates**
   - Update architecture documentation
   - Document new subsystems
   - Update developer guide

2. **Performance Monitoring**
   - Establish baselines
   - Monitor key metrics
   - Profile regularly

3. **Technical Debt**
   - Review quarterly
   - Prioritize refactoring
   - Maintain TODO hygiene

4. **Testing**
   - Maintain >80% coverage
   - Add regression tests
   - Regular test review

---

## Conclusion

This comprehensive analysis categorizes all 80 remaining TODO items into a structured implementation plan spanning 22-32 weeks across 4 phases. The dependency graph identifies critical paths and enables parallel work where possible.

**Key Recommendations**:

1. **Start with Phase 1** - Foundation work provides the highest ROI and enables future work
2. **Parallel Workstreams** - Infrastructure, parser, and test work can proceed in parallel
3. **Incremental Delivery** - Each phase delivers tangible value
4. **Risk Management** - Address high-risk items early with proper mitigation
5. **Test-Driven** - Fix test infrastructure early to enable TDD for remaining work

**Expected Outcome**: Complete resolution of all TODO items with improved architecture, better performance, and comprehensive test coverage.

---

*Document Version: 1.0*  
*Last Updated: 2026-01-27*  
*Total Items Analyzed: 80*
