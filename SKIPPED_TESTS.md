# Skipped Tests Tracking

This document tracks all skipped tests in the SharpMUSH test suite.

## Status Legend

- **TODO**: Test has not been analyzed yet
- **SUCCESS**: Test passes when unskipped
- **FAIL**: Test fails when unskipped

## Summary

- **Total Skipped Tests**: 204
- **TODO**: 0
- **SUCCESS**: 16
- **FAIL**: 188

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
- **FAIL**: NameObject (AssertionException - returns "#-1 CAN'T SEE THAT HERE")
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

- **FAIL**: AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug (TODO - Needs Investigation)
- **FAIL**: AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug (TODO - Needs Investigation)

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

- **FAIL**: DestroyCommand (Not Yet Implemented)
- **FAIL**: NukeCommand (Not Yet Implemented)
- **FAIL**: UndestroyCommand (Not Yet Implemented)
- **FAIL**: UseCommand (Not Yet Implemented)

#### `Commands/QuotaCommandTests.cs`

- **FAIL**: SquotaCommand (Not Yet Implemented)

#### `Commands/SocialCommandTests.cs`

- **FAIL**: PageCommand (Not Yet Implemented)
- **FAIL**: PoseCommand (Not Yet Implemented)
- **FAIL**: SemiposeCommand (Not Yet Implemented)
- **FAIL**: WhisperCommand (Not Yet Implemented)

#### `Commands/SystemCommandTests.cs`

- **FAIL**: AtrchownCommand (Not Yet Implemented)
- **FAIL**: AtrlockCommand (Not Yet Implemented)
- **FAIL**: AttributeCommand (Not Yet Implemented)
- **FAIL**: CommandCommand (Not Yet Implemented)
- **FAIL**: FirstexitCommand (Not Yet Implemented)
- **FAIL**: FlagCommand (Not Yet Implemented)
- **FAIL**: FunctionCommand (Not Yet Implemented)
- **FAIL**: HideCommand (Not Yet Implemented)
- **FAIL**: HookCommand (Not Yet Implemented)
- **FAIL**: KickCommand (Not Yet Implemented)
- **FAIL**: PowerCommand (Not Yet Implemented)

#### `Commands/UserDefinedCommandsTests.cs`

- **FAIL**: SetAndResetCacheTest (Not Yet Implemented)

#### `Commands/UtilityCommandTests.cs`

- **FAIL**: EntrancesCommand (Not Yet Implemented)
- **FAIL**: FindCommand (Not Yet Implemented)
- **FAIL**: SearchCommand (Not Yet Implemented)
- **FAIL**: StatsCommand (Not Yet Implemented)
- **FAIL**: WhereisCommand (Not Yet Implemented)

#### `Commands/VerbCommandTests.cs`

- **FAIL**: VerbExecutesAwhat (Not Yet Implemented)
- **FAIL**: VerbInsufficientArgs (Not Yet Implemented)
- **FAIL**: VerbPermissionDenied (Not Yet Implemented)
- **FAIL**: VerbWithAttributes (Not Yet Implemented)
- **FAIL**: VerbWithDefaultMessages (Not Yet Implemented)
- **FAIL**: VerbWithStackArguments (Not Yet Implemented)

#### `Commands/WarningCommandTests.cs`

- **FAIL**: WCheckCommand_WithAll_RequiresWizard (Not Yet Implemented)
- **FAIL**: WCheckCommand_WithMe_ChecksOwnedObjects (Not Yet Implemented)

#### `Commands/WizardCommandTests.cs`

- **FAIL**: AllquotaCommand (Not Yet Implemented)
- **FAIL**: BootCommand (Not Yet Implemented)
- **FAIL**: DbckCommand (Not Yet Implemented)
- **FAIL**: DumpCommand (Not Yet Implemented)
- **FAIL**: HaltCommand (Not Yet Implemented)
- **FAIL**: Hide_AlreadyVisible_ShowsAppropriateMessage (Not Yet Implemented)
- **FAIL**: Hide_NoSwitch_TogglesHidden (Not Yet Implemented)
- **FAIL**: Hide_OffSwitch_UnsetsHidden (Not Yet Implemented)
- **FAIL**: PsCommand (Not Yet Implemented)
- **FAIL**: PsWithTarget (Not Yet Implemented)
- **FAIL**: QuotaCommand (Not Yet Implemented)
- **FAIL**: TriggerCommand (Not Yet Implemented)

### Database


#### `Database/FilteredObjectQueryTests.cs`

- **FAIL**: FilterByOwner_ReturnsOnlyOwnedObjects (Not Yet Implemented)

### Documentation


#### `Documentation/HelpfileTests.cs`

- **FAIL**: CanIndex (Not Yet Implemented)
- **FAIL**: Indexable (Not Yet Implemented)

### Functions


#### `Functions/ChannelFunctionUnitTests.cs`

- **FAIL**: Cstatus_WithNonMember_ReturnsOff (Not Yet Implemented)

#### `Functions/ConnectionFunctionUnitTests.cs`

- **FAIL**: Idle (Not Yet Implemented)

#### `Functions/InformationFunctionUnitTests.cs`

- **FAIL**: Hidden (Not Yet Implemented)

#### `Functions/MailFunctionUnitTests.cs`

- **FAIL**: Mail_InvalidMessage_ReturnsError (Not Yet Implemented)
- **FAIL**: Mailsubject_InvalidMessage_ReturnsError (Not Yet Implemented)

#### `Functions/MessageFunctionTests.cs`

- **FAIL**: MessageHashHashReplacement (Not Yet Implemented)
- **FAIL**: MessageNoSideFxDisabled (Not Yet Implemented)
- **FAIL**: MessageOemitSwitch (Not Yet Implemented)
- **FAIL**: MessageRemitSwitch (Not Yet Implemented)

#### `Functions/StringFunctionUnitTests.cs`

- **FAIL**: Decompose (Not Yet Implemented)
- **FAIL**: DecomposeWeb (Not Yet Implemented)

#### `Functions/TimeFunctionUnitTests.cs`

- **FAIL**: Time (Not Yet Implemented)

### Parser


#### `Parser/RecursionAndInvocationLimitTests.cs`

- **FAIL**: RecursionLimit_CommandsTrackAttributeRecursion (TODO - Needs Investigation)
- **FAIL**: RecursionLimit_IncludeCommand_TracksRecursion (TODO - Needs Investigation)
- **FAIL**: RecursionLimit_TriggerCommand_TracksRecursion (TODO - Needs Investigation)

### Services


#### `Services/EventServiceTests.cs`

- **FAIL**: TriggerEventWithHandler (TODO - Needs Investigation)
- **FAIL**: TriggerEventWithNoHandlerConfigured (TODO - Needs Investigation)
- **FAIL**: TriggerEventWithSystemEnactor (TODO - Needs Investigation)

#### `Services/LocateServiceCompatibilityTests.cs`

- **FAIL**: LocateMatch_MatchObjectsInLookerLocation_ShouldFindObjectsInSameRoom (TODO - Needs Investigation)
- **FAIL**: LocateMatch_MultipleObjects_ShouldHandleAmbiguousMatches (TODO - Needs Investigation)
- **FAIL**: LocateMatch_NameMatching_ShouldMatchExactNamesForNonExits (TODO - Needs Investigation)
- **FAIL**: LocateMatch_PartialMatching_ShouldFindObjectByPartialName (TODO - Needs Investigation)
- **FAIL**: LocateMatch_TypePreference_ShouldRespectPlayerPreference (TODO - Needs Investigation)

#### `Services/MoveServiceTests.cs`

- **FAIL**: DetectsDirectLoop (TODO - Needs Investigation)
- **FAIL**: DetectsIndirectLoop (TODO - Needs Investigation)
- **FAIL**: ExecuteMoveAsyncFailsOnLoop (TODO - Needs Investigation)
- **FAIL**: ExecuteMoveAsyncFailsOnPermission (TODO - Needs Investigation)
- **FAIL**: ExecuteMoveAsyncTriggersEnterHooks (TODO - Needs Investigation)
- **FAIL**: ExecuteMoveAsyncTriggersLeaveHooks (TODO - Needs Investigation)
- **FAIL**: ExecuteMoveAsyncTriggersTeleportHooks (TODO - Needs Investigation)
- **FAIL**: ExecuteMoveAsyncWithValidMove (TODO - Needs Investigation)
- **FAIL**: NoLoopIntoRoom (TODO - Needs Investigation)
- **FAIL**: NoLoopWithSimpleMove (TODO - Needs Investigation)

#### `Services/PennMUSHDatabaseConverterTests.cs`

- **FAIL**: ConversionResultIncludesStatistics (TODO - Needs Investigation)

#### `Services/WarningLockChecksTests.cs`

- **FAIL**: LockChecks_Integration_EmptyLock_Skipped (TODO - Needs Investigation)
- **FAIL**: LockChecks_Integration_GoingObjectReference_TriggersWarning (TODO - Needs Investigation)
- **FAIL**: LockChecks_Integration_InvalidLock_TriggersWarning (TODO - Needs Investigation)
- **FAIL**: LockChecks_Integration_MultipleLocks_ChecksAll (TODO - Needs Investigation)
- **FAIL**: LockChecks_Integration_ValidLock_NoWarnings (TODO - Needs Investigation)

#### `Services/WarningNoWarnTests.cs`

- **FAIL**: BackgroundService_DisabledWhenIntervalZero (TODO - Needs Investigation)
- **FAIL**: BackgroundService_RunsAtConfiguredInterval (TODO - Needs Investigation)
- **FAIL**: WarningService_SkipsGoingObjects (TODO - Needs Investigation)
- **FAIL**: WarningService_SkipsObjectsWithNoWarn (TODO - Needs Investigation)
- **FAIL**: WarningService_SkipsObjectsWithOwnerNoWarn (TODO - Needs Investigation)

#### `Services/WarningTopologyTests.cs`

- **FAIL**: CheckExitWarnings_MultipleReturnExits_DetectsWarning (TODO - Needs Investigation)
- **FAIL**: CheckExitWarnings_OnewayExit_DetectsWarning (TODO - Needs Investigation)
- **FAIL**: CheckExitWarnings_UnlinkedExit_DetectsWarning (TODO - Needs Investigation)
