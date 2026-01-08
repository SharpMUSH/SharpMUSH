# Skipped Tests Tracking

This document tracks all skipped tests in the SharpMUSH test suite.

## Status Legend

- **TODO**: Test has not been analyzed yet
- **SUCCESS**: Test passes when unskipped
- **FAIL**: Test fails when unskipped

## Summary

- **Total Skipped Tests**: 204
- **TODO**: 204
- **SUCCESS**: 0
- **FAIL**: 0

## Tests by Category


### Commands


#### `Commands/AdminCommandTests.cs`

- **TODO**: ChownallCommand
- **TODO**: ChzoneallCommand
- **TODO**: NewpasswordCommand
- **TODO**: PasswordCommand
- **TODO**: PcreateCommand
- **TODO**: PoorCommand
- **TODO**: PurgeCommand
- **TODO**: ReadcacheCommand
- **TODO**: RestartCommand
- **TODO**: ShutdownCommand

#### `Commands/AtListCommandTests.cs`

- **TODO**: List_Flags_Lowercase_DisplaysLowercaseFlagList

#### `Commands/AttributeCommandTests.cs`

- **TODO**: Test_AtrLock_LockAndUnlock
- **TODO**: Test_CopyAttribute_Basic
- **TODO**: Test_CopyAttribute_Direct
- **TODO**: Test_CopyAttribute_MultipleDestinations
- **TODO**: Test_MoveAttribute_Basic
- **TODO**: Test_WipeAttributes_AllAttributes

#### `Commands/BuildingCommandTests.cs`

- **TODO**: ChownObject
- **TODO**: CloneObject
- **TODO**: DoDigForCommandListCheck
- **TODO**: DoDigForCommandListCheck2
- **TODO**: LinkExit
- **TODO**: LockObject
- **TODO**: SetParent
- **TODO**: UnlinkExit
- **TODO**: UnlockObject

#### `Commands/ChannelCommandTests.cs`

- **TODO**: AddcomCommand
- **TODO**: ChannelCommand
- **TODO**: ChatCommand
- **TODO**: ClistCommand
- **TODO**: ComlistCommand
- **TODO**: ComtitleCommand
- **TODO**: DelcomCommand

#### `Commands/CommunicationCommandTests.cs`

- **TODO**: ComListEmpty
- **TODO**: ComTitleBasic
- **TODO**: LemitBasic
- **TODO**: NsemitBasic
- **TODO**: NslemitBasic
- **TODO**: NsoemitBasic
- **TODO**: NspemitBasic
- **TODO**: NsremitBasic
- **TODO**: NszemitBasic
- **TODO**: OemitBasic
- **TODO**: RemitBasic
- **TODO**: ZemitBasic

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
