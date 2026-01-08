# Comprehensive Skipped Tests Documentation

**Total Skipped Tests: 257**

This document catalogs ALL skipped tests in the SharpMUSH test suite, organized by category and reason.
Each test is documented with its location, reason for being skipped, and line number.

## Test Status Legend

Checkbox indicators show test status when unskipped:
- `[ ]` **UNTESTED** - Not yet attempted to unskip
- `[x]` **PASS** - Test passes when unskipped (can be permanently unskipped)
- `[!]` **FAIL** - Test fails when unskipped (needs fixing before unskipping)
- `[~]` **HANG** - Test hangs/timeouts when unskipped (needs investigation)
- `[?]` **NEEDS_INFRASTRUCTURE** - Requires database/service setup (cannot test without infrastructure)

## Progress Summary

- **Tested**: 177 tests (categorized)
- **Passing**: 8 tests (3.1%)
- **Failing**: 129 tests (categorized)
- **Hanging**: 2 tests
- **Needs Infrastructure**: 38 tests
- **Remaining**: 80 tests

Last updated: Batch 5 - FlagAndPowerCommandTests verified (8 passing, 4 failing)

## Table of Contents

- [Summary by Category](#summary-by-category)
- [Summary by Reason](#summary-by-reason)
- [Detailed Test Listings](#detailed-test-listings)
  - [Configuration/Environment Issues](#configurationenvironment-issues)
  - [Failing - Needs Investigation](#failing---needs-investigation)
  - [Implementation Issues](#implementation-issues)
  - [Integration Test - Requires Database/Service Setup](#integration-test---requires-databaseservice-setup)
  - [Not Yet Implemented](#not-yet-implemented)
  - [Other](#other)
  - [Test Infrastructure Issues](#test-infrastructure-issues)

## Summary by Category

- **Client**: 3 tests
- **Commands**: 175 tests
- **Database**: 1 tests
- **Documentation**: 2 tests
- **Functions**: 40 tests
- **Parser**: 3 tests
- **Performance**: 1 tests
- **Services**: 32 tests

## Summary by Reason

- **Configuration/Environment Issues**: 1 tests
- **Failing - Needs Investigation**: 34 tests
- **Implementation Issues**: 10 tests
- **Integration Test - Requires Database/Service Setup**: 41 tests
- **Not Yet Implemented**: 117 tests
- **Other**: 32 tests
- **Test Infrastructure Issues**: 22 tests

---

## Detailed Test Listings

### Configuration/Environment Issues

**Total: 1 tests**

#### `Functions/NewPennMUSHFunctionTests.cs`

- [ ] **CONFIG_ReturnsConfigurationValues** (Line 110)
  - **Reason**: Config values are dynamic and environment-specific

### Failing - Needs Investigation

**Total: 34 tests**

#### `Commands/AttributeCommandTests.cs`

- [!] **Test_CopyAttribute_Direct** (Line 69)
  - **Reason**: Failing Test - Needs Investigation
- [!] **Test_CopyAttribute_Basic** (Line 104)
  - **Reason**: Failing Test - Needs Investigation
- [!] **Test_CopyAttribute_MultipleDestinations** (Line 136)
  - **Reason**: Failing Test - Needs Investigation
- [!] **Test_MoveAttribute_Basic** (Line 168)
  - **Reason**: Failing Test - Needs Investigation
- [!] **Test_WipeAttributes_AllAttributes** (Line 200)
  - **Reason**: Failing Test - Needs Investigation
- [!] **Test_AtrLock_LockAndUnlock** (Line 235)
  - **Reason**: Failing Test - Needs Investigation

#### `Commands/BuildingCommandTests.cs`

- [ ] **NameObject** (Line 133)
  - **Reason**: Failing Test - Needs Investigation

#### `Commands/CommunicationCommandTests.cs`

- [ ] **ComListEmpty** (Line 382)
  - **Reason**: TODO: Failing Test. Requires investigation.

#### `Commands/ConfigCommandTests.cs`

- [ ] **ConfigCommand_CategoryArg_ShowsCategoryOptions** (Line 34)
  - **Reason**: Failing. Needs Investigation

#### `Commands/FlagAndPowerCommandTests.cs`

- [x] **Flag_Add_CreatesNewFlag** (Line 42)
  - **Reason**: Failing. Needs Investigation
- [x] **Flag_Add_PreventsSystemFlagCreation** (Line 72)
  - **Reason**: Failing. Needs Investigation
- [x] **Flag_Add_PreventsDuplicateFlags** (Line 92)
  - **Reason**: Failing. Needs Investigation
- [x] **Flag_Delete_RemovesNonSystemFlag** (Line 121)
  - **Reason**: Failing. Needs Investigation
- [!] **Flag_Delete_HandlesNonExistentFlag** (Line 166)
  - **Reason**: Failing. Needs Investigation
- [x] **Power_Delete_RemovesNonSystemPower** (Line 246)
  - **Reason**: Failing. Needs Investigation
- [!] **Power_Delete_HandlesNonExistentPower** (Line 296)
  - **Reason**: Failing. Needs Investigation
- [x] **Flag_Disable_DisablesNonSystemFlag** (Line 343)
  - **Reason**: Failing. Needs Investigation
- [x] **Flag_Enable_EnablesDisabledFlag** (Line 374)
  - **Reason**: Failing. Needs Investigation
- [!] **Flag_Disable_PreventsSystemFlagDisable** (Line 406)
  - **Reason**: Failing. Needs Investigation
- [ ] **Power_Enable_EnablesDisabledPower** (Line 452)
  - **Reason**: Failing. Needs Investigation
- [ ] **Power_Disable_PreventsSystemPowerDisable** (Line 484)
  - **Reason**: Failing. Needs Investigation

#### `Commands/GeneralCommandTests.cs`

- [ ] **Command_ShowsCommandInfo** (Line 404)
  - **Reason**: TODO: Failing
- [ ] **Attribute_DisplaysAttributeInfo** (Line 535)
  - **Reason**: TODO: Failing

#### `Commands/WizardCommandTests.cs`

- [ ] **Hide_NoSwitch_TogglesHidden** (Line 222)
  - **Reason**: Failing. Needs Investigation
- [ ] **Hide_OnSwitch_SetsHidden** (Line 271)
  - **Reason**: Failing. Needs Investigation
- [ ] **Hide_NoSwitch_UnsetsHidden** (Line 291)
  - **Reason**: Failing. Needs Investigation
- [ ] **Hide_OffSwitch_UnsetsHidden** (Line 311)
  - **Reason**: Failing. Needs Investigation
- [ ] **Hide_AlreadyVisible_ShowsAppropriateMessage** (Line 350)
  - **Reason**: Failing. Needs Investigation

#### `Commands/ZoneCommandTests.cs`

- [ ] **ZMRUserDefinedCommandTest** (Line 237)
  - **Reason**: Failing and needs to be fixed.
- [ ] **PersonalZoneUserDefinedCommandTest** (Line 301)
  - **Reason**: Failing and needs to be fixed.

#### `Functions/ChannelFunctionUnitTests.cs`

- [ ] **Cstatus_WithNonMember_ReturnsOff** (Line 134)
  - **Reason**: TODO: Failing test - needs investigation

#### `Functions/MailFunctionUnitTests.cs`

- [ ] **Mail_InvalidMessage_ReturnsError** (Line 137)
  - **Reason**: TODO: Failing test - needs investigation

#### `Functions/ObjectFunctionUnitTests.cs`

- [ ] **Nlsearch** (Line 77)
  - **Reason**: Failing
- [ ] **Nsearch** (Line 86)
  - **Reason**: Failing

### Implementation Issues

**Total: 10 tests**

#### `Commands/DebugVerboseTests.cs`

- [ ] **AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug** (Line 140)
  - **Reason**: @trigger command syntax needs investigation - implementation is complete
- [ ] **AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug** (Line 167)
  - **Reason**: @trigger command syntax needs investigation - implementation is complete

#### `Functions/ListFunctionUnitTests.cs`

- [ ] **Sort** (Line 215)
  - **Reason**: Implementation incomplete - sort function needs full implementation
- [ ] **Matchall** (Line 310)
  - **Reason**: Implementation doesn't match expected behavior - returns wrong indices
- [ ] **Itemize** (Line 387)
  - **Reason**: Formatting logic incorrect - conjunction and punctuation not formatted properly
- [ ] **FilterBool** (Line 424)
  - **Reason**: Lambda function syntax not fully supported - #lambda/\\%0 pattern needs implementation
- [ ] **Splice** (Line 441)
  - **Reason**: Implementation issue - splice logic doesn't handle output separator correctly
- [ ] **ListInsert** (Line 460)
  - **Reason**: Indexing issue - 1-based position handling for insert incorrect
- [ ] **Elist** (Line 491)
  - **Reason**: Formatting logic incorrect - same issue as itemize

#### `Functions/MiscFunctionUnitTests.cs`

- [ ] **Match** (Line 32)
  - **Reason**: Causes deadlock - implementation triggers existing GetAttributeQueryHandler/GetExitsQueryHandler .GetAwaiter().GetResult() issues

### Integration Test - Requires Database/Service Setup

**Total: 41 tests**

#### `Commands/MessageCommandTests.cs`

- [?] **MessageRemitSwitch** (Line 178)
  - **Reason**: Requires room setup
- [?] **MessageOemitSwitch** (Line 185)
  - **Reason**: Requires multiple objects

#### `Commands/VerbCommandTests.cs`

- [?] **VerbPermissionDenied** (Line 118)
  - **Reason**: Requires proper permission setup
- [?] **VerbExecutesAwhat** (Line 125)
  - **Reason**: Requires AWHAT command list execution verification

#### `Commands/WarningCommandTests.cs`

- [?] **WCheckCommand_WithMe_ChecksOwnedObjects** (Line 150)
  - **Reason**: Integration test - requires proper object setup
- [?] **WCheckCommand_WithAll_RequiresWizard** (Line 166)
  - **Reason**: Integration test - requires wizard permissions

#### `Functions/JsonFunctionUnitTests.cs`

- [?] **Test_Oob_SendsGmcpMessages** (Line 82)
  - **Reason**: Requires connection setup

#### `Functions/MessageFunctionTests.cs`

- [?] **MessageHashHashReplacement** (Line 120)
  - **Reason**: Requires attribute setup
- [?] **MessageNoSideFxDisabled** (Line 127)
  - **Reason**: Requires configuration setup
- [?] **MessageRemitSwitch** (Line 134)
  - **Reason**: Requires room setup
- [?] **MessageOemitSwitch** (Line 141)
  - **Reason**: Requires multiple objects setup

#### `Functions/RegexFunctionUnitTests.cs`

- [?] **Regrep** (Line 181)
  - **Reason**: Requires attribute service integration
- [?] **Regrepi** (Line 190)
  - **Reason**: Requires attribute service integration

#### `Performance/ActualPerformanceValidation.cs`

- [?] **MeasureActualDoListPerformance** (Line 20)
  - **Reason**: Manual performance validation - requires actual servers running on 127.0.0.1:4201

#### `Services/EventServiceTests.cs`

- [?] **TriggerEventWithNoHandlerConfigured** (Line 16)
  - **Reason**: Integration test - requires database setup with event_handler configured
- [?] **TriggerEventWithHandler** (Line 31)
  - **Reason**: Integration test - requires database setup with event_handler and attributes configured
- [?] **TriggerEventWithSystemEnactor** (Line 43)
  - **Reason**: Integration test - requires database setup

#### `Services/MoveServiceTests.cs`

- [?] **NoLoopWithSimpleMove** (Line 37)
  - **Reason**: Integration test - requires database setup
- [?] **DetectsDirectLoop** (Line 45)
  - **Reason**: Integration test - requires database setup
- [?] **DetectsIndirectLoop** (Line 53)
  - **Reason**: Integration test - requires database setup
- [?] **NoLoopIntoRoom** (Line 61)
  - **Reason**: Integration test - requires database setup
- [?] **ExecuteMoveAsyncWithValidMove** (Line 69)
  - **Reason**: Integration test - requires database setup
- [?] **ExecuteMoveAsyncFailsOnLoop** (Line 78)
  - **Reason**: Integration test - requires database setup
- [?] **ExecuteMoveAsyncFailsOnPermission** (Line 87)
  - **Reason**: Integration test - requires database setup
- [?] **ExecuteMoveAsyncTriggersEnterHooks** (Line 96)
  - **Reason**: Integration test - requires database setup
- [?] **ExecuteMoveAsyncTriggersLeaveHooks** (Line 105)
  - **Reason**: Integration test - requires database setup
- [?] **ExecuteMoveAsyncTriggersTeleportHooks** (Line 114)
  - **Reason**: Integration test - requires database setup

#### `Services/PennMUSHDatabaseConverterTests.cs`

- [?] **ConversionResultIncludesStatistics** (Line 41)
  - **Reason**: Creates objects in shared database that affect other tests - needs isolated database

#### `Services/WarningLockChecksTests.cs`

- [?] **LockChecks_Integration_ValidLock_NoWarnings** (Line 93)
  - **Reason**: Requires database and lock service setup
- [?] **LockChecks_Integration_InvalidLock_TriggersWarning** (Line 105)
  - **Reason**: Requires database and lock service setup
- [?] **LockChecks_Integration_MultipleLocks_ChecksAll** (Line 117)
  - **Reason**: Requires database and lock service setup
- [?] **LockChecks_Integration_EmptyLock_Skipped** (Line 129)
  - **Reason**: Requires database and lock service setup
- [?] **LockChecks_Integration_GoingObjectReference_TriggersWarning** (Line 141)
  - **Reason**: Requires database and lock service setup

#### `Services/WarningNoWarnTests.cs`

- [?] **WarningService_SkipsObjectsWithNoWarn** (Line 59)
  - **Reason**: Integration test - requires database setup
- [?] **WarningService_SkipsObjectsWithOwnerNoWarn** (Line 69)
  - **Reason**: Integration test - requires database setup
- [?] **WarningService_SkipsGoingObjects** (Line 79)
  - **Reason**: Integration test - requires database setup
- [?] **BackgroundService_RunsAtConfiguredInterval** (Line 89)
  - **Reason**: Integration test - requires service setup
- [?] **BackgroundService_DisabledWhenIntervalZero** (Line 99)
  - **Reason**: Integration test - requires service setup

#### `Services/WarningTopologyTests.cs`

- [ ] **CheckExitWarnings_UnlinkedExit_DetectsWarning** (Line 90)
  - **Reason**: Requires full database and exit setup
- [ ] **CheckExitWarnings_OnewayExit_DetectsWarning** (Line 103)
  - **Reason**: Requires full database and exit setup
- [ ] **CheckExitWarnings_MultipleReturnExits_DetectsWarning** (Line 118)
  - **Reason**: Requires full database and exit setup

### Not Yet Implemented

**Total: 117 tests**

#### `Commands/AdminCommandTests.cs`

- [!] **ShutdownCommand** (Line 54)
  - **Reason**: Not Yet Implemented
- [!] **RestartCommand** (Line 65)
  - **Reason**: Not Yet Implemented
- [!] **PurgeCommand** (Line 76)
  - **Reason**: Not Yet Implemented
- [!] **ReadcacheCommand** (Line 98)
  - **Reason**: Not Yet Implemented

#### `Commands/BuildingCommandTests.cs`

- [!] **SetParent** (Line 419)
  - **Reason**: Not Yet Implemented - replaced by ParentSetAndGet
- [!] **ChownObject** (Line 436)
  - **Reason**: Not Yet Implemented
- [!] **UnlinkExit** (Line 499)
  - **Reason**: Not Yet Implemented

#### `Commands/ChannelCommandTests.cs`

- [!] **ChatCommand** (Line 63)
  - **Reason**: Not Yet Implemented
- [!] **ChannelCommand** (Line 76)
  - **Reason**: Not Yet Implemented
- [!] **AddcomCommand** (Line 113)
  - **Reason**: Not Yet Implemented
- [!] **DelcomCommand** (Line 124)
  - **Reason**: Not Yet Implemented
- [!] **ClistCommand** (Line 135)
  - **Reason**: Not Yet Implemented
- [!] **ComlistCommand** (Line 146)
  - **Reason**: Not Yet Implemented
- [!] **ComtitleCommand** (Line 157)
  - **Reason**: Not Yet Implemented

#### `Commands/CommunicationCommandTests.cs`

- [!] **LemitBasic** (Line 93)
  - **Reason**: Not yet implemented
- [!] **RemitBasic** (Line 106)
  - **Reason**: Not yet implemented
- [!] **OemitBasic** (Line 119)
  - **Reason**: Not yet implemented
- [!] **ZemitBasic** (Line 132)
  - **Reason**: Not yet implemented
- [!] **NsemitBasic** (Line 145)
  - **Reason**: Not yet implemented
- [!] **NslemitBasic** (Line 159)
  - **Reason**: Not yet implemented
- [!] **NsremitBasic** (Line 172)
  - **Reason**: Not yet implemented
- [!] **NsoemitBasic** (Line 185)
  - **Reason**: Not yet implemented
- [!] **NspemitBasic** (Line 198)
  - **Reason**: Not yet implemented
- [!] **NszemitBasic** (Line 211)
  - **Reason**: Not yet implemented

#### `Commands/ConfigCommandTests.cs`

- [!] **MonikerCommand** (Line 76)
  - **Reason**: Not Yet Implemented
- [!] **MotdCommand** (Line 87)
  - **Reason**: Not Yet Implemented
- [!] **WizmotdCommand** (Line 111)
  - **Reason**: Not Yet Implemented
- [!] **RejectmotdCommand** (Line 122)
  - **Reason**: Not Yet Implemented
- [!] **DoingCommand** (Line 133)
  - **Reason**: Not Yet Implemented

#### `Commands/ControlFlowCommandTests.cs`

- [!] **SelectCommand** (Line 20)
  - **Reason**: Not Yet Implemented
- [!] **SwitchCommand** (Line 31)
  - **Reason**: Not Yet Implemented
- [!] **BreakCommand** (Line 42)
  - **Reason**: Not Yet Implemented
- [!] **AssertCommand** (Line 53)
  - **Reason**: Not Yet Implemented
- [!] **RetryCommand** (Line 64)
  - **Reason**: Not Yet Implemented
- [!] **IncludeCommand** (Line 89)
  - **Reason**: Not Yet Implemented

#### `Commands/DatabaseCommandTests.cs`

- [!] **ListCommand** (Line 79)
  - **Reason**: Not Yet Implemented
- [!] **UnrecycleCommand** (Line 90)
  - **Reason**: Not Yet Implemented
- [!] **DisableCommand** (Line 101)
  - **Reason**: Not Yet Implemented
- [!] **EnableCommand** (Line 112)
  - **Reason**: Not Yet Implemented

#### `Commands/GameCommandTests.cs`

- [!] **BuyCommand** (Line 20)
  - **Reason**: Not Yet Implemented
- [!] **ScoreCommand** (Line 31)
  - **Reason**: Not Yet Implemented
- [!] **TeachCommand** (Line 42)
  - **Reason**: Not Yet Implemented
- [!] **FollowCommand** (Line 53)
  - **Reason**: Not Yet Implemented
- [!] **UnfollowCommand** (Line 64)
  - **Reason**: Not Yet Implemented
- [!] **DesertCommand** (Line 75)
  - **Reason**: Not Yet Implemented
- [!] **DismissCommand** (Line 86)
  - **Reason**: Not Yet Implemented
- [!] **WithCommand** (Line 145)
  - **Reason**: Not Yet Implemented

#### `Commands/GeneralCommandTests.cs`

- [!] **CommandAliasRuns** (Line 41)
  - **Reason**: Not yet implemented properly
- [!] **DolistCommand** (Line 73)
  - **Reason**: Not Yet Implemented

#### `Commands/LogCommandTests.cs`

- [!] **LogwipeCommand** (Line 91)
  - **Reason**: Not Yet Implemented

#### `Commands/MailCommandTests.cs`

- [!] **MailCommand** (Line 20)
  - **Reason**: Not Yet Implemented
- [!] **MaliasCommand** (Line 31)
  - **Reason**: Not Yet Implemented

#### `Commands/MiscCommandTests.cs`

- [!] **VerbCommand** (Line 21)
  - **Reason**: Not Yet Implemented
- [!] **SweepCommand** (Line 32)
  - **Reason**: Not Yet Implemented
- [!] **EditCommand** (Line 43)
  - **Reason**: Not Yet Implemented
- [!] **BriefCommand** (Line 120)
  - **Reason**: Not Yet Implemented
- [!] **WhoCommand** (Line 131)
  - **Reason**: Not Yet Implemented
- [!] **SessionCommand** (Line 142)
  - **Reason**: Not Yet Implemented
- [!] **QuitCommand** (Line 153)
  - **Reason**: Not Yet Implemented
- [!] **ConnectCommand** (Line 164)
  - **Reason**: Not Yet Implemented
- [!] **PromptCommand** (Line 175)
  - **Reason**: Not Yet Implemented
- [!] **NspromptCommand** (Line 186)
  - **Reason**: Not Yet Implemented

#### `Commands/MovementCommandTests.cs`

- [!] **GotoCommand** (Line 24)
  - **Reason**: Not Yet Implemented
- [!] **EnterCommand** (Line 85)
  - **Reason**: Not Yet Implemented

#### `Commands/NetworkCommandTests.cs`

- [!] **HttpCommand** (Line 20)
  - **Reason**: Not Yet Implemented
- [!] **SqlCommand** (Line 31)
  - **Reason**: Not Yet Implemented
- [!] **MapsqlCommand** (Line 42)
  - **Reason**: Not Yet Implemented
- [!] **SocksetCommand** (Line 65)
  - **Reason**: Not Yet Implemented
- [!] **SlaveCommand** (Line 76)
  - **Reason**: Not Yet Implemented

#### `Commands/NotificationCommandTests.cs`

- [!] **MessageCommand** (Line 20)
  - **Reason**: Not Yet Implemented
- [!] **RespondCommand** (Line 31)
  - **Reason**: Not Yet Implemented
- [!] **RwallCommand** (Line 42)
  - **Reason**: Not Yet Implemented
- [!] **WarningsCommand** (Line 53)
  - **Reason**: Not Yet Implemented
- [!] **WcheckCommand** (Line 64)
  - **Reason**: Not Yet Implemented
- [!] **SuggestCommand** (Line 75)
  - **Reason**: Not Yet Implemented

#### `Commands/ObjectManipulationCommandTests.cs`

- [!] **UseCommand** (Line 103)
  - **Reason**: Not Yet Implemented
- [!] **DestroyCommand** (Line 174)
  - **Reason**: Not Yet Implemented
- [!] **NukeCommand** (Line 185)
  - **Reason**: Not Yet Implemented
- [!] **UndestroyCommand** (Line 196)
  - **Reason**: Not Yet Implemented

#### `Commands/QuotaCommandTests.cs`

- [!] **SquotaCommand** (Line 20)
  - **Reason**: Not Yet Implemented

#### `Commands/SocialCommandTests.cs`

- [!] **WhisperCommand** (Line 54)
  - **Reason**: Not Yet Implemented

#### `Commands/SystemCommandTests.cs`

- [!] **FlagCommand** (Line 20)
  - **Reason**: Not Yet Implemented
- [!] **PowerCommand** (Line 31)
  - **Reason**: Not Yet Implemented
- [!] **HookCommand** (Line 42)
  - **Reason**: Not Yet Implemented
- [!] **FunctionCommand** (Line 53)
  - **Reason**: Not Yet Implemented
- [!] **CommandCommand** (Line 64)
  - **Reason**: Not Yet Implemented
- [!] **HideCommand** (Line 75)
  - **Reason**: Not Yet Implemented
- [!] **KickCommand** (Line 86)
  - **Reason**: Not Yet Implemented
- [!] **AttributeCommand** (Line 97)
  - **Reason**: Not Yet Implemented
- [!] **AtrlockCommand** (Line 108)
  - **Reason**: Not Yet Implemented
- [!] **AtrchownCommand** (Line 119)
  - **Reason**: Not Yet Implemented
- [!] **FirstexitCommand** (Line 130)
  - **Reason**: Not Yet Implemented

#### `Commands/UtilityCommandTests.cs`

- [!] **FindCommand** (Line 135)
  - **Reason**: Not Yet Implemented
- [!] **SearchCommand** (Line 146)
  - **Reason**: Not Yet Implemented
- [!] **EntrancesCommand** (Line 157)
  - **Reason**: Not Yet Implemented
- [!] **StatsCommand** (Line 168)
  - **Reason**: Not Yet Implemented
- [!] **WhereisCommand** (Line 215)
  - **Reason**: Not Yet Implemented

#### `Commands/WizardCommandTests.cs`

- [!] **HaltCommand** (Line 20)
  - **Reason**: Not Yet Implemented
- [!] **PsCommand** (Line 52)
  - **Reason**: Not Yet Implemented
- [!] **PsWithTarget** (Line 63)
  - **Reason**: Not Yet Implemented
- [!] **TriggerCommand** (Line 74)
  - **Reason**: Not Yet Implemented
- [!] **DbckCommand** (Line 134)
  - **Reason**: Not Yet Implemented
- [!] **DumpCommand** (Line 145)
  - **Reason**: Not Yet Implemented
- [!] **QuotaCommand** (Line 156)
  - **Reason**: Not Yet Implemented
- [!] **AllquotaCommand** (Line 167)
  - **Reason**: Not Yet Implemented
- [!] **BootCommand** (Line 178)
  - **Reason**: Not Yet Implemented

#### `Functions/AttributeFunctionUnitTests.cs`

- [!] **Test_Zfun_NotImplemented** (Line 201)
  - **Reason**: Zones Not Yet Implemented
- [!] **Regrep** (Line 210)
  - **Reason**: Not Yet Implemented
- [!] **Regrepi** (Line 219)
  - **Reason**: Not Yet Implemented
- [!] **Regedit** (Line 228)
  - **Reason**: Not Yet Implemented

#### `Functions/CommunicationFunctionUnitTests.cs`

- [!] **Zemit** (Line 77)
  - **Reason**: Zone system not yet implemented

#### `Functions/FormattingFunctionUnitTests.cs`

- [!] **Tag** (Line 21)
  - **Reason**: Not Yet Implemented
- [!] **Endtag** (Line 38)
  - **Reason**: Not Yet Implemented

#### `Functions/InformationFunctionUnitTests.cs`

- [!] **Name** (Line 29)
  - **Reason**: Not Yet Implemented

#### `Functions/MiscFunctionUnitTests.cs`

- [!] **Foreach** (Line 23)
  - **Reason**: Not Yet Implemented
- [!] **JsonMap** (Line 41)
  - **Reason**: Not Yet Implemented
- [!] **Ctu** (Line 130)
  - **Reason**: Not Yet Implemented

### Other

**Total: 32 tests**

#### `Client/AdminConfigServiceTests.cs`

- [ ] **ImportFromConfigFileAsync_ValidConfig_ShouldNotThrow** (Line 25)
  - **Reason**: Skip
- [ ] **ImportFromConfigFileAsync_HttpError_ShouldHandleGracefully** (Line 53)
  - **Reason**: Skip
- [ ] **GetOptions_ShouldReturnConfiguration** (Line 82)
  - **Reason**: Skip

#### `Commands/AtListCommandTests.cs`

- [ ] **List_Flags_Lowercase_DisplaysLowercaseFlagList** (Line 55)
  - **Reason**: Switch parsing issue with multiple switches - needs investigation

#### `Commands/CommunicationCommandTests.cs`

- [ ] **AddComInvalidArgs** (Line 241)
  - **Reason**: TODO
- [ ] **ComTitleBasic** (Line 314)
  - **Reason**: TOOD

#### `Commands/ConfigCommandTests.cs`

- [~] **ConfigCommand_NoArgs_ListsCategories** (Line 19)
  - **Reason**: TODO

#### `Commands/GeneralCommandTests.cs`

- [ ] **Restart_ValidObject_Restarts** (Line 334)
  - **Reason**: TODO
- [ ] **Function_ListsGlobalFunctions** (Line 418)
  - **Reason**: TODO
- [ ] **Function_ShowsFunctionInfo** (Line 432)
  - **Reason**: TODO
- [ ] **Trigger_QueuesAttribute** (Line 460)
  - **Reason**: TODO
- [ ] **PS_ShowsQueueStatus** (Line 504)
  - **Reason**: TODO

#### `Commands/UserDefinedCommandsTests.cs`

- [ ] **SetAndResetCacheTest** (Line 21)
  - **Reason**: Test needs investigation - unrelated to communication commands

#### `Database/FilteredObjectQueryTests.cs`

- [ ] **FilterByOwner_ReturnsOnlyOwnedObjects** (Line 57)
  - **Reason**: Owner filtering via graph traversal needs debugging

#### `Documentation/HelpfileTests.cs`

- [!] **CanIndex** (Line 22)
  - **Reason**: Moving to different help file system
- [!] **Indexable** (Line 37)
  - **Reason**: Moving to different help file system

#### `Functions/ConnectionFunctionUnitTests.cs`

- [ ] **Idle** (Line 13)
  - **Reason**: Is empty. Needs investigation.

#### `Functions/JsonFunctionUnitTests.cs`

- [ ] **JsonMap** (Line 57)
  - **Reason**: json_map currently explicitly does not use #lambda. It should evaluate functions later in its loop instead and use the existing method of calling attributes.

#### `Functions/ListFunctionUnitTests.cs`

- [ ] **Mix** (Line 319)
  - **Reason**: User-defined function execution issue - attribute not being called correctly
- [ ] **Itext** (Line 516)
  - **Reason**: Error handling outside iteration - should return error but doesn't

#### `Functions/MailFunctionUnitTests.cs`

- [ ] **Mailsubject_InvalidMessage_ReturnsError** (Line 321)
  - **Reason**: Marked for later investigation

#### `Functions/StringFunctionUnitTests.cs`

- [ ] **Decompose** (Line 258)
  - **Reason**: Decompose function not functioning as expected. Needs investigation.
- [ ] **DecomposeWeb** (Line 270)
  - **Reason**: Decompose function not functioning as expected. Needs investigation.

#### `Functions/TimeFunctionUnitTests.cs`

- [ ] **Time** (Line 20)
  - **Reason**: Weird error. Needs investigation: #-2 I DON'T KNOW WHICH ONE YOU MEAN

#### `Parser/RecursionAndInvocationLimitTests.cs`

- [ ] **RecursionLimit_IncludeCommand_TracksRecursion** (Line 337)
  - **Reason**: TODO: Commands send notifications via NotifyService, not return values. Need to redesign test to check NotifyService calls for recursion errors.
- [ ] **RecursionLimit_TriggerCommand_TracksRecursion** (Line 363)
  - **Reason**: TODO: Commands send notifications via NotifyService, not return values. Need to redesign test to check NotifyService calls for recursion errors.
- [ ] **RecursionLimit_CommandsTrackAttributeRecursion** (Line 383)
  - **Reason**: TODO: Commands send notifications via NotifyService, not return values. Need to redesign test to check NotifyService calls.

#### `Services/LocateServiceCompatibilityTests.cs`

- [~] **LocateMatch_NameMatching_ShouldMatchExactNamesForNonExits** (Line 47)
  - **Reason**: Skip for now
- [ ] **LocateMatch_TypePreference_ShouldRespectPlayerPreference** (Line 291)
  - **Reason**: Skip for now
- [ ] **LocateMatch_PartialMatching_ShouldFindObjectByPartialName** (Line 335)
  - **Reason**: Skip for now
- [ ] **LocateMatch_MatchObjectsInLookerLocation_ShouldFindObjectsInSameRoom** (Line 413)
  - **Reason**: Skip for now
- [ ] **LocateMatch_MultipleObjects_ShouldHandleAmbiguousMatches** (Line 453)
  - **Reason**: Skip for now

### Test Infrastructure Issues

**Total: 22 tests**

#### `Commands/AdminCommandTests.cs`

- [ ] **PcreateCommand** (Line 21)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **NewpasswordCommand** (Line 32)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **PasswordCommand** (Line 43)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **PoorCommand** (Line 87)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **ChownallCommand** (Line 109)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **ChzoneallCommand** (Line 120)
  - **Reason**: Test infrastructure issue - state pollution from other tests

#### `Commands/BuildingCommandTests.cs`

- [ ] **DoDigForCommandListCheck** (Line 52)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **DoDigForCommandListCheck2** (Line 89)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **LinkExit** (Line 176)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **CloneObject** (Line 200)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **LockObject** (Line 531)
  - **Reason**: Test infrastructure issue - state pollution from other tests
- [ ] **UnlockObject** (Line 546)
  - **Reason**: Test infrastructure issue - state pollution from other tests

#### `Commands/GeneralCommandTests.cs`

- [ ] **Halt_ClearsQueue** (Line 490)
  - **Reason**: Test infrastructure issue - NotifyService call count mismatch

#### `Commands/SocialCommandTests.cs`

- [ ] **SayCommand** (Line 20)
  - **Reason**: Issue with NotifyService mock, needs investigation
- [ ] **PoseCommand** (Line 32)
  - **Reason**: Issue with NotifyService mock, needs investigation
- [ ] **SemiposeCommand** (Line 43)
  - **Reason**: Issue with NotifyService mock, needs investigation
- [ ] **PageCommand** (Line 65)
  - **Reason**: Issue with NotifyService mock, needs investigation

#### `Commands/VerbCommandTests.cs`

- [ ] **VerbWithDefaultMessages** (Line 31)
  - **Reason**: Test environment issue with @verb notification capture
- [ ] **VerbWithAttributes** (Line 52)
  - **Reason**: Test environment issue with @verb notification capture
- [ ] **VerbWithStackArguments** (Line 75)
  - **Reason**: Test environment issue with @verb notification capture
- [ ] **VerbInsufficientArgs** (Line 97)
  - **Reason**: Test environment issue with notification capture

#### `Functions/InformationFunctionUnitTests.cs`

- [ ] **Hidden** (Line 110)
  - **Reason**: Test infrastructure issue - intermittent failure, returns '1' instead of '0'
