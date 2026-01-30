# SharpMUSH TODO Status - Updated 2026-01-30

## Executive Summary

**Total TODOs Found**: 54 (across production code and tests)
- **Production Code**: 24 TODOs
- **Test Code**: 30 TODOs (failing tests, skipped tests, test infrastructure)

This document reflects the CURRENT state of TODO items in the SharpMUSH codebase as of 2026-01-30.

---

## Production Code TODOs (24 Total)

### Commands (7 TODOs)

#### GeneralCommands.cs (3 TODOs)
1. **Line 6048**: Parser stack rewinding for better state management
   - **Impact**: Loop iteration stability
   - **Priority**: High
   - **Category**: Architectural

2. **Line 6243**: Retroactive flag updates to existing attribute instances
   - **Impact**: Attribute flag consistency
   - **Priority**: Medium
   - **Category**: Complex Implementation

3. **Line 6336**: Attribute validation via regex patterns
   - **Impact**: Data validation
   - **Priority**: Medium
   - **Category**: Complex Implementation

#### MoreCommands.cs (1 TODO)
4. **Line 1874**: Money/penny transfer system
   - **Impact**: New feature
   - **Priority**: Low
   - **Category**: Major Subsystem

#### WizardCommands.cs (3 TODOs)
5. **Line 684**: Pipe message through SPEAK() function (instance 1)
6. **Line 705**: Pipe message through SPEAK() function (instance 2)
7. **Line 2371**: Pipe message through SPEAK() function (instance 3)
   - **Impact**: Optional text processing enhancement
   - **Priority**: Low
   - **Category**: Optional Enhancement

### Functions (7 TODOs)

#### HTMLFunctions.cs (1 TODO)
8. **Line 247**: Websocket/out-of-band HTML communication
   - **Impact**: New feature
   - **Priority**: Low (requires client support)
   - **Category**: Major Subsystem

#### JSONFunctions.cs (1 TODO)
9. **Line 481**: Websocket/out-of-band JSON communication
   - **Impact**: New feature
   - **Priority**: Low (requires client support)
   - **Category**: Major Subsystem

#### StringFunctions.cs (2 TODOs)
10. **Line 1026**: Apply attribute function to each character using MModule.apply2
    - **Impact**: Performance optimization
    - **Priority**: Low
    - **Category**: Performance

11. **Line 1051**: ANSI reconstruction after text replacements
    - **Impact**: ANSI markup preservation
    - **Priority**: Medium
    - **Category**: Performance

#### UtilityFunctions.cs (3 TODOs)
12. **Line 27**: pcreate() returns #1234:timestamp format
    - **Impact**: API enhancement
    - **Priority**: Low
    - **Category**: Enhancement

13. **Line 64**: Move ANSI color processing to AnsiMarkup module
    - **Impact**: Code organization
    - **Priority**: Low
    - **Category**: Performance

### Parser/Visitors (8 TODOs)

#### SharpMUSHParserVisitor.cs (8 TODOs)
14. **Line 350**: Move function resolution to dedicated Library Service
    - **Impact**: Architecture improvement
    - **Priority**: High
    - **Category**: Architectural

15. **Line 470**: Depth checking before argument refinement
    - **Impact**: Informational note
    - **Priority**: None (documentation)
    - **Category**: Informational

16. **Line 530**: Pass ParserContexts directly as arguments
    - **Impact**: Performance improvement
    - **Priority**: Medium
    - **Category**: Performance

17. **Line 1344**: Single-token commands argument splitting
    - **Impact**: Feature investigation
    - **Priority**: Low
    - **Category**: Complex Implementation

18. **Line 1412**: Implement lsargs (list-style arguments) support
    - **Impact**: New feature
    - **Priority**: Medium
    - **Category**: Complex Implementation

19. **Line 1431**: Parsed message alternative for performance
    - **Impact**: Performance optimization
    - **Priority**: Medium
    - **Category**: Performance

20. **Line 1573**: Handle Q-registers with evaluation strings
    - **Impact**: Q-register behavior
    - **Priority**: Medium
    - **Category**: Complex Implementation

### Library/Core (2 TODOs)

#### ISharpDatabase.cs (1 TODO)
21. **Line 145**: Return type for attribute pattern queries
    - **Impact**: API design
    - **Priority**: Medium
    - **Category**: Architectural

#### QueueCommandListRequest.cs (2 TODOs)
22. **Line 7**: Return new PID for output/tracking (instance 1)
23. **Line 24**: Return new PID for output/tracking (instance 2)
    - **Impact**: Request handling enhancement
    - **Priority**: Medium
    - **Category**: Architectural

### Database Conversion (1 TODO)

#### PennMUSHDatabaseConverter.cs (1 TODO)
24. **Line 823**: Implement proper Pueblo escape stripping
    - **Impact**: Database import accuracy
    - **Priority**: Low
    - **Category**: Enhancement

---

## Test Code TODOs (30 Total)

### Test Infrastructure Issues (2 TODOs)
- **JsonFunctionUnitTests.cs:74**: Implement attribute setting in test infrastructure
- **JsonFunctionUnitTests.cs:88**: Implement connection mocking in test infrastructure

### Failing/Skipped Tests (11 TODOs)
- **CommunicationCommandTests.cs:382**: Failing test requiring investigation
- **GeneralCommandTests.cs:385**: Failing test (skipped)
- **GeneralCommandTests.cs:510**: Failing test (skipped)
- **ExpandedDataTests.cs:49**: Failing behavior needing investigation
- **MotdDataTests.cs:71**: Failing test needing investigation
- **MailFunctionUnitTests.cs:137**: Failing test needing investigation
- **RecursionAndInvocationLimitTests.cs:337**: Needs NotifyService check redesign
- **RecursionAndInvocationLimitTests.cs:363**: Needs NotifyService check redesign
- **RecursionAndInvocationLimitTests.cs:383**: Needs NotifyService check redesign
- **Align.cs:199**: Failing test (commented)
- **Align.cs:212**: Failing test (commented)

### Test Bugs/Issues (6 TODOs)
- **CommandUnitTests.cs:32**: Need eval vs noparse evaluation
- **DatabaseCommandTests.cs:253**: Bug with reading/looping
- **ListFunctionUnitTests.cs:73**: %iL evaluation issue
- **ListFunctionUnitTests.cs:101**: ibreak() evaluation order issue
- **StringFunctionUnitTests.cs:263**: decomposeweb fix needed
- **StringFunctionUnitTests.cs:266**: decompose 'b' matching issue

### Missing Test Features (4 TODOs)
- **RoomsAndMovementTests.cs:13**: Add tests
- **DbrefFunctionUnitTests.cs:91**: Enable when tel() implemented
- **ListFunctionUnitTests.cs:83**: Implement #@ token shorthand

### Test Investigation/Design (7 TODOs)
- **FilteredObjectQueryTests.cs:64**: Debug owner filter AQL query
- **InsertAt.cs:20**: Investigate Optimize case handling
- **RecursionAndInvocationLimitTests.cs:353**: Check NotifyService for errors
- **RecursionAndInvocationLimitTests.cs:373**: Check NotifyService for errors
- **RecursionAndInvocationLimitTests.cs:397**: Check NotifyService for output
- **RegistersUnitTests.cs:25-27**: Requires full server integration (3 commented tests)

---

## Comparison with TODO_FINAL_ANALYSIS.md

### Missing from Previous Analysis
The previous analysis listed **37 TODOs** but the current codebase has **24 production TODOs**. Discrepancies:

**NOT FOUND in current code** (may have been completed or merged):
- Attribute metadata system checks (GeneralCommands.cs)
- Attribute enumeration validation (GeneralCommands.cs)
- Channel name fuzzy matching (SharpMUSHParserVisitor.cs)
- Command indexing/caching (SharpMUSHParserVisitor.cs - one of two)
- ANSI 'n' (clear/normal) handling (UtilityFunctions.cs)
- Mail system per-player numbering (MailCommand/StatusMail.cs)
- Multi-database SQL support (SqlService.cs)
- CRON service extraction (StartupHandler.cs)
- Attribute table query system (GeneralCommands.cs - 2 related)
- HTML websocket (HTMLFunctions.cs reported 3, only found 1)
- Password compatibility note (Startup.cs)

**Newly found** (not in previous analysis):
- Pueblo escape stripping (PennMUSHDatabaseConverter.cs)
- All 30 test-related TODOs

### Recommendations

1. **Update TODO_FINAL_ANALYSIS.md** to reflect current state
2. **Investigate discrepancies** - were those 13 TODOs completed?
3. **Prioritize test fixes** - 11 failing/skipped tests need attention
4. **Address test infrastructure** - Enable full testing capabilities

---

## Priority Recommendations (Production Code)

### Critical (Do First)
1. Parser stack rewinding (GeneralCommands.cs:6048)
2. Function resolution service (SharpMUSHParserVisitor.cs:350)
3. Fix failing tests (11 tests skipped/failing)

### High Priority (Next Sprint)
4. lsargs support (SharpMUSHParserVisitor.cs:1412)
5. ParserContext optimization (SharpMUSHParserVisitor.cs:530)
6. Q-register evaluation (SharpMUSHParserVisitor.cs:1573)
7. PID return values (QueueCommandListRequest.cs)

### Medium Priority
8. Attribute validation (GeneralCommands.cs:6336)
9. Retroactive flag updates (GeneralCommands.cs:6243)
10. Database return types (ISharpDatabase.cs:145)
11. ANSI reconstruction (StringFunctions.cs:1051)
12. Parsed message alternative (SharpMUSHParserVisitor.cs:1431)

### Low Priority
13. SPEAK() integration (3 instances)
14. Websocket communication (2 instances)
15. Money system (MoreCommands.cs:1874)
16. ANSI module refactoring (UtilityFunctions.cs:64)
17. pcreate() format (UtilityFunctions.cs:27)

---

*Document Version: 2.0*
*Last Updated: 2026-01-30*
*Scan Method: PowerShell Select-String across entire codebase*
