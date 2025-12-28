# SharpMUSH TODO Implementation Plan

**Analysis Date:** December 28, 2024  
**Total TODO Items:** 283

This document categorizes all TODO items in the SharpMUSH codebase and provides a structured plan for implementing them. The items are organized by functional area and priority level to help guide implementation decisions.

---

## Executive Summary

The codebase contains 283 TODO items across multiple areas:

- **Commands**: 104 items (37% of total)
- **Functions**: 58 items (20% of total)
- **Services**: 42 items (15% of total)
- **Parser & Visitors**: 15 items (5% of total)
- **Database**: 7 items (2% of total)
- **MarkupString & Formatting**: 19 items (7% of total)
- **Tests**: 17 items (6% of total)
- **Telnet Protocol**: 4 items (1% of total)
- **Substitutions**: 7 items (2% of total)
- **Other**: 10 items (4% of total)

---

## 1. Commands (104 items)

### 1.1 General Commands (54 items)
**File:** `SharpMUSH.Implementation/Commands/GeneralCommands.cs`

**High Priority:**
- Line 124: Full implementation - parsing object/attribute path, splitting list into elements
- Line 634: Implement LOCK LIST functionality
- Line 782: Check if exit has destination attribute
- Line 846: Implement teleporting through an exit
- Line 944: Implement full database query to find matching objects
- Line 1032: Locate target object, check control permissions/halt @power
- Line 2198: Implement full database search with filters
- Line 4774: Implement actual halt functionality
- Line 4775: Trigger @STARTUP attribute if exists

**Medium Priority:**
- Line 169: Optimize - avoid re-parsing, use context and visit children
- Line 642: Check channels and warnings
- Line 650: Match proper date format (Mon Feb 26 18:05:10 2007)
- Line 709: Proper carry format
- Line 716: Proper format
- Line 896: Show target player new location (LOOK equivalent)
- Line 1186: Implement Noisy, Silent, NoEval switches
- Line 1225: Permission check on outputs
- Line 1356: Use glob instead of current implementation
- Line 1979-1980: Implement switches and queue functionality
- Line 3363: Channel visibility on commands
- Line 3565-3566: Set locks and parent if not default
- Line 3698: Implement proper default flag checking by object type
- Line 3708: Implement proper default attribute flag checking
- Line 3718: Make NoEval work
- Line 3853: Support room/obj format like PennMUSH
- Line 3975: Query actual database statistics
- Line 4210: Query database for objects linked to target
- Line 4946: Track last restarted time
- Line 4998: Make inline vs queued actually do something

**Q-Register Related (Dependent on Q-Register System):**
- Line 3284: Set up environment arguments (%0-%9, r(0,args)-r(29,args))
- Line 3285: Handle Q-register management (clearregs, localize)
- Line 3286: Handle /match for pattern matching
- Line 3287: Handle queueing vs inline execution
- Line 4483: Handle Q-register management
- Line 4490: Clear Q-registers when system available
- Line 4494: Save Q-registers when system available
- Line 4497: Handle environment argument substitution
- Line 4508: Restore Q-registers if LOCALIZE
- Line 4510: Handle NOBREAK switch
- Line 4982: Need way to REWIND stack in parser

**Low Priority (Validation/Polish):**
- Line 1472: Ensure attribute has same flags as SEMAPHORE @attribute
- Line 1482: Validate valid attribute value
- Line 3111: Full implementation requires (multiple dependencies)
- Line 3192: Full implementation requires (multiple dependencies)
- Line 5063: Full implementation requires (multiple dependencies)
- Line 5131: If retroactive, update all existing copies
- Line 5150: Full implementation requires (multiple dependencies)
- Line 5176: Full implementation requires (multiple dependencies)
- Line 5204: Full implementation requires (multiple dependencies)
- Line 5232: Full implementation requires (multiple dependencies)
- Line 5248: Full implementation requires (multiple dependencies)
- Line 5273: Handle switches

### 1.2 Movement & Social Commands (17 items)
**File:** `SharpMUSH.Implementation/Commands/MoreCommands.cs`

**High Priority:**
- Line 835: Clear all FOLLOWING attributes pointing to us
- Line 899: Iterate through all objects with FOLLOWING attribute pointing to us
- Line 1774: Implement money transfer

**Medium Priority:**
- Line 1040: Notify others in room (exclude executor)
- Line 1418: Notify others in current location (exclude executor)
- Line 1478: Notify others inside object (exclude executor)
- Line 1490: Notify others in old location (exclude executor)
- Line 1709: Notify others in source location (exclude executor)
- Line 1875: Notify others in room (exclude executor and recipient)
- Line 1912: Notify others in room (exclude executor and recipient)
- Line 2270: Fix VERB evaluation - not evaluating correctly
- Line 2858: Implement proper context switching in parser

**Lock/Attribute Related:**
- Line 430: Implement lock flag storage
- Line 562: Persist the change to the database

### 1.3 Wizard Commands (7 items)
**File:** `SharpMUSH.Implementation/Commands/WizardCommands.cs`

**High Priority:**
- Line 1387: Boot only last active connection (match Penn behavior)
- Line 1689: Validate name and passwords
- Line 2102: Strip powers

**Medium Priority:**
- Line 670: Pipe through SPEAK()
- Line 691: Pipe through SPEAK()
- Line 1997: Pipe through SPEAK()

### 1.4 Channel Commands (6 items)
**Files:** Various channel command files

**High Priority:**
- Line 42 (ChannelOn.cs): Announce channel join
- Line 42 (ChannelOff.cs): Announce channel join

**Medium Priority:**
- Lines 33, 83, 135 (ChannelCommands.cs): Use standardized method
- Line 103 (ChannelCommands.cs): Change notification type based on first character

### 1.5 Mail Commands (4 items)
**Files:** Mail command files

**Medium Priority:**
- Line 90 (SendMail.cs): If AMAIL config true and AMAIL attribute set, trigger it
- Line 31 (AdminMail.cs): Deletes own mail, not all mail on server
- Line 67 (StatusMail.cs): Consider how IDs are displayed for mail output
- Line 74 (MessageListHelper.cs): Fix to use Locate() to find person

### 1.6 Building Commands (2 items)
**File:** `SharpMUSH.Implementation/Commands/BuildingCommands.cs`

**Medium Priority:**
- Line 386: Check if exit is unlinked, check @lock/link, handle ownership
- Line 617: Strip powers

### 1.7 Database Commands (2 items)
**File:** `SharpMUSH.Implementation/Commands/DatabaseCommands.cs`

**High Priority:**
- Line 186: NOT YET IMPLEMENTED
- Line 201: NOT YET IMPLEMENTED

### 1.8 Socket Commands (2 items)
**File:** `SharpMUSH.Implementation/Commands/SocketCommands.cs`

**Medium Priority:**
- Line 151: Confirm there is no SiteLock
- Line 176: Display disconnect banner

### 1.9 Single Token Commands (1 item)
**File:** `SharpMUSH.Implementation/Commands/SingleTokenCommands.cs`

**Low Priority:**
- Line 15: Better way to pick up where left off instead of re-parsing

---

## 2. Functions (58 items)

### 2.1 Attribute Functions (8 items)
**File:** `SharpMUSH.Implementation/Functions/AttributeFunctions.cs`

**High Priority:**
- Line 1034: Implement grep functionality (requires attribute service integration)
- Line 1523: CHECK TRUST AGAINST OBJECT
- Line 1524: Logic should live in EvaluateAttributeFunctionAsync
- Line 1628-1629: Target attribute handling with mediator & service

**Medium Priority:**
- Line 173: Update documentation (like default() now)
- Line 495: Check config, handle single space contents assumptions
- Line 525: Check config, handle single space contents assumptions

### 2.2 Dbref/Object Functions (12 items)
**File:** `SharpMUSH.Implementation/Functions/DbrefFunctions.cs`

**High Priority:**
- Line 235: Implement type, start, and count filtering
- Line 287: Implement follower tracking system
- Line 306: Implement following tracking system
- Line 833: Add regex matching support for search criteria
- Line 946: Implement proper next dbref tracking
- Line 1152: Exit may need editing

**Medium Priority:**
- Line 259: Create proper error constant
- Line 263: Turn Content into async enumerable
- Line 869: obj/attr for evaluation of bad results

### 2.3 Channel Functions (4 items)
**File:** `SharpMUSH.Implementation/Functions/ChannelFunctions.cs`

**High Priority:**
- Line 416: Query actual channel message history from database
- Line 618: Query actual message count from database

**Medium Priority:**
- Lines 91, 529: Use standardized method

### 2.4 Connection Functions (7 items)
**File:** `SharpMUSH.Implementation/Functions/ConnectionFunctions.cs`

**High Priority:**
- Line 587: Get All Players functionality
- Line 634: Create "mortal" viewer context (can't see hidden players)
- Line 1250: Get current @poll value from configuration/game state

**Medium Priority:**
- Lines 110, 124, 422, 438: CanSee in case of Dark

### 2.5 Information Functions (4 items)
**File:** `SharpMUSH.Implementation/Functions/InformationFunctions.cs`

**High Priority:**
- Line 168: Implement actual PID tracking and information retrieval
- Line 444: Implement WAIT and INDEPENDENT queue handling
- Line 462: Implement database-wide object counting
- Line 591: Implement actual quota checking when database iteration available

### 2.6 String Functions (6 items)
**File:** `SharpMUSH.Implementation/Functions/StringFunctions.cs`

**Medium Priority:**
- Line 88: Create HelperFunction for reused behavior
- Line 593: Turn into compiled regexes
- Line 1003: MModule.apply2 for attribute-function on each character
- Line 1008: Escape <>s properly
- Line 1026: ansi() needs to happen after/separately from replacements
- Line 1132: Fix background handling

### 2.7 Utility Functions (9 items)
**File:** `SharpMUSH.Implementation/Functions/UtilityFunctions.cs`

**High Priority:**
- Line 1303: Implement suggest (fuzzy string matching/suggestion algorithm)
- Line 1317: Implement stext (text file system integration)
- Line 1403: Implement textentries (text file system integration)
- Line 1410: Implement textfile (text file system integration)

**Medium Priority:**
- Line 25: Not compatible - can't indicate DBREF
- Line 60: Move to AnsiMarkup for parsed Markup
- Line 79: Handle background
- Line 131: Better handling for clear (tree structure issue)
- Line 134: Inline as function
- Line 679: Check if MarkupString is properly Immutable

### 2.8 Communication Functions (2 items)
**File:** `SharpMUSH.Implementation/Functions/CommunicationFunctions.cs`

**Medium Priority:**
- Lines 153, 389: Support room/obj format like PennMUSH

### 2.9 List Functions (2 items)
**File:** `SharpMUSH.Implementation/Functions/ListFunctions.cs`

**Low Priority:**
- Lines 75, 81: Indicate arg number in error messages

### 2.10 Time Functions (1 item)
**File:** `SharpMUSH.Implementation/Functions/TimeFunctions.cs`

**Low Priority:**
- Line 136: Handle more complex time calculations with modifiers

### 2.11 HTML Functions (3 items)
**File:** `SharpMUSH.Implementation/Functions/HTMLFunctions.cs`

**Low Priority:**
- Line 15: More complex implementation needed
- Lines 99, 148: Implement actual websocket/out-of-band communication

---

## 3. Services (42 items)

### 3.1 AttributeService (13 items)
**File:** `SharpMUSH.Library/Services/AttributeService.cs`

**High Priority:**
- Line 431-432: Implement Pattern Modes and CheckParents
- Line 433: GetAttributesAsync should return full path, not final attribute
- Line 434: CanViewAttribute memoization during list checks
- Line 448-450: Same issues repeated for different methods
- Line 538: Fix - object permissions also needed

**Medium Priority:**
- Lines 133, 233: Return full path, not just last piece
- Line 335: Not skipping function permission checks
- Lines 483, 515: Handle already set / not set scenarios

### 3.2 PermissionService (6 items)
**File:** `SharpMUSH.Library/Services/PermissionService.cs`

**High Priority:**
- Line 39: Implement missing functionality
- Lines 41-42: Confirm implementation and optimize for lists
- Lines 90-91: Confirm implementation and optimize for lists
- Line 174: Fix logic issue ('return true or true')

### 3.3 PennMUSH Database Converter (6 items)
**File:** `SharpMUSH.Library/Services/DatabaseConversion/PennMUSHDatabaseConverter.cs`

**High Priority:**
- Line 618: Convert to proper MarkupStrings instead of stripping
- Line 781: Convert escape sequences to proper MarkupStrings

**Medium Priority:**
- Line 206: Update God player name/password from PennMUSH data
- Line 270: Update Room #0 name from PennMUSH data
- Line 553: Set parent relationship
- Line 561: Set zone relationship

### 3.4 ValidateService (4 items)
**File:** `SharpMUSH.Library/Services/ValidateService.cs`

**Medium Priority:**
- Line 36: ValidAttributeNameRegex().IsMatch(value.ToPlainText())
- Line 144: Cache by name
- Line 151: Caching & ensuring enum can do globbing
- Line 201: Forbidden names

### 3.5 LockService (2 items)
**File:** `SharpMUSH.Library/Services/LockService.cs`

**High Priority:**
- Line 120: throw new NotImplementedException()

**Medium Priority:**
- Line 110: Optimize #TRUE calls (don't need to cache)

### 3.6 LocateService (2 items)
**File:** `SharpMUSH.Library/Services/LocateService.cs`

**High Priority:**
- Line 220: Fix Async
- Line 235: Review logic (may be incorrect)

### 3.7 WarningService (2 items)
**File:** `SharpMUSH.Library/Services/WarningService.cs`

**Medium Priority:**
- Line 165: Check if owner connected before notifying
- Line 312: Check for variable exits without DESTINATION or EXITTO attribute

### 3.8 ManipulateSharpObjectService (2 items)
**File:** `SharpMUSH.Library/Services/ManipulateSharpObjectService.cs`

**Medium Priority:**
- Line 178: Flag restrictions based on ownership/permissions
- Line 365: Confirm logic for ownership transfer permissions/restrictions

### 3.9 Other Services (5 items)

**MoveService:**
- Line 126: Implement quota checking when quota system available

**NotifyService:**
- Line 250: Implement when DBRef to handle mapping exists

**HookService:**
- Line 77: This is placeholder implementation

**INotifyService Interface:**
- Line 23: Add 'sender' for Noisy etc rules

**SqlService:**
- Line 21: Support multiple database types

---

## 4. Parser & Visitors (15 items)

### 4.1 SharpMUSHParserVisitor (13 items)
**File:** `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs`

**High Priority:**
- Line 746: Implement $-command matching for override
- Line 915: Implement lsargs (no immediate commands need it)
- Line 933: Implement Parsed Message alt

**Medium Priority:**
- Lines 135-136: Optimization - grab in-built ones at startup, move to Library Service
- Line 177: Check permissions
- Lines 202, 206: Reconsider placement, Context Depth not correct value
- Line 257: Consider adding ParserContexts as Arguments for optimization
- Line 383: Better channel name matching
- Line 399: Optimize
- Line 454: Check for @attribute syntax
- Line 848: Should Single Commands split?
- Line 1110: Doesn't work with QREG with evaluationstring

### 4.2 SharpMUSHBooleanExpressionVisitor (1 item)
**File:** `SharpMUSH.Implementation/Visitors/SharpMUSHBooleanExpressionVisitor.cs`

**Medium Priority:**
- Line 568: Attribute should be evaluated with %# = unlocker, %! = gated object

### 4.3 BooleanExpressionParser (1 item)
**File:** `SharpMUSH.Implementation/BooleanExpressionParser.cs`

**Low Priority:**
- Line 11: Allow evaluation to indicate if cache should be used for optimization

---

## 5. Database (7 items)

### 5.1 ArangoDB Implementation (6 items)
**File:** `SharpMUSH.Database.ArangoDB/ArangoDatabase.cs`

**High Priority:**
- Lines 1666-1667: Pattern matching - lazy implementation, doesn't support ` section
- Lines 1702-1703: Same issue - wildcard can match multiple attribute levels

**File:** `SharpMUSH.Database.ArangoDB/Migrations/Migration_CreateDatabase.cs`

**Low Priority:**
- Line 2781: Consider if needed for our purposes
- Line 2792: Find better way than breaking async flow

### 5.2 Database Interfaces (1 item)
**File:** `SharpMUSH.Library/ISharpDatabase.cs`

**Medium Priority:**
- Line 142: Consider return value (attribute pattern returns multiple attributes)

---

## 6. MarkupString & Formatting (19 items)

### 6.1 Core Markup (6 items)
**File:** `SharpMUSH.MarkupString/MarkupStringModule.fs`

**Medium Priority:**
- Line 35: Consider using built-in option type
- Line 49: Optimize ansi strings (don't re-initialize same tag sequentially)
- Line 680: Needs changing - should be able to composite functions

**File:** `SharpMUSH.MarkupString/Markup/Markup.fs`

**Medium Priority:**
- Line 108: Move to ANSI.fs (doesn't belong here)
- Line 125: Implement case that turns...

**File:** `SharpMUSH.MarkupString/ColumnModule.fs`

**Low Priority:**
- Line 27: Turn string into Markup

### 6.2 ANSI Library (2 items)
**File:** `SharpMUSH.MarkupString/Markup/ANSILibrary/ANSI.fs`

**Medium Priority:**
- Line 118: Handle ANSI colors
- Line 154: Clear needs to affect span, should be ahead of resultWithHyperlink

### 6.3 Documentation Rendering (1 item)
**File:** `SharpMUSH.Documentation/MarkdownToAsciiRenderer/AsciiListRenderer.cs`

**Low Priority:**
- Line 9: Create Queue on Renderer for list items

**File:** `SharpMUSH.Documentation/MarkdownToAsciiRenderer/AsciiTableRenderer.cs`

**Low Priority:**
- Line 18: Copied and adjusted from HTML code

### 6.4 Helpfiles (2 items)
**File:** `SharpMUSH.Documentation/Helpfiles.cs`

**Low Priority:**
- Lines 20, 30: Add logging

---

## 7. Tests (17 items)

### 7.1 Skipped Tests (Multiple files)

**High Priority (Failing Tests):**
- `CommandUnitTests.cs` Line 32: Need eval vs noparse evaluation
- `DatabaseCommandTests.cs` Line 240: Bug - keeps reading and loops
- `ConfigCommandTests.cs` Line 19: Marked as TODO
- `GeneralCommandTests.cs` Lines 334, 404, 418, 432, 460, 503, 534: Multiple failing tests
- `CommunicationCommandTests.cs` Lines 241, 382: Failing tests
- `ChannelFunctionUnitTests.cs` Line 134: Failing test
- `ExpandedDataTests.cs` Line 49: Failing behavior
- `MotdDataTests.cs` Line 71: Failing test
- `MailFunctionUnitTests.cs` Line 137: Failing test

**Medium Priority (Test Infrastructure):**
- `RoomsAndMovementTests.cs` Line 13: Add tests
- `JsonFunctionUnitTests.cs` Lines 74, 88: Implement attribute setting/connection mocking
- `ListFunctionUnitTests.cs` Lines 73, 81-82, 100: Various evaluation issues
- `StringFunctionUnitTests.cs` Lines 254, 257, 266, 269: Fix decompose functions
- `MathFunctionUnitTests.cs` Line 62: Should return 10, not 10.0

**Low Priority:**
- `Align.cs` Lines 199, 212: Failing tests
- `InsertAt.cs` Line 20: Investigate Optimize behavior

---

## 8. Telnet Protocol Handlers (4 items)

**File:** `SharpMUSH.Implementation/Handlers/Telnet/`

**Medium Priority - All Need Implementation:**
- `TelnetGMCPHandler.cs` Line 10: Implement GMCP
- `TelnetMSDPHandler.cs` Line 10: Implement MSDP
- `TelnetMSSPHandler.cs` Line 10: Implement MSSP
- `TelnetOutputHandler.cs` Line 10: Implement output handling

---

## 9. Substitutions (7 items)

### 9.1 Variable Substitutions (4 items)
**File:** `SharpMUSH.Implementation/Substitutions/Substitutions.cs`

**Medium Priority:**
- Line 34: ACCENTED ENACTOR NAME
- Line 35: MONIKER ENACTOR NAME
- Line 80: LAST COMMAND BEFORE EVALUATION
- Line 81: LAST COMMAND AFTER EVALUATION

### 9.2 Test Substitutions (3 items)
**File:** `SharpMUSH.Tests/Substitutions/RegistersUnitTests.cs`

**Low Priority:**
- Lines 25-27: Require full server integration

---

## 10. Message Consumers (3 items)

**File:** `SharpMUSH.Server/Consumers/InputMessageConsumers.cs`

**Medium Priority:**
- Line 51: Implement GMCP signal handling
- Line 67: Implement MSDP update handling
- Line 83: Implement NAWS update handling

---

## 11. Other/Miscellaneous (10 items)

### 11.1 Helper Functions (5 items)
**File:** `SharpMUSH.Library/HelperFunctions.cs`

**Medium Priority:**
- Lines 218, 237, 250, 315: Validate Attribute Pattern
- Line 348: Make split versions for Patterns and Regex Patterns

### 11.2 Configuration (1 item)
**File:** `SharpMUSH.Configuration/ReadPennMUSHConfig.cs`

**Low Priority:**
- Line 43: Use Regex to split values

### 11.3 Server (2 items)
**File:** `SharpMUSH.Server/Startup.cs`

**High Priority:**
- Line 66: PasswordHasher may not be compatible with PennMUSH Passwords

**File:** `SharpMUSH.Server/StartupHandler.cs`

**Medium Priority:**
- Line 19: Move CRON handling to own background handler

### 11.4 Library Interfaces (2 items)
**File:** `SharpMUSH.Library/ParserInterfaces/ParserState.cs`

**Medium Priority:**
- Line 214: Validate Register Pattern

**File:** `SharpMUSH.Library/Queries/ScheduleQuery.cs`

**Low Priority:**
- Line 10: Get non-semaphore data

**File:** `SharpMUSH.Library/Requests/QueueCommandListRequest.cs`

**Medium Priority:**
- Lines 7, 23: Make it return new PID for output

### 11.5 Extensions (2 items)
**File:** `SharpMUSH.Library/Extensions/StringExtensions.cs`

**Low Priority:**
- Line 11: Turn into proper method of MModule

**File:** `SharpMUSH.Library/Extensions/SharpAttributeExtensions.cs`

**Medium Priority:**
- Line 44: Command Pattern - code repeated in several places

### 11.6 Handlers (1 item)
**File:** `SharpMUSH.Implementation/Handlers/ChannelMessageRequestHandler.cs`

**Low Priority:**
- Line 22: Do Mogrification stuff here

---

## Implementation Priority Recommendations

### Phase 1: Critical Foundation (High Impact, Many Dependencies)
1. **Q-Register System** - Blocks ~15 command TODOs
2. **AttributeService Pattern Modes** - Affects many functions and commands
3. **Permission System Fixes** - Security critical, affects many features
4. **Database Pattern Matching** - Core functionality for attribute trees

### Phase 2: Core Functionality
1. **Command Queue System** - Enables proper command execution
2. **Follower/Following Tracking** - Social features
3. **Lock System Completion** - Security and access control
4. **Halt Functionality** - Command control

### Phase 3: Communication & Social
1. **Channel System Polish** - Notifications, standardized methods
2. **SPEAK() Integration** - Multiple wizard commands need this
3. **Movement Notifications** - Room awareness
4. **Mail System** - AMAIL triggers, better display

### Phase 4: Parser & Optimization
1. **$-Command Matching** - Override functionality
2. **Parser Optimizations** - Performance improvements
3. **Context Switching** - Proper evaluation contexts
4. **QREG with Evaluation Strings** - Advanced parsing

### Phase 5: Functions & Compatibility
1. **Database Query Functions** - Finding objects, statistics
2. **Connection Functions** - Player visibility, Dark handling
3. **PennMUSH Format Support** - room/obj formats
4. **Quota System** - Resource management

### Phase 6: Advanced Features
1. **Telnet Protocol Handlers** - GMCP, MSDP, MSSP, NAWS
2. **Text File Functions** - suggest, stext, textentries, textfile
3. **MarkupString Optimizations** - ANSI handling, formatting
4. **Regex Compilation** - String function performance

### Phase 7: Polish & Testing
1. **Fix Failing Tests** - Ensure quality
2. **Add Missing Tests** - Coverage
3. **Documentation Updates** - Keep docs in sync
4. **Code Cleanup** - Remove duplicated code

---

## Dependency Map

**Q-Register System enables:**
- Environment arguments (%0-%9)
- Q-register management (clearregs, localize)
- Pattern matching (/match)
- Queue management
- Stack rewinding
- NOBREAK switch

**AttributeService Pattern Modes enable:**
- Wildcard attribute matching
- Parent checking
- Full path returns
- Permission memoization

**Permission System fixes enable:**
- Secure command execution
- Proper access control
- Trust checking
- Visibility controls

**Queue System enables:**
- Inline vs queued execution
- PID tracking
- WAIT and INDEPENDENT queues
- @STARTUP triggers

**Database improvements enable:**
- Full attribute tree support
- Efficient object searching
- Quota tracking
- Statistics queries

---

## Notes

- Many TODOs are interconnected - implementing one may unblock several others
- Some TODOs are marked as "low priority" because they depend on larger systems
- Test TODOs should be addressed as related functionality is implemented
- Documentation TODOs can be tackled independently

This plan should be updated as items are completed and new dependencies are discovered.
