# Skipped Tests Tracking

This document tracks all skipped tests in the SharpMUSH test suite.

## Status Legend

- **TODO**: Test has not been analyzed yet
- **SUCCESS**: Test passes when unskipped
- **FAIL**: Test fails when unskipped

## Summary

- **Total Skipped Tests**: 204
- **TODO**: 151
- **SUCCESS**: 15
- **FAIL**: 38

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
- **TODO**: ComListEmpty (skipped with "TODO")
- **TODO**: AddComInvalidArgs (2 tests skipped with "TODO")

#### `Commands/ConfigCommandTests.cs`

- **TODO**: ConfigCommand_CategoryArg_ShowsCategoryOptions
- **TODO**: DoingCommand
- **TODO**: MonikerCommand
- **TODO**: MotdCommand
- **TODO**: RejectmotdCommand
- **TODO**: WizmotdCommand

#### `Commands/ControlFlowCommandTests.cs`

- **TODO**: AssertCommand
- **TODO**: BreakCommand
- **TODO**: IncludeCommand
- **TODO**: RetryCommand
- **TODO**: SelectCommand
- **TODO**: SwitchCommand

#### `Commands/DatabaseCommandTests.cs`

- **TODO**: DisableCommand
- **TODO**: EnableCommand
- **TODO**: ListCommand
- **TODO**: UnrecycleCommand

#### `Commands/DebugVerboseTests.cs`

- **TODO**: AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug
- **TODO**: AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug

#### `Commands/FlagAndPowerCommandTests.cs`

- **TODO**: Flag_Delete_HandlesNonExistentFlag
- **TODO**: Flag_Disable_PreventsSystemFlagDisable
- **TODO**: Power_Delete_HandlesNonExistentPower
- **TODO**: Power_Disable_PreventsSystemPowerDisable
- **TODO**: Power_Enable_EnablesDisabledPower

#### `Commands/GameCommandTests.cs`

- **TODO**: BuyCommand
- **TODO**: DesertCommand
- **TODO**: DismissCommand
- **TODO**: FollowCommand
- **TODO**: ScoreCommand
- **TODO**: TeachCommand
- **TODO**: UnfollowCommand
- **TODO**: WithCommand

#### `Commands/GeneralCommandTests.cs`

- **TODO**: Attribute_DisplaysAttributeInfo
- **TODO**: Command_ShowsCommandInfo
- **TODO**: DolistCommand
- **TODO**: Halt_ClearsQueue

#### `Commands/LogCommandTests.cs`

- **TODO**: LogwipeCommand

#### `Commands/MailCommandTests.cs`

- **TODO**: MailCommand
- **TODO**: MaliasCommand

#### `Commands/MessageCommandTests.cs`

- **TODO**: MessageOemitSwitch
- **TODO**: MessageRemitSwitch

#### `Commands/MiscCommandTests.cs`

- **TODO**: BriefCommand
- **TODO**: ConnectCommand
- **TODO**: EditCommand
- **TODO**: NspromptCommand
- **TODO**: PromptCommand
- **TODO**: QuitCommand
- **TODO**: SessionCommand
- **TODO**: SweepCommand
- **TODO**: VerbCommand
- **TODO**: WhoCommand

#### `Commands/MovementCommandTests.cs`

- **TODO**: EnterCommand
- **TODO**: GotoCommand

#### `Commands/NetworkCommandTests.cs`

- **TODO**: HttpCommand
- **TODO**: MapsqlCommand
- **TODO**: SlaveCommand
- **TODO**: SocksetCommand
- **TODO**: SqlCommand

#### `Commands/NotificationCommandTests.cs`

- **TODO**: MessageCommand
- **TODO**: RespondCommand
- **TODO**: RwallCommand
- **TODO**: SuggestCommand
- **TODO**: WarningsCommand
- **TODO**: WcheckCommand

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
