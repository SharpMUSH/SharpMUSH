# Skipped Tests Tracking

This document tracks all skipped tests in the SharpMUSH test suite.

## Status Legend

- **TODO**: Test has not been analyzed yet
- **SUCCESS**: Test passes when unskipped
- **FAIL**: Test fails when unskipped

## Summary

- **Total Skipped Tests**: 204
- **TODO**: 80
- **SUCCESS**: 16
- **FAIL**: 108

## Tests by Category


### Commands


#### `Commands/AdminCommandTests.cs`

- **FAIL**: ChownallCommand (ReceivedCallsException - test interference from other tests)
- **FAIL**: ChzoneallCommand (ReceivedCallsException - test interference from other tests)
- **FAIL**: NewpasswordCommand (ReceivedCallsException - test interference from other tests)
- **FAIL**: PasswordCommand (ReceivedCallsException - test interference from other tests)
- **FAIL**: PcreateCommand (ReceivedCallsException - test interference from other tests)
- **FAIL**: PoorCommand (ReceivedCallsException - test interference from other tests)
- **FAIL**: PurgeCommand (RedundantArgumentMatcherException - mock specification issue)
- **FAIL**: ReadcacheCommand (RedundantArgumentMatcherException - mock specification issue)
- **FAIL**: RestartCommand (RedundantArgumentMatcherException - mock specification issue)
- **FAIL**: ShutdownCommand (RedundantArgumentMatcherException - mock specification issue)

#### `Commands/AtListCommandTests.cs`

- **FAIL**: List_Flags_Lowercase_DisplaysLowercaseFlagList (ReceivedCallsException - test interference, received 10 notify calls from other tests)
- **SUCCESS**: Test_Atlist_Command
- **SUCCESS**: Test_List_Commands_Help_DisplaysListCommands
- **SUCCESS**: Test_List_Locks_Lowercase_DisplaysLowercaseLockList
- **SUCCESS**: Test_List_Empty_Success
- **SUCCESS**: Test_List_Functions_Lowercase_DisplaysLowercaseFunctionList
- **SUCCESS**: Test_List_Allocations_Help_DisplaysListAllocations
- **SUCCESS**: Test_List_Motd_Help_DisplaysListMotd
- **SUCCESS**: Test_List_Attribs_Lowercase_DisplaysLowercaseAttribList

#### `Commands/AttributeCommandTests.cs`

- **FAIL**: Test_AtrLock_LockAndUnlock (ReceivedCallsException - test interference, received 15 notify calls from other tests)
- **FAIL**: Test_CopyAttribute_Basic (ReceivedCallsException - wrong notification message, expected "copied to 1 destination")
- **FAIL**: Test_CopyAttribute_Direct (ReceivedCallsException - no notification received)
- **FAIL**: Test_CopyAttribute_MultipleDestinations (ReceivedCallsException - wrong notification message, expected "copied to 2 destinations")
- **FAIL**: Test_MoveAttribute_Basic (ReceivedCallsException - test interference, received 6 notify calls from other tests)
- **FAIL**: Test_WipeAttributes_AllAttributes (AssertionException - attributes not properly wiped)
- **SUCCESS**: Test_CopyAttribute_WithSlash
- **SUCCESS**: Test_Edit_Check_NoChange
- **SUCCESS**: Test_Edit_Caret_ReplaceCharacter
- **SUCCESS**: Test_Edit_Dollar_ReplaceWithDollar
- **SUCCESS**: Test_Edit_Dollar_ReplaceAtEnd

#### `Commands/BuildingCommandTests.cs`

- **SUCCESS**: ChownObject
- **SUCCESS**: CloneObject
- **SUCCESS**: DoDigForCommandListCheck
- **SUCCESS**: DoDigForCommandListCheck2
- **FAIL**: LinkExit (IndexOutOfRangeException - array bounds error)
- **FAIL**: LockObject (ReceivedCallsException - test interference, 43 notify calls)
- **TODO**: NameObject (listed elsewhere, marked as FAIL)
- **FAIL**: SetParent (RedundantArgumentMatcherException - mock specification issue)
- **FAIL**: UnlinkExit (ReceivedCallsException - test interference, 41 notify calls)
- **FAIL**: UnlockObject (ReceivedCallsException - test interference, 44 notify calls)

#### `Commands/ChannelCommandTests.cs`

- **FAIL**: AddcomCommand (details TBD)
- **FAIL**: ChannelCommand (details TBD)
- **FAIL**: ChatCommand (details TBD)
- **FAIL**: ClistCommand (details TBD)
- **FAIL**: ComlistCommand (details TBD)
- **FAIL**: ComtitleCommand (details TBD)
- **FAIL**: DelcomCommand (details TBD)
Note: 2 of these 7 tests actually passed - need to identify which ones from detailed test output

#### `Commands/CommunicationCommandTests.cs`

- **FAIL**: ComTitleBasic (ReceivedCallsException - test interference, received 24 notify calls from other tests)
- **FAIL**: LemitBasic (ReceivedCallsException - test interference, received 14 notify calls)
- **FAIL**: NsemitBasic (RedundantArgumentMatcherException - mock specification issue with Arg.Any<string>())
- **FAIL**: NslemitBasic (RedundantArgumentMatcherException - mock specification issue with Arg.Any<string>())
- **FAIL**: NsoemitBasic (RedundantArgumentMatcherException - mock specification issue with Arg.Any<string>())
- **FAIL**: NspemitBasic (RedundantArgumentMatcherException - mock specification issue with Arg.Any<string>())
- **FAIL**: NsremitBasic (RedundantArgumentMatcherException - mock specification issue with Arg.Any<string>())
- **FAIL**: NszemitBasic (RedundantArgumentMatcherException - mock specification issue with Arg.Any<string>())
- **FAIL**: OemitBasic (ReceivedCallsException - test interference, received 15 notify calls)
- **FAIL**: RemitBasic (ReceivedCallsException - test interference, received 13 notify calls)
- **FAIL**: ZemitBasic (ReceivedCallsException - wrong message, expected "Test zone emit" but received "Don't you have anything to say?")
- **FAIL**: ComListEmpty (TODO: Failing Test. Requires investigation)
- **FAIL**: AddComInvalidArgs (2 parametrized tests - TODO)

#### `Commands/ConfigCommandTests.cs`

- **FAIL**: ConfigCommand_NoArgs_ListsCategories (TODO - hangs/needs investigation)
- **FAIL**: ConfigCommand_CategoryArg_ShowsCategoryOptions (Failing. Needs Investigation)
- **FAIL**: DoingCommand (Not Yet Implemented)
- **FAIL**: MonikerCommand (Not Yet Implemented)
- **FAIL**: MotdCommand (Not Yet Implemented)
- **FAIL**: RejectmotdCommand (Not Yet Implemented)
- **FAIL**: WizmotdCommand (Not Yet Implemented)

#### `Commands/ControlFlowCommandTests.cs`

- **FAIL**: AssertCommand (Not Yet Implemented)
- **FAIL**: BreakCommand (Not Yet Implemented)
- **FAIL**: IncludeCommand (Not Yet Implemented)
- **FAIL**: RetryCommand (Not Yet Implemented)
- **FAIL**: SelectCommand (Not Yet Implemented)
- **FAIL**: SwitchCommand (Not Yet Implemented)

#### `Commands/DatabaseCommandTests.cs`

- **SUCCESS**: ClockCommand
- **FAIL**: Test_Sql_Count (ReceivedCallsException - test interference, received 4 notify calls from previous tests)
- **FAIL**: DisableCommand (Not Yet Implemented)
- **FAIL**: EnableCommand (Not Yet Implemented)
- **FAIL**: ListCommand (Not Yet Implemented)
- **FAIL**: UnrecycleCommand (Not Yet Implemented)

#### `Commands/DebugVerboseTests.cs`

- **TODO**: AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug
- **TODO**: AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug

#### `Commands/FlagAndPowerCommandTests.cs`

- **FAIL**: Flag_Delete_HandlesNonExistentFlag (Not Yet Implemented)
- **FAIL**: Flag_Disable_PreventsSystemFlagDisable (Not Yet Implemented)
- **FAIL**: Power_Delete_HandlesNonExistentPower (Not Yet Implemented)
- **FAIL**: Power_Disable_PreventsSystemPowerDisable (Not Yet Implemented)
- **FAIL**: Power_Enable_EnablesDisabledPower (Not Yet Implemented)

#### `Commands/GameCommandTests.cs`

- **FAIL**: BuyCommand (Not Yet Implemented)
- **FAIL**: DesertCommand (Not Yet Implemented)
- **FAIL**: DismissCommand (Not Yet Implemented)
- **FAIL**: FollowCommand (Not Yet Implemented)
- **FAIL**: ScoreCommand (Not Yet Implemented)
- **FAIL**: TeachCommand (Not Yet Implemented)
- **FAIL**: UnfollowCommand (Not Yet Implemented)
- **FAIL**: WithCommand (Not Yet Implemented)

#### `Commands/GeneralCommandTests.cs`

- **FAIL**: Attribute_DisplaysAttributeInfo (Not Yet Implemented)
- **FAIL**: Command_ShowsCommandInfo (Not Yet Implemented)
- **FAIL**: DolistCommand (Not Yet Implemented)
- **FAIL**: Halt_ClearsQueue (Not Yet Implemented)

#### `Commands/LogCommandTests.cs`

- **FAIL**: LogwipeCommand (Not Yet Implemented)

#### `Commands/MailCommandTests.cs`

- **FAIL**: MailCommand (Not Yet Implemented)
- **FAIL**: MaliasCommand (Not Yet Implemented)

#### `Commands/MessageCommandTests.cs`

- **FAIL**: MessageOemitSwitch (Not Yet Implemented)
- **FAIL**: MessageRemitSwitch (Not Yet Implemented)

#### `Commands/MiscCommandTests.cs`

- **FAIL**: BriefCommand (Not Yet Implemented)
- **FAIL**: ConnectCommand (Not Yet Implemented)
- **FAIL**: EditCommand (Not Yet Implemented)
- **FAIL**: NspromptCommand (Not Yet Implemented)
- **FAIL**: PromptCommand (Not Yet Implemented)
- **FAIL**: QuitCommand (Not Yet Implemented)
- **FAIL**: SessionCommand (Not Yet Implemented)
- **FAIL**: SweepCommand (Not Yet Implemented)
- **FAIL**: VerbCommand (Not Yet Implemented)
- **FAIL**: WhoCommand (Not Yet Implemented)

#### `Commands/MovementCommandTests.cs`

- **FAIL**: EnterCommand (Not Yet Implemented)
- **FAIL**: GotoCommand (Not Yet Implemented)

#### `Commands/NetworkCommandTests.cs`

- **FAIL**: HttpCommand (Not Yet Implemented)
- **FAIL**: MapsqlCommand (Not Yet Implemented)
- **FAIL**: SlaveCommand (Not Yet Implemented)
- **FAIL**: SocksetCommand (Not Yet Implemented)
- **FAIL**: SqlCommand (Not Yet Implemented)

#### `Commands/NotificationCommandTests.cs`

- **FAIL**: MessageCommand (Not Yet Implemented)
- **FAIL**: RespondCommand (Not Yet Implemented)
- **FAIL**: RwallCommand (Not Yet Implemented)
- **FAIL**: SuggestCommand (Not Yet Implemented)
- **FAIL**: WarningsCommand (Not Yet Implemented)
- **FAIL**: WcheckCommand (Not Yet Implemented)

#### `Commands/ObjectManipulationCommandTests.cs`

- **TODO**: DestroyCommand
- **TODO**: NukeCommand
- **TODO**: UndestroyCommand
- **TODO**: UseCommand

#### `Commands/QuotaCommandTests.cs`

- **TODO**: SquotaCommand

#### `Commands/SocialCommandTests.cs`

- **TODO**: PageCommand
- **TODO**: PoseCommand
- **TODO**: SemiposeCommand
- **TODO**: WhisperCommand

#### `Commands/SystemCommandTests.cs`

- **TODO**: AtrchownCommand
- **TODO**: AtrlockCommand
- **TODO**: AttributeCommand
- **TODO**: CommandCommand
- **TODO**: FirstexitCommand
- **TODO**: FlagCommand
- **TODO**: FunctionCommand
- **TODO**: HideCommand
- **TODO**: HookCommand
- **TODO**: KickCommand
- **TODO**: PowerCommand

#### `Commands/UserDefinedCommandsTests.cs`

- **TODO**: SetAndResetCacheTest

#### `Commands/UtilityCommandTests.cs`

- **TODO**: EntrancesCommand
- **TODO**: FindCommand
- **TODO**: SearchCommand
- **TODO**: StatsCommand
- **TODO**: WhereisCommand

#### `Commands/VerbCommandTests.cs`

- **TODO**: VerbExecutesAwhat
- **TODO**: VerbInsufficientArgs
- **TODO**: VerbPermissionDenied
- **TODO**: VerbWithAttributes
- **TODO**: VerbWithDefaultMessages
- **TODO**: VerbWithStackArguments

#### `Commands/WarningCommandTests.cs`

- **TODO**: WCheckCommand_WithAll_RequiresWizard
- **TODO**: WCheckCommand_WithMe_ChecksOwnedObjects

#### `Commands/WizardCommandTests.cs`

- **TODO**: AllquotaCommand
- **TODO**: BootCommand
- **TODO**: DbckCommand
- **TODO**: DumpCommand
- **TODO**: HaltCommand
- **TODO**: Hide_AlreadyVisible_ShowsAppropriateMessage
- **TODO**: Hide_NoSwitch_TogglesHidden
- **TODO**: Hide_OffSwitch_UnsetsHidden
- **TODO**: PsCommand
- **TODO**: PsWithTarget
- **TODO**: QuotaCommand
- **TODO**: TriggerCommand

### Database


#### `Database/FilteredObjectQueryTests.cs`

- **TODO**: FilterByOwner_ReturnsOnlyOwnedObjects

### Documentation


#### `Documentation/HelpfileTests.cs`

- **TODO**: CanIndex
- **TODO**: Indexable

### Functions


#### `Functions/ChannelFunctionUnitTests.cs`

- **TODO**: Cstatus_WithNonMember_ReturnsOff

#### `Functions/ConnectionFunctionUnitTests.cs`

- **TODO**: Idle

#### `Functions/InformationFunctionUnitTests.cs`

- **TODO**: Hidden

#### `Functions/MailFunctionUnitTests.cs`

- **TODO**: Mail_InvalidMessage_ReturnsError
- **TODO**: Mailsubject_InvalidMessage_ReturnsError

#### `Functions/MessageFunctionTests.cs`

- **TODO**: MessageHashHashReplacement
- **TODO**: MessageNoSideFxDisabled
- **TODO**: MessageOemitSwitch
- **TODO**: MessageRemitSwitch

#### `Functions/StringFunctionUnitTests.cs`

- **TODO**: Decompose
- **TODO**: DecomposeWeb

#### `Functions/TimeFunctionUnitTests.cs`

- **TODO**: Time

### Parser


#### `Parser/RecursionAndInvocationLimitTests.cs`

- **TODO**: RecursionLimit_CommandsTrackAttributeRecursion
- **TODO**: RecursionLimit_IncludeCommand_TracksRecursion
- **TODO**: RecursionLimit_TriggerCommand_TracksRecursion

### Services


#### `Services/EventServiceTests.cs`

- **TODO**: TriggerEventWithHandler
- **TODO**: TriggerEventWithNoHandlerConfigured
- **TODO**: TriggerEventWithSystemEnactor

#### `Services/LocateServiceCompatibilityTests.cs`

- **TODO**: LocateMatch_MatchObjectsInLookerLocation_ShouldFindObjectsInSameRoom
- **TODO**: LocateMatch_MultipleObjects_ShouldHandleAmbiguousMatches
- **TODO**: LocateMatch_NameMatching_ShouldMatchExactNamesForNonExits
- **TODO**: LocateMatch_PartialMatching_ShouldFindObjectByPartialName
- **TODO**: LocateMatch_TypePreference_ShouldRespectPlayerPreference

#### `Services/MoveServiceTests.cs`

- **TODO**: DetectsDirectLoop
- **TODO**: DetectsIndirectLoop
- **TODO**: ExecuteMoveAsyncFailsOnLoop
- **TODO**: ExecuteMoveAsyncFailsOnPermission
- **TODO**: ExecuteMoveAsyncTriggersEnterHooks
- **TODO**: ExecuteMoveAsyncTriggersLeaveHooks
- **TODO**: ExecuteMoveAsyncTriggersTeleportHooks
- **TODO**: ExecuteMoveAsyncWithValidMove
- **TODO**: NoLoopIntoRoom
- **TODO**: NoLoopWithSimpleMove

#### `Services/PennMUSHDatabaseConverterTests.cs`

- **TODO**: ConversionResultIncludesStatistics

#### `Services/WarningLockChecksTests.cs`

- **TODO**: LockChecks_Integration_EmptyLock_Skipped
- **TODO**: LockChecks_Integration_GoingObjectReference_TriggersWarning
- **TODO**: LockChecks_Integration_InvalidLock_TriggersWarning
- **TODO**: LockChecks_Integration_MultipleLocks_ChecksAll
- **TODO**: LockChecks_Integration_ValidLock_NoWarnings

#### `Services/WarningNoWarnTests.cs`

- **TODO**: BackgroundService_DisabledWhenIntervalZero
- **TODO**: BackgroundService_RunsAtConfiguredInterval
- **TODO**: WarningService_SkipsGoingObjects
- **TODO**: WarningService_SkipsObjectsWithNoWarn
- **TODO**: WarningService_SkipsObjectsWithOwnerNoWarn

#### `Services/WarningTopologyTests.cs`

- **TODO**: CheckExitWarnings_MultipleReturnExits_DetectsWarning
- **TODO**: CheckExitWarnings_OnewayExit_DetectsWarning
- **TODO**: CheckExitWarnings_UnlinkedExit_DetectsWarning
