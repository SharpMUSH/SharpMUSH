# TODO Items - Dependency Analysis and Resolution Strategy

**Generated**: 2026-01-27  
**Total TODO Items**: 80 (37 production code + 43 test-related)

## Executive Summary

This document provides a comprehensive analysis of all remaining TODO items in the SharpMUSH codebase, organized by category, dependency relationships, and recommended implementation order. The analysis includes dependency information showing which TODOs must be completed before others, enabling a strategic approach to resolving all remaining items.

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
  - **Scope**: Abstraction layer, connection pooling, migrations
  - **Blocks**: None directly, but enables multi-database deployments

#### 1.2 Function Resolution Service
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: High

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:350**
  - Move function resolution to a dedicated Library Service
  - **Rationale**: Better separation of concerns, enables caching
  - **Scope**: Service extraction, dependency injection setup
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
  - **Scope**: File abstraction, security model, quota system
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
  - **Scope**: Service extraction, configuration model
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
  - **Scope**: Regex patterns, testing with real data
  - **Blocks**: None

#### 1.6 API Design Improvements
**Count**: 2 TODOs  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.Library/ISharpDatabase.cs:145**
  - Return type for attribute pattern queries needs reconsideration
  - **Rationale**: Current return type may not be optimal for all use cases
  - **Scope**: API redesign, migration of consumers
  - **Blocks**: None
  - **Breaking Change**: Yes, requires consumer updates

- **SharpMUSH.Library/Requests/QueueCommandListRequest.cs:7,24** (2 instances)
  - Return the new PID for output/tracking
  - **Rationale**: Commands need to track queued process IDs
  - **Scope**: IRequest interface changes, handler updates
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
  - **Scope**: Matching algorithm, ambiguity handling
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
  - **Scope**: Parser refactoring, testing
  - **Blocks**: None
  - **Benefits**: Significant performance improvement

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:1388**
  - Implement parsed message alternative for better performance
  - **Rationale**: Avoid re-parsing messages
  - **Scope**: Caching strategy, invalidation
  - **Blocks**: None
  - **Benefits**: Faster message handling

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:470**
  - Depth checking is done here before argument refinement (informational)
  - **Rationale**: Note about current implementation order
  - **Type**: Informational comment

#### 2.2 Parser Feature Enhancements
**Count**: 3 TODOs  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Medium

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:1301**
  - Investigate if single-token commands should support argument splitting
  - **Rationale**: Research needed to determine correct behavior
  - **Scope**: Research, testing, decision
  - **Blocks**: None
  - **Type**: Investigation/Research

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:1369**
  - Implement lsargs (list-style arguments) support
  - **Rationale**: Feature compatibility with PennMUSH
  - **Scope**: Parser changes, testing
  - **Blocks**: None

- **SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:1530**
  - Handle Q-registers containing evaluation strings properly
  - **Rationale**: Deferred evaluation support
  - **Scope**: Evaluation context management
  - **Blocks**: None

#### 2.3 Parser State Management
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Medium

- **SharpMUSH.Implementation/Commands/GeneralCommands.cs:6025**
  - Implement parser stack rewinding for better state management
  - **Rationale**: Better handling of loop iterations and control flow
  - **Scope**: State design, testing
  - **Blocks**: None

#### 2.4 Command Indexing
**Count**: 1 TODO  
**Dependencies**: Function Resolution Service (1.2) for consistency  
**Complexity**: Low  
**Priority**: Medium

- **Mentioned in parser context** (related to single-token commands)
  - Cache/index single-token commands for faster lookup
  - **Rationale**: Performance optimization
  - **Scope**: Index structure, caching
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
  - **Scope**: Bulk update queries, testing
  - **Blocks**: None

- **SharpMUSH.Implementation/Commands/GeneralCommands.cs:6313**
  - Attribute validation via regex patterns
  - **Rationale**: Enforce attribute value constraints
  - **Scope**: Validation framework, error handling
  - **Blocks**: None
  - **Depends On**: Would benefit from attribute metadata system

- **Related**: Attribute information display
  - Full attribute metadata display
  - **Scope**: Query system, formatting

#### 3.2 Economy System
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.Implementation/Commands/MoreCommands.cs:1874**
  - Money/penny transfer system
  - **Rationale**: In-game economy feature
  - **Scope**: Transaction system, balance tracking, audit log
  - **Blocks**: None

#### 3.3 SPEAK() Integration
**Count**: 3 TODOs  
**Dependencies**: None  
**Complexity**: Low  
**Priority**: Low

- **SharpMUSH.Implementation/Commands/WizardCommands.cs:684, 705, 2371**
  - Could pipe message through SPEAK() function for text processing
  - **Rationale**: Optional enhancement for message formatting
  - **Scope**: Function integration, testing
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
  - **Scope**: Protocol design, client library, server infrastructure
  - **Blocks**: None

- **SharpMUSH.Implementation/Functions/JSONFunctions.cs:456**
  - Actual websocket/out-of-band JSON communication
  - **Rationale**: GMCP-style protocol support
  - **Scope**: Included in websocket infrastructure
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
  - **Scope**: Format change, backward compatibility option
  - **Blocks**: None

#### 4.3 String Function Enhancements
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.Implementation/Functions/StringFunctions.cs:1026**
  - Apply attribute function to each character using MModule.apply2
  - **Rationale**: Functional programming approach
  - **Scope**: F# integration, testing
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
  - **Scope**: Refactoring, testing
  - **Blocks**: None

#### 5.2 ANSI Performance Optimizations
**Count**: 2 TODOs  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.MarkupString/MarkupStringModule.fs:49**
  - Optimize ANSI strings to avoid re-initializing same tag sequentially
  - **Rationale**: Performance improvement
  - **Scope**: Optimization algorithm, testing
  - **Blocks**: None

- **SharpMUSH.Implementation/Functions/StringFunctions.cs:1051**
  - ANSI reconstruction after text replacements
  - **Rationale**: Preserve ANSI codes correctly
  - **Scope**: Reconstruction algorithm
  - **Blocks**: None

#### 5.3 ANSI Feature Enhancements
**Count**: 3 TODOs  
**Dependencies**: None  
**Complexity**: Low-Medium  
**Priority**: Low

- **SharpMUSH.MarkupString/Markup/ANSILibrary/ANSI.fs:118**
  - Handle ANSI colors
  - **Rationale**: Complete ANSI color support
  - **Scope**: Color handling, testing
  - **Blocks**: None

- **SharpMUSH.MarkupString/Markup/ANSILibrary/ANSI.fs:154**
  - Clear needs to affect span correctly
  - **Rationale**: Proper ANSI clear handling
  - **Scope**: Span management
  - **Blocks**: None

- **SharpMUSH.MarkupString/Markup/Markup.fs:108**
  - Move to ANSI.fs (code organization)
  - **Rationale**: Better module organization
  - **Scope**: Refactoring
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
  - **Scope**: Refactoring
  - **Blocks**: None

#### 6.2 Markup Composition
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.MarkupString/MarkupStringModule.fs:680**
  - Improve function composition
  - **Rationale**: Better functional programming patterns
  - **Scope**: Refactoring, testing
  - **Blocks**: None

#### 6.3 Column Rendering
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Low  
**Priority**: Low

- **SharpMUSH.MarkupString/ColumnModule.fs:27**
  - Turn string into Markup
  - **Rationale**: Type consistency
  - **Scope**: Conversion function
  - **Blocks**: None

#### 6.4 Markup Case Handling
**Count**: 1 TODO  
**Dependencies**: None  
**Complexity**: Medium  
**Priority**: Low

- **SharpMUSH.MarkupString/Markup/Markup.fs:125**
  - Implement a case that turns...
  - **Rationale**: Additional transformation support
  - **Scope**: Implementation, testing
  - **Blocks**: None

---

## Test-Related TODOs (43 items)

### Category 7: Skipped/Failing Tests (28 items)
Tests that need investigation or fixing.

#### 7.1 Database Tests (3 items)
**Dependencies**: Bug fixes needed  
**Complexity**: Medium  
**Priority**: High

- **SharpMUSH.Tests/Database/MotdDataTests.cs:71** - Failing test - needs investigation
- **SharpMUSH.Tests/Database/ExpandedDataTests.cs:49** - Failing Behavior. Needs Investigation
- **SharpMUSH.Tests/Database/FilteredObjectQueryTests.cs:64** - Debug owner filter - AQL query needs adjustment

#### 7.2 Function Tests (8 items)
**Dependencies**: Various infrastructure improvements  
**Complexity**: Low-Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Functions/JsonFunctionUnitTests.cs:74** - Implement attribute setting in test infrastructure
- **SharpMUSH.Tests/Functions/JsonFunctionUnitTests.cs:88** - Implement connection mocking in test infrastructure
- **SharpMUSH.Tests/Functions/DbrefFunctionUnitTests.cs:91** - Enable when tel() is implemented
- **SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs:73** - %iL does not evaluate to the correct value
- **SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs:81** - Fix: %$0 is for switches
- **SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs:82** - This should be #@, which is not yet implemented
- **SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs:100** - Why does putting [ibreak()] at the start cause different evaluation?
- **SharpMUSH.Tests/Functions/MailFunctionUnitTests.cs:137** - Failing test - needs investigation
- **SharpMUSH.Tests/Functions/ChannelFunctionUnitTests.cs:134** - Failing test - needs investigation

#### 7.3 String Function Tests (4 items)
**Dependencies**: decompose/decomposeweb fixes  
**Complexity**: Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Functions/StringFunctionUnitTests.cs:254** - Fix decompose, and then fix this test
- **SharpMUSH.Tests/Functions/StringFunctionUnitTests.cs:257** - returns "ansi\(u\,red\)". Something wrong with 'b'?
- **SharpMUSH.Tests/Functions/StringFunctionUnitTests.cs:266** - Fix decomposeweb, and then fix this test
- **SharpMUSH.Tests/Functions/StringFunctionUnitTests.cs:269** - decompose is not matching 'b' correctly

#### 7.4 Command Tests (9 items)
**Dependencies**: Various  
**Complexity**: Low-Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Commands/GeneralCommandTests.cs:315, 399, 413, 441, 482** - Skipped tests (5 items)
- **SharpMUSH.Tests/Commands/GeneralCommandTests.cs:385, 510** - Failing tests (2 items)
- **SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:241** - Alias name cannot be empty
- **SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:382** - Failing Test. Requires investigation
- **SharpMUSH.Tests/Commands/RoomsAndMovementTests.cs:13** - Add Tests (entire test class empty)

#### 7.5 Substitution & Other Tests (6 items)
**Dependencies**: Various  
**Complexity**: Low-Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Substitutions/RegistersUnitTests.cs:25, 26, 27** - Requires full server Integration (3 items)
- **SharpMUSH.Tests/Commands/CommandUnitTests.cs:32** - Need eval vs noparse evaluation
- **SharpMUSH.Tests/Commands/ConfigCommandTests.cs:19** - Skipped test
- **SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:240** - Bug: loops around somehow

#### 7.6 Parser Tests (5 items)
**Dependencies**: NotifyService integration  
**Complexity**: Medium  
**Priority**: Medium

- **SharpMUSH.Tests/Parser/RecursionAndInvocationLimitTests.cs:337, 363, 383** - Commands send notifications via NotifyService, not return values (3 skipped tests)
- **SharpMUSH.Tests/Parser/RecursionAndInvocationLimitTests.cs:353, 373** - Need to check NotifyService for recursion error messages (2 commented lines)

#### 7.7 Markup Tests (3 items)
**Dependencies**: Markup system fixes  
**Complexity**: Low-Medium  
**Priority**: Low

- **SharpMUSH.Tests/Markup/Data/InsertAt.cs:20** - Investigate why Optimize does not handle this case correctly
- **SharpMUSH.Tests/Markup/Data/Align.cs:199** - Failing Test
- **SharpMUSH.Tests/Markup/Data/Align.cs:212** - Failing Test

#### 7.8 Documentation Tests (1 item)
**Dependencies**: None  
**Complexity**: Low  
**Priority**: Low

- **SharpMUSH.Tests/Documentation/MarkdownToAsciiRendererTests.cs:166** - Link URL storage in markup is a TODO (informational)

---

## Dependency Graph

### Foundation Layer (No Dependencies)
These can be started immediately and independently:

- Database Abstraction - Multi-database support
- Text File System - stext() function
- Pueblo Escape - Database conversion
- Channel Matching - Better UX
- CRON Service - Cleaner architecture
- SPEAK() Integration - Optional enhancement (3 items)
- Economy System - Game feature
- pcreate() Enhancement - Small improvement
- API Design - PID return, query return types
- Markup System - 10 small improvements

### Parser Layer (Depends on Foundation)

**Function Resolution Service** enables:
- Parser Performance optimizations
- Function Caching
- Command Indexing
- Test Infrastructure improvements

**Parser Performance** improvements:
- ParserContext arguments
- Parsed message alternative
- Depth checking note

**Parser Features**:
- Single-token investigation
- lsargs support
- Q-register evaluation
- Parser stack rewinding

### Command/Function Layer (Depends on Parser)

**Attribute Management** depends on:
- Parser Performance (partially)
- Parser Features (partially)

Includes:
- Retroactive updates
- Regex validation
- Information display

**Websocket Subsystem**:
- HTML functions (3)
- JSON functions (1)
- Modern client support

**String Functions**:
- MModule.apply2
- ANSI reconstruction

**ANSI Integration** depends on:
- Markup System improvements

### Test Layer (Depends on All)

**Test Infrastructure** enables:
- Attribute setting
- Connection mocking
- NotifyService integration

**Test Fixes** depend on:
- Test Infrastructure
- Various production fixes

**Test Creation** depends on:
- Test Infrastructure
- Test Fixes

---

## Implementation Strategy by Phase

### Phase 1: Foundation
**Goal**: Establish architectural improvements and core services

**Priority Items**:
1. Function Resolution Service - Architectural foundation
2. Parser Performance Optimizations - Significant performance impact
3. CRON Service Extraction - Clean architecture
4. Test Infrastructure - Enables test fixes

**Deliverables**:
- Cleaner architecture
- Performance improvements
- Better testability
- Foundation for caching

### Phase 2: Performance & Features
**Goal**: Add caching and complete high-value features

**Priority Items**:
1. Command Indexing - Faster command lookup
2. Attribute Management - Complete feature set
3. Parser Features - lsargs, stack rewinding
4. Fix High-Priority Tests - Reduce test failures

**Deliverables**:
- Complete attribute management
- Enhanced parser capabilities
- Reduced failing test count
- Better command performance

### Phase 3: Enhancements
**Goal**: Add remaining features and fix all tests

**Priority Items**:
1. ANSI/Markup System - Integration and optimization
2. Database Abstraction - Multi-database support
3. Channel Improvements - Fuzzy/partial matching
4. Fix Remaining Tests - All function and command tests

**Deliverables**:
- Complete test coverage
- Multi-database support
- Polished ANSI system
- Better user experience

### Phase 4: Advanced Features
**Goal**: Implement complex subsystems

**Priority Items**:
1. Websocket/OOB Subsystem - Modern client support (4 TODOs)
2. Text File System - File-based content
3. Economy System - Transaction infrastructure
4. Remaining Enhancements - SPEAK(), pcreate(), string functions

**Deliverables**:
- Modern websocket support
- File-based content system
- Complete economy feature
- 100% TODO resolution

---

## Prioritization Matrix

### High Priority (Do First)
**Criteria**: High impact, enables other work, architectural improvement

1. Function Resolution Service
2. Parser Performance Optimizations
3. Test Infrastructure
4. Command Indexing
5. Attribute Management

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
- [ ] Parser performance improvement (Phase 1)
- [ ] Function lookup latency reduction (Phase 2)
- [ ] Command execution overhead minimization (Phase 2)

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

This comprehensive analysis categorizes all 80 remaining TODO items into a structured implementation plan across 4 phases. The dependency graph identifies critical paths and enables parallel work where possible.

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
