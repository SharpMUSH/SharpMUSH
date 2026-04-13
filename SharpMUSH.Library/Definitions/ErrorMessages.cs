using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Internationalization-ready error messaging system.
/// Consolidates all error constants from Errors class plus new ones.
/// Returns: Technical MUSH format (e.g., "#-1 PERMISSION DENIED") - never translated
/// Notifications: User-friendly messages (e.g., "You don't have permission to do that.") - translatable
/// </summary>
public static class ErrorMessages
{
	/// <summary>
	/// Technical error messages for error returns (parsing/API).
	/// Format: "#-1 ERROR NAME" or "#-2 ERROR NAME" for ambiguity.
	/// These are used in CallState returns and should NOT be translated.
	/// Merged from Errors class for full consistency.
	/// </summary>
	public static class Returns
	{
		// Object and matching errors
		public const string BadObjectName = "#-1 BAD OBJECT NAME";
		public const string NoMatch = "#-1 NO MATCH";
		public const string NoSuchObject = "#-1 NO SUCH OBJECT";
		public const string AmbiguousMatch = "#-2 I DON'T KNOW WHICH ONE YOU MEAN";
		public const string NotVisible = "#-1 NO SUCH OBJECT VISIBLE";
		public const string CantSeeThat = "#-1 CAN'T SEE THAT HERE";
		public const string InvalidDbref = "#-1 INVALID DBREF";

		// Object type errors
		public const string NotARoom = "#-1 NOT A ROOM";
		public const string NotAnExit = "#-1 NOT AN EXIT";
		public const string NotAThing = "#-1 NOT A THING";
		public const string NotAPlayer = "#-1 NOT A PLAYER";
		public const string InvalidPlayer = "#-1 INVALID PLAYER";
		public const string InvalidRoom = "#-1 INVALID ROOM";
		public const string InvalidDestination = "#-1 INVALID DESTINATION";
		public const string InvalidObjectType = "#-1 INVALID OBJECT TYPE";

		// Permission errors
		public const string PermissionDenied = "#-1 PERMISSION DENIED";
		public const string AttrPermissions = "#-1 NO PERMISSION TO GET ATTRIBUTE";
		public const string AttrEvalPermissions = "#-1 NO PERMISSION TO EVALUATE ATTRIBUTE";
		public const string AttrSetPermissions = "#-1 NO PERMISSION TO SET ATTRIBUTE";
		public const string AttrWipPermissions = "#-1 NO PERMISSION TO WIPE ATTRIBUTE";
		public const string CannotTeleport = "#-1 NO PERMISSION TO TELEPORT OBJECT";

		// Argument and validation errors
		public const string InvalidArgument = "#-1 INVALID ARGUMENT";
		public const string Integer = "#-1 ARGUMENT MUST BE INTEGER";
		public const string PositiveInteger = "#-1 ARGUMENT MUST BE POSITIVE INTEGER";
		public const string Integers = "#-1 ARGUMENTS MUST BE INTEGERS";
		public const string UInteger = "#-1 ARGUMENT MUST BE POSITIVE INTEGER";
		public const string UIntegers = "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS";
		public const string Number = "#-1 ARGUMENT MUST BE NUMBER";
		public const string Numbers = "#-1 ARGUMENTS MUST BE NUMBERS";
		public const string DivideByZero = "#-1 DIVIDE BY ZERO";
		public const string InvalidPassword = "#-1 INVALID PASSWORD";
		public const string InvalidFlag = "#-1 INVALID FLAG";
		public const string ObjectAttributeString = "#-1 INVALID OBJECT/ATTRIBUTE VALUE";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BadArgumentFormat = "#-1 BAD ARGUMENT FORMAT TO {0}";
		public const string ArgRange = "#-1 ARGUMENT OUT OF RANGE";
		public const string TimeInteger = "#-1 TIME INTEGER OUT OF RANGE";

		// Attribute errors
		public const string NoSuchAttribute = "#-1 NO SUCH ATTRIBUTE";

		// Function and feature errors
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NoSuchFunction = "#-1 COULD NOT FIND FUNCTION: {0}";
		public const string NoSuchPower = "#-1 NO SUCH POWER";
		public const string NoSuchFlag = "#-1 NO SUCH FLAG";
		public const string NoSuchTimezone = "#-1 NO SUCH TIMEZONE";
		public const string FunctionDisabled = "#-1 FUNCTION DISABLED";
		public const string NoSideFx = "#-1 SIDE EFFECTS DISABLED FOR THIS FUNCTION";

		// Limits and recursion
		public const string Invoke = "#-1 FUNCTION INVOCATION LIMIT EXCEEDED";
		public const string Recursion = "#-1 FUNCTION RECURSION LIMIT EXCEEDED";
		public const string Call = "#-1 CALL LIMIT EXCEEDED";
		public const string RegisterRange = "#-1 REGISTER OUT OF RANGE";
		public const string BadRegName = "#-1 REGISTER NAME INVALID";
		public const string TooManyRegs = "#-1 TOO MANY REGISTERS";
		public const string TooManySwitches = "#-1 TOO MANY SWITCHES, OR A BAD COMBINATION OF SWITCHES";
		public const string OutOfRange = "#-1 OUT OF RANGE";

		// Configuration and database errors
		public const string NoSuchConfigOption = "#-1 NO SUCH CONFIG OPTION";
		public const string InvalidZone = "#-1 INVALID ZONE";
		public const string SeparatorMustBeOneChar = "#-1 SEPARATOR MUST BE ONE CHARACTER";
		public const string MissingArguments = "#-1 MISSING ARGUMENTS";
		public const string NoSuchRecord = "#-1 NO SUCH RECORD";

		// SQL/Database errors (for future SQL support)
		public const string SqlNoConnection = "#-1 SQL ERROR: NO DATABASE CONNECTED";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SqlError = "#-1 SQL ERROR: {0}";
		public const string SqliteError = "#-1 SQLITE ERROR";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SqliteErrorDetail = "#-1 SQLITE ERROR: {0}";

		// Channel errors
		public const string AmbiguousChannelName = "#-2 AMBIGUOUS CHANNEL NAME";

		// Function argument errors
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TooFewArguments = "#-1 FUNCTION ({0}) EXPECTS AT LEAST {1} ARGUMENTS BUT GOT {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TooManyArguments = "#-1 FUNCTION ({0}) EXPECTS AT MOST {1} ARGUMENTS BUT GOT {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GotEvenArgs = "#-1 FUNCTION ({0}) EXPECTS AN ODD NUMBER OF ARGUMENTS";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GotUnEvenArgs = "#-1 FUNCTION ({0}) EXPECTS AN EVEN NUMBER OF ARGUMENTS";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WrongArgumentsRange = "#-1 FUNCTION ({0}) EXPECTS AT LEAST {1} ARGUMENTS AND AT MOST {2} BUT GOT {3}";

		// State and operation errors
		public const string NothingToEvaluate = "#-1 NOTHING TO EVALUATE";
		public const string NothingToDo = "#-1 NOTHING TO DO";
		public const string ExitsCannotContainThings = "#-1 EXITS CANNOT CONTAIN THINGS";
		public const string ParentLoop = "#-1 PARENT LOOP DETECTED";
		public const string NotSupported = "#-1 BEHAVIOR NOT SUPPORTED BY SHARPMUSH";
		public const string SafeObject = "#-1 OBJECT IS SAFE";
		public const string NotGoing = "#-1 OBJECT NOT MARKED FOR DESTRUCTION";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InternalErrorFormat = "#-1 INTERNAL SHARPMUSH ERROR:\n{0}";
	}

	/// <summary>
	/// User-friendly notification messages for player notifications.
	/// These are more natural English and SHOULD be translated for i18n.
	/// Found in actual Notify calls across the codebase.
	/// </summary>
	public static class Notifications
	{
		// Object and matching notifications
		public const string BadObjectName = "I don't understand that object name.";
		public const string InvalidNameThing = "Invalid name for a thing.";
		public const string NoMatch = "I don't see that here.";
		public const string CantSeeThat = "I can't see that here.";
		public const string NoSuchObject = "I can't find that.";
		public const string CouldNotFind = "Could not find that.";
		public const string CouldNotFindPlayer = "Could not find that player.";
		public const string CantFindThatPlayer = "I can't find that player";
		public const string AmbiguousMatch = "I don't know which one you mean.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontKnowWhichYouMean = "I don't know which {0} you mean!";
		public const string DontSeeWhatYouWantToLock = "I don't see what you want to lock!";
		public const string DontKnowWhichOneToLock = "I don't know which one you want to lock!";
		public const string DontSeeThatHere = "I don't see that here.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontSeeThatHereFormat = "I don't see {0} here.";
		public const string DontKnowWhoYouMean = "I don't know who you mean!";

		// Object type notifications
		public const string NotARoom = "That's not a room.";
		public const string NotAnExit = "That's not an exit.";
		public const string NotAThing = "That's not a thing.";
		public const string NotAPlayer = "That's not a player.";
		public const string MustBePlayer = "New owner must be a player.";
		public const string InvalidDestinationExit = "Invalid destination for exit.";
		public const string HomeMustBeRoom = "Home must be a room.";
		public const string DropToMustBeRoom = "Drop-to must be a room.";
		public const string InvalidObjectTypeForLinking = "Invalid object type for linking.";
		public const string InvalidObjectTypeGeneric = "Invalid object type.";
		public const string CannotClonePlayers = "You cannot clone players.";
		public const string CannotCloneThisObjectType = "Cannot clone this object type.";
		public const string NotMarkedForDestruction = "That object is not marked for destruction.";

		// Permission notifications
		public const string PermissionDenied = "Permission denied.";
		public const string NoPermission = "You don't have permission to do that.";
		public const string YouDoNotControlThatObject = "You do not control that object.";

		// General
		public const string EmptyLine = "";
		public const string CantLinkToThat = "You can't link to that.";
		public const string DontPassLinkLock = "You don't pass the link lock.";
		public const string FailedToTransferOwnership = "Failed to transfer ownership.";
		public const string PermissionDeniedCannotZoneTo = "Permission denied: You cannot zone to that object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PermissionDeniedSetAttribute = "Permission denied to set attribute on {0}.";
		public const string LackSpoofingPermissions = "Permission denied: You lack spoofing permissions.";
		public const string AttributeCannotBeChanged = "That attribute cannot be changed by you.";
		public const string AttributePermissionsCannotBeChanged = "That attribute's permissions cannot be changed.";
		public const string NoPermissionToChown = "You don't have the permission to chown that.";
		public const string CantRemakeWorld = "You can't remake the world in your image.";
		public const string CannotDoWhileGagged = "You cannot do that while gagged.";
		public const string CantTeleportToNothing = "You can't teleport to nothing!";
		public const string HavenFlagSet = "You are set HAVEN and cannot receive pages.";

		// Argument and validation notifications
		public const string InvalidArgument = "Invalid argument.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InvalidArguments = "Invalid arguments to {0}.";
		public const string InvalidDbref = "That's not a valid object reference.";
		public const string DontUnderstandThosePermissions = "I don't understand those permissions.";
		public const string DontUnderstandThatKey = "I don't understand that key.";
		public const string DontUnderstandListOfTypes = "I don't understand the list of types.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontUnderstandSwitch = "I don't understand switch '{0}'.";
		public const string DontUnderstandWhatYouWantToList = "I don't understand what you want to @list.";
		public const string DontUnderstandWhatYouWantToDo = "I don't understand what you want to do.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontKnowThat = "I don't know that {0}.";
		public const string DontKnowThatAttribute = "I don't know that attribute.";

		// Name and alias notifications
		public const string PlayerNameInUse = "That player name is already in use.";
		public const string PlayerAliasInUse = "That player alias is already in use.";

		// Function notifications
		public const string RecursionLimit = "That caused too much recursion.";
		public const string FunctionDisabled = "That function is disabled.";

		// Operation result notifications
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string Created = "Created {0} ({1}).";
		public const string CreatedObject = "Created: Object {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WipedAttributes = "Wiped attributes matching {0}.";
		public const string CouldNotFindNewOwner = "Could not find new owner.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CouldNotFindDestination = "Could not find destination: {0}";

		// Channel notifications
		public const string DontRecognizeThatChannel = "CHAT: I don't recognize that channel.";
		public const string DontKnowWhichChannel = "CHAT: I don't know which channel you mean.";

		// Mail and communication notifications
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontThinkWantsToHearFrom = "I don't think #{0} wants to hear from {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontThinkWantsMail = "I don't think #{0} wants {1}'s mail.";

		// Economic notifications
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NotEnoughMoneyToLink = "You don't have enough {0} to link.";
		public const string CantBuyThingsByTakingMoney = "You can't buy things by taking money.";

		// Administrative notifications
		public const string DontLookLikeGod = "You don't look like God.";
		public const string NotAnAdmin = "You don't look like an admin to me.";
		public const string CantAliasCommandToThat = "I can't alias a command to that!";
		public const string CantMakeMultipleRequests = "You can't make multiple requests at the same time!";

		// HTTP/Network notifications
		public const string CannotSetContentLengthHeader = "You cannot set Content-Length header.";

		// --- Destruction edge-case notifications (PennMUSH src/destroy.c) ---
		public const string GuestCantDestroy = "I'm sorry, Dave, I'm afraid I can't do that.";
		public const string AlreadyDestroyed = "Destroying that again is hardly necessary.";
		public const string DestroyGodBlasphemous = "Destroying God would be blasphemous.";
		public const string TooSpecialToDestroy = "That is too special to be destroyed.";

		// --- Movement default notifications (PennMUSH src/move.c) ---
		public const string DefaultOLeave = "has left.";
		public const string DefaultOEnter = "has arrived.";
		public const string HomeNoPlaceLikeHome = "There's no place like home...";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HomeGoesHome = "{0} goes home.";
		public const string AmbiguousExitDirection = "I don't know which way you mean!";

		// --- Speech lock enforcement (PennMUSH src/speech.c) ---
		public const string MayNotSpeakHere = "You may not speak here!";

		// --- @force guardrails (PennMUSH src/wiz.c) ---
		public const string CantForceGod = "You can't force God!";

		// --- Admin / Wizard guardrails (PennMUSH src/wiz.c, src/flags.c) ---
		public const string CantBootOtherPeople = "You can't boot other people!";
		public const string CantTeleportRooms = "You can't teleport rooms.";
		public const string TeleportsNotAllowed = "Teleports are not allowed in this room.";
		public const string NoZoneTeleport = "You may not teleport out of the zone from this room.";
		public const string InTheVoid = "You're in the Void. This is not a good thing.";
		public const string VoidSendingHome = "You're in the void - sending you home.";
		public const string TooManyContainers = "You're in too many containers.";
		public const string CantGrantPowersUnregistered = "You can't grant powers to unregistered players.";
		public const string CantMakeAdminGuests = "You can't make admin into guests.";
		public const string WhoDoYouThinkYouAre = "Who do you think you are, GOD?";
		public const string NoPowerOverBodyAndMind = "You do not have the power over body and mind!";

		// --- GAME: broadcast notifications (PennMUSH src/bsd.c, src/game.c, src/conf.c) ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GameShutdownBy = "GAME: Shutdown by {0}";
		public const string GameShutdownExternal = "GAME: Shutdown by external signal";
		public const string GameSavingDatabase = "GAME: Saving database. Game may freeze for a few moments.";
		public const string GameSaveComplete = "GAME: Save complete.";
		public const string GameSaveIn1Minute = "GAME: Database save in 1 minute.";
		public const string GameSaveIn5Minutes = "GAME: Database save in 5 minutes.";
		public const string GameHasConnected = "has connected.";
		public const string GameHasReconnected = "has reconnected.";
		public const string GameHasDisconnected = "has disconnected.";
		public const string GameHasPartiallyDisconnected = "has partially disconnected.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GameRebootBy = "GAME: Reboot w/o disconnect by {0}, please wait.";
		public const string GameRebootFinished = "GAME: Reboot finished.";
		public const string GameRebootFailed = "GAME: Reboot failed.";
		public const string GameDbSaveFailed = "GAME: ERROR! Database save failed!";
		public const string GameDbConsistencyCheck = "GAME: Performing database consistency check.";
		public const string GameDbConsistencyDone = "GAME: Database consistency check complete.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GameSuspectCreated = "GAME: Suspect {0} created.";

		// --- Reboot / shutdown broadcast ---
		public const string GameRebootNoDisconnect = "GAME: Reboot w/o disconnect from game account, please wait.";

		// --- Pueblo protocol (PennMUSH hdrs/conf.h) ---
		public const string PuebloHello = "This world is Pueblo 1.10 Enhanced.\r\n";

		// --- OUTPUTPREFIX / OUTPUTSUFFIX (PennMUSH hdrs/conf.h) ---
		public const string OutputPrefixSet = "OUTPUTPREFIX set.";
		public const string OutputSuffixSet = "OUTPUTSUFFIX set.";
		public const string OutputPrefixCleared = "OUTPUTPREFIX cleared.";
		public const string OutputSuffixCleared = "OUTPUTSUFFIX cleared.";

		// --- Movement messages aligned with PennMUSH src/move.c ---
		public const string ExitDestinationInvalid = "Exit destination is invalid.";
		public const string CantSeemToDropThingsHere = "You can't seem to drop things here.";
		public const string CantEmptyThatFromHere = "You can't empty that from here.";
		public const string DontHaveThat = "You don't have that!";

		// --- Destruction SAFE messages aligned with PennMUSH src/destroy.c ---
		/// <summary>PennMUSH: when object is SAFE and REALLY_SAFE is true (strict mode).</summary>
		public const string SafeObjectMustUnset = "That object is set SAFE. You must set it !SAFE before destroying it.";
		/// <summary>PennMUSH: when object is marked SAFE (standard mode).</summary>
		public const string SafeObjectUseNuke = "That object is marked SAFE. Use @nuke to destroy it.";

		// --- Zone messages aligned with PennMUSH src/set.c ---
		public const string ZoneChanged = "Zone changed.";
		public const string CantMakeCircularZones = "You can't make circular zones!";

		// --- Channel messages aligned with PennMUSH src/extchat.c (CHAT: prefix) ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNotOnChannel = "CHAT: You are not on channel <{0}>.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatChannelDescSet = "CHAT: Channel <{0}> description set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatChannelDescCleared = "CHAT: Channel <{0}> description cleared.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatResizingBuffer = "CHAT: Resizing buffer of channel <{0}>";
		public const string ChatGuestsCantModify = "CHAT: Guests may not modify channels.";

		// --- Lock/Unlock messages aligned with PennMUSH src/lock.c ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectLocked = "{0}(#{1}) - {2} locked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectUnlocked = "{0}(#{1}) - {2} unlocked.";

		// --- Link/Unlink messages aligned with PennMUSH src/create.c ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LinkedExitToRoom = "Linked exit #{0} to #{1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string UnlinkedExit = "Unlinked exit #{0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string OpenedExit = "Opened exit {0}";

		// --- Flag/Power messages aligned with PennMUSH src/flags.c ---
		// PennMUSH format: "AName(thing) - FLAGNAME set." / "AName(thing) - FLAGNAME reset."
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagSet = "{0} - {1} set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagAlreadySet = "{0} - {1} (already) set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagReset = "{0} - {1} reset.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagAlreadyReset = "{0} - {1} (already) reset.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontRecognizeFlag = "{0} - I don't recognize that flag.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontRecognizePower = "{0} - I don't recognize that power.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerSet = "{0} - {1} set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerAlreadySet = "{0} - {1} (already) set.";

		// --- Attribute set messages aligned with PennMUSH src/set.c ---
		// PennMUSH format: "ObjectName/ATTRNAME - Set."
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeSet = "{0}/{1} - Set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCleared = "{0}/{1} - Cleared.";

		// --- Connection lifecycle ---
		public const string Connected = "Connected!";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WelcomeBackFormat = "Welcome back, {0}!";

		// --- Name / Password ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotNameObjectFormat = "You cannot name that object {0}.";
		public const string InvalidPasswordText = "That password is not a valid password.";
		public const string InvalidPasswordForCommand = "Invalid password.";
		public const string OnlyPlayersHavePasswords = "Only players have passwords.";

		// --- Parent / Zone ---
		public const string ParentLoopCannotAdd = "Cannot add parent to loop.";
		public const string ParentSet = "Parent set.";
		public const string ZoneCycleCannotAdd = "Cannot add zone: would create a cycle.";
		public const string ZoneSet = "Zone set.";
		public const string ZoneCleared = "Zone cleared.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ClearedPowersFromFormat = "Cleared {0} power(s) from {1}.";

		// --- Attribute operations ---
		public const string NeedObjectAttributePair = "You need to give an object/attribute pair.";
		public const string AttributeIsLocked = "That attribute is locked.";
		public const string AttributeIsUnlocked = "That attribute is unlocked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InvalidArgumentsToCommandFormat = "Invalid arguments to {0}.";
		public const string InvalidSourceFormat = "Invalid source format. Use: object/attribute";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeNotFoundOnSourceFormat = "Attribute {0} not found on source object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InvalidDestinationFormat = "Invalid destination format: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToCopyAttributeToFormat = "Failed to copy attribute to {0}: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCopiedToDestinationsFormat = "Attribute copied to {0} {1}.";
		public const string FailedToCopyAttributeAny = "Failed to copy attribute to any destinations.";
		public const string AttributeLocked = "Attribute locked.";
		public const string AttributeUnlocked = "Attribute unlocked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeMovedFailedRemoveFormat = "Attribute moved to {0} {1} but failed to remove source: {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeMovedToFormat = "Attribute moved to {0} {1}.";
		public const string FailedToMoveAttributeAny = "Failed to move attribute to any destinations.";
		public const string AttributeNotFound = "No such attribute.";
		public const string CanOnlyChownToYourself = "You can only chown an attribute to yourself.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToChangeOwnershipFormat = "Failed to change ownership: {0}";
		public const string AttributeOwnerChanged = "Attribute owner changed.";
		public const string WipeWhat = "Wipe what?";
		public const string ObjectIsProtectedSafe = "That object is protected (SAFE).";
		public const string AttributesWiped = "Attributes wiped.";

		// --- Building / Creating ---
		public const string Destroyed = "Destroyed.";
		public const string LinkedToHome = "Linked to home.";
		public const string LinkedToVariable = "Linked to variable.";
		public const string HomeSet = "Home set.";
		public const string DropToSet = "Drop-to set.";
		public const string DropToRemoved = "Drop-to removed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SparedFromDestructionFormat = "Spared from destruction: {0}";
		public const string SourceMustBeARoom = "Source must be a room.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LinkedToNameFormat = "Linked to {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ClonedNewObjectFormat = "Cloned. New object: #{0}.";
		public const string MonikerCleared = "Moniker cleared.";
		public const string MonikerSet = "Moniker set.";
		public const string DigWhat = "Dig what?";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RoomCreatedWithNumberFormat = "{0} created with room number {1}.";
		public const string TryingToLink = "Trying to link...";

		// --- Navigation ---
		public const string CantGoThatWay = "You can't go that way.";
		public const string ExitNoValidLocation = "That exit doesn't go to a valid location.";
		public const string CantGoThatWayContainmentLoop = "You can't go that way - it would create a containment loop.";
		public const string YouHaveBeenTeleported = "You have been teleported.";

		// --- General game operations ---
		public const string DontYouHaveAnythingToSay = "Don't you have anything to say?";
		public const string HuhTypeHelp = "Huh?  (Type \"help\" for help.)";
		public const string AllObjectsHalted = "All objects halted.";
		public const string Halted = "Halted.";
		public const string Notified = "Notified.";
		public const string YouDoNotHavePermissionToSpoofEmits = "You do not have permission to spoof emits.";
		public const string NoSuchCommandAtLogin = "No such command available at login.";
		public const string InvalidRoomSpecified = "Invalid room specified.";
		public const string YouMustProvideMatchString = "You must provide a string to match when using /match.";
		public const string YouMustSpecifyObjectToDecompile = "You must specify an object to decompile.";
		public const string AllObjectsRestarted = "All objects restarted.";
		public const string YouMustSpecifyObjectToRestart = "You must specify an object to restart.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AmbiguousChannelNameFormat = "Ambiguous channel name '{0}'. Please be more specific.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AmbiguousChannelNameMatchesFormat = "Ambiguous channel name '{0}'. Matches: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string UsageAtCommandFormat = "Usage: @{0} <object>=<value>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ErrorDetailFormat = "Error: {0}";

		// --- Channel management (addcom/delcom/comtitle) ---
		public const string ChatAlreadyExists = "CHAT: Channel already exists.";
		public const string ChatInvalidChannelNameShort = "CHAT: Invalid channel name.";
		public const string ChatChannelCreated = "Channel has been created.";
		public const string ChatYesOrNoOnly = "CHAT: Yes or No are the only valid options.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNotMemberFormat = "CHAT: You are not a member of {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatCombinedChannelsOnFormat = "CHAT: Combined channels turned on for {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatCombinedChannelsOffFormat = "CHAT: Combined channels turned off for {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatAlreadyInGagStateFormat = "CHAT: You are already in that gag state on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatGaggedOnFormat = "CHAT: You have been gagged on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatUngaggedOnFormat = "CHAT: You have been ungagged on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatAlreadyInHideStateFormat = "CHAT: You are already in that hide state on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatHiddenOnChannelFormat = "CHAT: You have been hidden on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatUnhiddenOnChannelFormat = "CHAT: You have been unhidden on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatPlayerRemovedFromChannelFormat = "CHAT: {0} has been removed from {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatPlayerAddedToChannelFormat = "CHAT: {0} has been added to {1}.";
		public const string ChatChannelRenamed = "CHAT: Renamed channel.";
		public const string UsageAddcom = "Usage: addcom <alias>=<channel>";
		public const string AliasNameCannotBeEmpty = "Alias name cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ErrorSettingAliasFormat = "Error setting alias: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AliasAddedForChannelFormat = "Alias '{0}' added for channel {1}.";
		public const string UsageDelcom = "Usage: delcom <alias>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AliasNotFoundFormat = "Alias '{0}' not found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ErrorReadingAliasFormat = "Error reading alias: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ErrorDeletingAliasFormat = "Error deleting alias: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AliasDeletedFormat = "Alias '{0}' deleted.";
		public const string UsageComtitle = "Usage: comtitle <alias>=<title>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TitleSetForAliasChannelFormat = "Title set to '{0}' for alias '{1}' (channel {2}).";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ErrorReadingAliasesFormat = "Error reading aliases: {0}";
		public const string YouHaveNoChannelAliases = "You have no channel aliases.";

		// --- News / Help system ---
		public const string NewsSystemNotInitialized = "News system not initialized.";
		public const string NewsNoTopicAvailable = "No news available. Type 'news <topic>' for news on a specific topic.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NewsNoEntriesFoundContaining = "No news entries found containing '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NewsEntriesContaining = "News entries containing '{0}':";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NewsNoNewsForTopic = "No news available for '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NewsTopicsMatchingFormat = "News topics matching '{0}':";
		public const string NewsTryPattern = "Try 'news <pattern>' with wildcards (*) or 'news/search <text>' to search news content.";
		public const string AdminCommandOnly = "Permission denied. This command is for administrators only.";
		public const string AhelpSystemNotInitialized = "Admin help system not initialized.";
		public const string AhelpNoHelpAvailable = "No admin help available. Type 'ahelp <topic>' for help on a specific topic.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AhelpNoEntriesFoundContaining = "No admin help entries found containing '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AhelpEntriesContaining = "Admin help entries containing '{0}':";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AhelpNoHelpForTopic = "No admin help available for '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AhelpTopicsMatchingFormat = "Admin help topics matching '{0}':";
		public const string AhelpTryPattern = "Try 'ahelp <pattern>' with wildcards (*) or 'ahelp/search <text>' to search admin help.";

		// --- Wizard admin — flag management ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AllObjectsHaltedWithCountFormat = "All objects halted. {0} objects processed.";
		public const string FlagAddRequiresNameAndSymbol = "@FLAG/ADD requires flag name and symbol.";
		public const string FlagNameAndSymbolCannotBeEmpty = "Flag name and symbol cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagAlreadyExistsFormat = "Flag '{0}' already exists.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagCreatedWithSymbolFormat = "Flag '{0}' created with symbol '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToCreateFlagFormat = "Failed to create flag '{0}'.";
		public const string FlagDeleteRequiresName = "@FLAG/DELETE requires a flag name.";
		public const string FlagNameCannotBeEmpty = "Flag name cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagNotFoundFormat = "Flag '{0}' not found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotDeleteSystemFlagFormat = "Cannot delete system flag '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagDeletedFormat = "Flag '{0}' deleted.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToDeleteFlagFormat = "Failed to delete flag '{0}'.";
		public const string FlagLetterRequiresNameAndSymbol = "@FLAG/LETTER requires flag name and new symbol.";
		public const string FlagNameAndSymbolEmptyError = "Flag name and symbol cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagSymbolChangedFormat = "Flag '{0}' symbol changed to '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToUpdateFlagFormat = "Failed to update flag '{0}'.";
		public const string FlagTypeRequiresNameAndTypes = "@FLAG/TYPE requires flag name and type restrictions.";
		public const string FlagNameAndTypesCannotBeEmpty = "Flag name and types cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagTypeUpdatedFormat = "Flag '{0}' type restrictions updated to: {1}.";
		public const string FlagAliasRequiresNameAndAliases = "@FLAG/ALIAS requires flag name and aliases.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagAliasesSetFormat = "Flag '{0}' aliases set to: {1}.";
		public const string FlagRestrictRequiresNameAndPermissions = "@FLAG/RESTRICT requires flag name and permissions.";
		public const string FlagNameAndPermissionsCannotBeEmpty = "Flag name and permissions cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagPermissionsUpdatedFormat = "Flag '{0}' permissions updated to: {1}.";
		public const string FlagDecompileRequiresName = "@FLAG/DECOMPILE requires a flag name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagDisabledFormat = "Flag '{0}' disabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagEnabledFormat = "Flag '{0}' enabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToDisableFlagFormat = "Failed to disable flag '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToEnableFlagFormat = "Failed to enable flag '{0}'.";

		// --- Wizard admin — power management ---
		public const string PowerAddRequiresNameAndAlias = "@POWER/ADD requires power name and alias.";
		public const string PowerNameAndAliasCannotBeEmpty = "Power name and alias cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerCreatedWithAliasFormat = "Power '{0}' created with alias '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToCreatePowerFormat = "Failed to create power '{0}'.";
		public const string PowerDeleteRequiresName = "@POWER/DELETE requires a power name.";
		public const string PowerNameCannotBeEmpty = "Power name cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerNotFoundFormat = "Power '{0}' not found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotDeleteSystemPowerFormat = "Cannot delete system power '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerDeletedFormat = "Power '{0}' deleted.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToDeletePowerFormat = "Failed to delete power '{0}'.";
		public const string PowerAliasRequiresNameAndAlias = "@POWER/ALIAS requires power name and new alias.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerAliasChangedFormat = "Power '{0}' alias changed to '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToUpdatePowerFormat = "Failed to update power '{0}'.";

		// --- Wizard admin — log / quota / dark ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NoLogEntriesForCategoryFormat = "No log entries found for category '{0}'.";
		public const string LogUsage = "Usage: @log[/<switch>] <message> or @log/recall[/<switch>] [<number>]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MessageLoggedToCategoryFormat = "Message logged to {0} log.";
		public const string PoorUsage = "Usage: @poor <player>";
		public const string QuotaSystemDisabled = "The quota system is disabled on this server.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PlayerSetToPoorFormat = "{0} has been set to poor status (quota: 0).";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YourQuotaSetToZeroByFormat = "Your building quota has been set to 0 by {0}.";
		public const string QuotaSystemDisabledMessage = "Quota system disabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string QuotaStatusFormat = "Quota: {0}/{1}";
		public const string AllQuotaUsage = "Usage: @allquota <amount>";
		public const string QuotaAmountMustBeNumber = "Quota amount must be a number.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SetQuotaForPlayersFormat = "Set quota to {0} for {1} players.";
		public const string NotSupportedForSharpMUSH = "Not Supported for SharpMUSH.";
		public const string ErrorDarkFlagNotFound = "Error: DARK flag not found in database.";
		public const string NowHiddenFromWho = "You are now hidden from the WHO list.";
		public const string NoLongerHiddenFromWho = "You are no longer hidden from the WHO list.";
		public const string AlreadyHiddenFromWho = "You are already hidden from the WHO list.";
		public const string AlreadyVisibleOnWho = "You are already visible on the WHO list.";
		public const string NeedAnnouncePower = "Permission denied. You need the Announce power.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MotdClearedFormat = "{0} MOTD cleared.";
		public const string MotdUsage = "Usage: @motd[/<type>] <message>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MotdSetFormat = "{0} MOTD set.";

		// --- Building / destroy ---
		public const string NoSuicideAllowed = "Sorry, no suicide allowed.";
		public const string EvenYouCantDoThat = "Even you can't do that!";
		public const string MayNotDestroyConnectedPlayer = "How gruesome. You may not destroy players who are connected.";
		public const string MustUseNukeToDestroyPlayer = "You must use @nuke to destroy a player.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectAndPossessionsScheduledDestroyedFormat = "{0} and their possessions are scheduled to be destroyed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectScheduledDestroyedFormat = "{0} is scheduled to be destroyed.";

		// --- Softcode functions ---
		public const string DefaultHomeLocationInvalid = "Default home location is invalid.";
		public const string MoneyFunctionNotSupported = "The money() function is not supported. SharpMUSH does not track money or pennies.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MessageSentToRecipientsFormat = "Message sent to {0} recipient(s).";

		// --- @map command ---
		public const string MapMustSpecifyAttribute = "You must specify an attribute to map.";
		public const string MapInvalidObjectAttributePath = "Invalid object/attribute path format.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MapWouldIterateFormat = "@map: Would iterate over {0} elements and execute {1}/{2}";
		public const string MapModeInline = "  Mode: Inline execution";
		public const string MapWillQueueNotify = "  Will queue @notify after completion";
		public const string MapWillClearRegisters = "  Will clear Q-registers";
		public const string MapWillLocalizeRegisters = "  Will localize Q-registers";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MapAttributeNotFoundOnObjectFormat = "Attribute {0} not found on {1}.";

		// --- @dolist command ---
		public const string DoListWhatToDoWithList = "What do you want to do with the list?";

		// --- goto / teleport ---
		public const string ExitNoValidLocationDetail = "That exit doesn't go to a valid location.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ExitNameToDestFormat = "{0} to {1}";
		public const string TeleportedPlayerNotified = "You have been teleported.";

		// --- @find command ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FindSearchingFormat = "@find: Searching for objects{0}...";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FindSearchMatchingFormat = " matching '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FindRangeFormat = "Range: {0} to {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FindObjectResultFormat = "  #{0} ({1})";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FindFoundMatchingFormat = "Found {0} matching objects.";

		// --- @halt command ---
		public const string HaltMustSpecifyPid = "You must specify a process ID.";
		public const string HaltInvalidPidFormat = "Invalid process ID format.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltTaskHaltedFormat = "Task {0} halted.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltNoTaskWithPidFormat = "No task found with PID {0}.";
		public const string HaltMustSpecifyTarget = "You must specify a target object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedPlayerAndObjectsFormat = "Halted {0} and all their objects.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedObjectWithActionsFormat = "Halted {0} with replacement actions.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedObjectFormat = "Halted {0}.";

		// --- @notify command (semaphore) ---
		public const string NotifyMustSpecifySemaphoreObject = "You must specify an object to use for the semaphore.";
		public const string NotifyMustSpecifyValidObjectAttribute = "You must specify a valid object with an optional valid attribute to use for the semaphore.";
		public const string NotifyMustSpecifyQregAssignments = "You must specify Q-register assignments.";
		public const string NotifyQregAssignmentsMustBePairs = "Q-register assignments must be in pairs: qreg,value[,qreg,value...]";
		public const string NotifyInvalidNumber = "Invalid number specified.";
		public const string NotifyNoTaskWaitingOnSemaphore = "No task is waiting on that semaphore.";

		// --- @nsprompt / @prompt ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectDoesNotWantToHearFromYouFormat = "{0} does not want to hear from you.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouPromptedFormat = "You prompted {0}.";

		// --- @switch ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SwitchInvalidRegexpFormat = "Invalid regexp: {0}: {1}";

		// --- @wait command ---
		public const string WaitCommandListMissing = "Command list missing";
		public const string WaitPermissionDenied = "Permission Denied.";
		public const string WaitInvalidTimeArgumentFormat = "Invalid time argument format";
		public const string WaitInvalidFirstArgumentFormat = "Invalid first argument format";
		public const string WaitInvalidPidSpecified = "Invalid PID specified.";
		public const string WaitWhatToDoWithProcess = "What do you want to do with the process?";
		public const string WaitInvalidTimeSpecified = "Invalid time specified.";

		// --- @command command ---
		public const string CommandMustSpecifyName = "You must specify a command name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandAddNotImplementedFormat = "@command/add: Dynamic command creation not yet implemented.";
		public const string CommandMustSpecifyAlias = "You must specify an alias name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandAliasNotImplementedFormat = "@command/alias: Dynamic command aliasing not yet implemented.";
		public const string CommandMustSpecifyCloneName = "You must specify a clone name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandCloneNotImplementedFormat = "@command/clone: Command cloning not yet implemented.";
		public const string CommandOnlyGodCanDelete = "Only God can delete commands.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandDeleteNotImplementedFormat = "@command/delete: Command deletion not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandDisableNotImplementedFormat = "@command/disable: Command disabling not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandEnableNotImplementedFormat = "@command/enable: Command enabling not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandRestrictNotImplementedFormat = "@command/restrict: Command restriction not yet implemented.";
		public const string CommandLibraryUnavailable = "Command library unavailable.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandNotFoundFormat = "Command '{0}' not found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoNameFormat = "Command: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoTypeFormat = "  Type: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoMinArgsFormat = "  Min Args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoMaxArgsFormat = "  Max Args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoSwitchesFormat = "  Switches: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoBehaviorFormat = "  Behavior: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoLockFormat = "  Lock: {0}";

		// --- @drain command ---
		public const string DrainInvalidNumber = "Invalid number specified.";
		public const string DrainCannotSpecifyBothAnyAndAttribute = "You may not specify both /any and a specific attribute.";
		public const string DrainCannotSpecifyBothAllAndNumber = "You may not specify both /all and a number.";

		// --- @force command ---
		public const string ForcePermissionDeniedDoNotControl = "Permission denied. You do not control the target.";
		public const string ForceThemToDoWhat = "Force them to do what?";

		// --- @nsemit / @oemit / @emit / @nsoemit ---
		public const string YouDoNotHavePermissionToSpoofEmitsDetail = "You do not have permission to spoof emits.";

		// --- @nsremit / @lemit / @nslemit / @nszemit / @zemit / @oemit / @remit / @nspemit ---
		public const string DontYouHaveAnythingToSayDetail = "Don't you have anything to say?";
		public const string InvalidRoomSpecifiedDetail = "Invalid room specified.";

		// --- @ps command ---
		public const string PsMustSpecifyPid = "You must specify a process ID.";
		public const string PsInvalidPidFormat = "Invalid process ID format.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsNoTaskWithPidFormat = "No task found with PID {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugTaskFormat = "@ps/debug: Task {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugOwnerFormat = "  Owner: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugSemaphoreFormat = "  Semaphore: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugCommandFormat = "  Command: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugDelayFormat = "  Delay: {0}s";
		public const string PsSummaryHeader = "@ps/summary: Queue totals";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsCommandQueueFormat = "  Command queue: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsWaitQueueFormat = "  Wait queue: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsSemaphoreQueueFormat = "  Semaphore queue: {0}";
		public const string PsLoadAverageZero = "  Load average: 0.0, 0.0, 0.0";
		public const string PsQuickHeader = "@ps/quick: Your queue totals";
		public const string PsAllHeader = "@ps/all: All queued tasks";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsAllGroupFormat = "Group: {0} ({1} tasks)";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsQueueForTargetFormat = "@ps: Queue for {0}";
		public const string PsSemaphoreTasksHeader = "Semaphore tasks:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsSemaphoreTaskEntryFormat = "  [{0}] {1} ({2}): {3}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsAndMoreFormat = "  ... and {0} more";
		public const string PsWaitQueueHeader = "Wait queue tasks:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsWaitTaskEntryFormat = "  [{0}] (delayed)";
		public const string PsQueueManagementNotImplemented = "Note: Queue management not yet implemented.";

		// --- @select command ---
		public const string SelectMustSpecifyTestString = "You must specify a test string.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SelectTestingStringFormat = "@select: Testing string '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SelectExpressionActionPairsFormat = "  Expression/action pairs: {0}";
		public const string SelectHasDefaultAction = "  Has default action";
		public const string SelectModeRegexp = "  Mode: Regular expression matching";
		public const string SelectModeWildcard = "  Mode: Wildcard pattern matching";
		public const string SelectExecutionInline = "  Execution: Inline (immediate)";
		public const string SelectNoBreakWontPropagate = "  @break won't propagate to caller";
		public const string SelectQregistersLocalized = "  Q-registers will be localized";
		public const string SelectQregistersCleared = "  Q-registers will be cleared";
		public const string SelectExecutionQueued = "  Execution: Queued";
		public const string SelectWillQueueNotify = "  Will queue @notify after completion";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SelectInvalidRegexPatternFormat = "Invalid regex pattern: {0}";

		// --- @trigger command ---
		public const string TriggerMustSpecifyAttributePath = "You must specify an object/attribute to trigger.";
		public const string TriggerMustSpecifyObjectAttributePath = "You must specify an object/attribute path.";
		public const string TriggerPermissionDeniedDoNotControl = "Permission denied. You do not control that object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TriggerNoSuchAttributeFormat = "No such attribute: {0}";
		public const string TriggerMustProvideMatchString = "You must provide a string to match when using /match.";

		// --- @whereis command ---
		public const string WhereIsMustSpecifyPlayer = "You must specify a player to locate.";
		public const string WhereIsCanOnlyLocatePlayers = "You can only @whereis players.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WhereIsTriedToLocateYouFormat = "{0} tried to locate you, but was unable to.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WhereIsUnfindableFormat = "{0} is UNFINDABLE.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WhereIsLocatedYourPositionFormat = "{0} has just located your position.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WhereIsObjectInLocationFormat = "{0} is in {1}.";

		// --- @config command ---
		public const string ConfigOnlyGodCanUseSave = "Only God can use /save switch.";
		public const string ConfigSetSaveNotImplemented = "@config/set and @config/save are not yet implemented.";
		public const string ConfigCategoriesHeader = "Configuration Categories:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigCategoryItemFormat = "  {0}";
		public const string ConfigUseCategoryHelp = "Use '@config <category>' to see options in a category.";
		public const string ConfigUseOptionHelp = "Use '@config <option>' to see the value of an option.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigNoOptionsInCategoryFormat = "No options found in category '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigOptionsInCategoryFormat = "Options in {0}:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigOptionValueFormat = "  {0}: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigOptionDescriptionFormat = "  Description: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigOptionCategoryFormat = "  Category: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigNoCategoryOrOptionFormat = "No configuration category or option named '{0}'.";

		// --- @edit command ---
		public const string EditInvalidArguments = "Invalid arguments to @edit.";
		public const string EditInvalidFormat = "Invalid format. Use: object/attribute=search,replace";
		public const string EditMustSpecifySearchAndReplace = "You must specify search and replace strings.";
		public const string EditNoMatchingAttributesFound = "No matching attributes found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EditAttributeSetFormat = "{0} - Set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EditWouldChangeToFormat = "{0} - Would change to: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EditSummaryFormat = "{0} {1} attribute{2}. {3} unchanged.";

		// --- @function command ---
		public const string FunctionLibraryUnavailable = "Function library unavailable.";
		public const string FunctionGlobalUserDefinedHeader = "Global user-defined functions:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionUserDefinedCountFormat = "  User-defined: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionEntryFormat = "    {0}: {1}-{2} args, Flags: {3}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionAndMoreFormat = "    ... and {0} more";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionBuiltInCountFormat = "  Built-in: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionUserDefinedSummaryFormat = "  {0} user-defined functions";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionBuiltInSummaryFormat = "  {0} built-in functions";
		public const string FunctionMustSpecifyName = "You must specify a function name.";
		public const string FunctionMustSpecifyAliasName = "You must specify an alias name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionAliasWouldCreateFormat = "@function/alias: Would create alias '{0}' for function '{1}'.";
		public const string FunctionAliasingNotImplemented = "Note: Function aliasing not yet implemented.";
		public const string FunctionMustSpecifyCloneName = "You must specify a clone name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionCloneWouldCloneFormat = "@function/clone: Would clone function '{0}' as '{1}'.";
		public const string FunctionCloningNotImplemented = "Note: Function cloning not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionDeleteWouldDeleteFormat = "@function/delete: Would delete function '{0}'.";
		public const string FunctionDeletionNotImplemented = "Note: Function deletion not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionDisableWouldDisableFormat = "@function/disable: Would disable function '{0}'.";
		public const string FunctionDisablingNotImplemented = "Note: Function disabling not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionEnableWouldEnableFormat = "@function/enable: Would enable function '{0}'.";
		public const string FunctionEnablingNotImplemented = "Note: Function enabling not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionRestrictWouldRestrictFormat = "@function/restrict: Would restrict function '{0}' to: {1}";
		public const string FunctionRestrictionNotImplemented = "Note: Function restriction not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionDefineWouldDefineFormat = "@function: Would define function '{0}' as: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionMinArgsFormat = "  Min args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionMaxArgsFormat = "  Max args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionRestrictionsArgFormat = "  Restrictions: {0}";
		public const string FunctionDynamicDefinitionNotImplemented = "Note: Dynamic function definition not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionNotFoundFormat = "Function '{0}' not found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoNameFormat = "Function: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoTypeFormat = "  Type: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoMinArgsFormat = "  Min Args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoMaxArgsFormat = "  Max Args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoFlagsFormat = "  Flags: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoRestrictionsFormat = "  Restrictions: {0}";

		// --- @grep command ---
		public const string GrepInvalidArguments = "Invalid arguments to @grep.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GrepErrorReadingAttributesFormat = "Error reading attributes: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GrepRegexpTimedOutFormat = "Regular expression timed out: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GrepInvalidRegexpFormat = "Invalid regular expression: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GrepWildcardTimedOutFormat = "Wildcard pattern timed out: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GrepInvalidWildcardFormat = "Invalid wildcard pattern: {0}";
		public const string GrepNoMatchingAttributesFound = "No matching attributes found.";

		// --- @include command ---
		public const string IncludeMustSpecifyAttributePath = "You must specify an object/attribute to include.";
		public const string IncludeMustSpecifyObjectAttributePath = "You must specify an object/attribute path.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string IncludeNoSuchAttributeFormat = "No such attribute: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string IncludeAttributeIsEmptyFormat = "Attribute {0} is empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string IncludeErrorExecutingFormat = "Error executing included attribute: {0}";

		// --- @mail command ---
		public const string MailTooManySwitches = "Error: Too many switches passed to @mail.";

		// --- @password command ---
		public const string PasswordOnlyPlayersHavePasswords = "Only players have passwords.";
		public const string PasswordInvalid = "Invalid password.";

		// --- @restart command ---
		public const string RestartMustSpecifyObject = "You must specify an object to restart.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RestartedPlayerAndObjectsFormat = "Restarted {0} and all their objects.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RestartedObjectFormat = "Restarted {0}.";

		// --- @sweep command ---
		public const string SweepListeningInRoom = "Listening in ROOM:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepObjectIsListeningFormat = "{0} is listening.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepObjectOwnerIsListeningFormat = "{0} [owner: {1}] is listening.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepRoomSpeechConnectedFormat = "{0} (this room) [speech]. (connected)";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepRoomSpeechFormat = "{0} (this room) [speech].";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepRoomCommandsFormat = "{0} (this room) [commands].";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepRoomBroadcastingFormat = "{0} (this room) [broadcasting].";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepObjectSpeechConnectedFormat = "{0} [speech]. (connected)";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepObjectSpeechFormat = "{0} [speech].";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepObjectCommandsFormat = "{0} [commands].";
		public const string SweepListeningExits = "Listening EXITS:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SweepExitBroadcastingFormat = "{0} [broadcasting].";
		public const string SweepListeningInInventory = "Listening in your INVENTORY:";

		// --- @retry command ---
		public const string RetryUsage = "Usage: @retry <condition>[=<arg0>,<arg1>,...]";
		public const string RetryNothingToRetry = "Nothing to retry.";

		// --- @attribute command ---
		public const string AttributeCommandMustSpecifyAttribute = "You must specify an attribute.";
		public const string AttributeCommandMustSpecifyFlags = "You must specify attribute flags.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandUnknownFlagFormat = "Unknown attribute flag: {0}";
		public const string AttributeCommandFailedToCreate = "Failed to create attribute entry.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandPermissionsNowFormat = "{0} -- Attribute permissions now: {1}";
		public const string AttributeCommandRetroactiveNotImplemented = "Note: Retroactive flag updating not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandRemovedFromTableFormat = "Attribute '{0}' removed from standard attribute table.";
		public const string AttributeCommandExistingCopiesRemain = "Existing copies remain but are no longer \"standard\".";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandNotFoundInTableFormat = "Attribute '{0}' not found in table.";
		public const string AttributeCommandMustSpecifyNewName = "You must specify a new name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandRenamedFormat = "Attribute '{0}' renamed to '{1}' in standard attribute table.";
		public const string AttributeCommandMustSpecifyPattern = "You must specify a regexp pattern.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandLimitSettingPatternFormat = "@attribute/limit: Setting pattern for '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandLimitPatternFormat = "  Pattern: {0}";
		public const string AttributeCommandLimitNewValuesMustMatch = "  New values must match this pattern (case insensitive)";
		public const string AttributeCommandValidationNotImplemented = "Note: Attribute validation not yet implemented.";
		public const string AttributeCommandMustSpecifyChoices = "You must specify a list of choices.";
		public const string AttributeCommandMustSpecifyAtLeastOneChoice = "You must specify at least one choice.";
		public const string AttributeCommandFailedToUpdate = "Failed to update attribute entry.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandEnumSetChoicesFormat = "@attribute/enum: Set choices for '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandEnumChoicesFormat = "  Choices: {0}";
		public const string AttributeCommandEnumNewValuesMustMatch = "  New values must match one of these choices";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandNotFoundNotErrorFormat = "Attribute '{0}' not found in standard attribute table.";
		public const string AttributeCommandNotFoundNotError2 = "This is not an error - the attribute may still be used on objects.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandInfoFormat = "@attribute: Information for '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDefaultFlagsFormat = "  Default flags: {0}";
		public const string AttributeCommandDefaultFlagsNone = "  Default flags: none";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandLimitPatternValueFormat = "  Limit pattern: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandEnumValuesFormat = "  Enum values: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandNoMatchPatternFormat = "No attributes match pattern '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDecompileHeaderFormat = "@attribute/decompile: {0} attributes match pattern '{1}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDecompileAccessFormat = "@attribute/access{0} {1}={2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDecompileLimitFormat = "@attribute/limit {0}={1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDecompileEnumFormat = "@attribute/enum {0}={1}";

		// --- @stats command ---
		public const string StatsTablesNotImplemented = "@stats/tables: Internal table statistics not yet implemented.";
		public const string StatsFlagsNotImplemented = "@stats/flags: Flag system statistics not yet implemented.";
		public const string StatsMemorySwitchesNotImplemented = "@stats memory switches not yet implemented.";
		public const string StatsDatabaseStatisticsHeader = "Database Statistics:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsForPlayerFormat = "  For player: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsRoomsFormat = "  Rooms: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsExitsFormat = "  Exits: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsThingsFormat = "  Things: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsPlayersFormat = "  Players: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsTotalFormat = "  Total: {0}";

		// --- @entrances command ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EntrancesToFormat = "Entrances to {0}:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EntrancesFilteringForFormat = "  Filtering for: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EntrancesRangeFormat = "  Range: {0} to {1}";
		public const string EntrancesZeroFound = "0 entrances found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EntrancesObjectEntryFormat = "  #{0} ({1})";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EntrancesCountFormat = "{0} entrance(s) found.";

		// --- @search command ---
		public const string SearchAdvancedHeader = "@search: Advanced database search";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SearchPlayerFilterFormat = "  Player filter: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SearchCriteriaFormat = "  Criteria: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SearchRangeFormat = "  Range: {0} to {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SearchObjectEntryFormat = "  #{0} ({1}) [{2}]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SearchObjectsFoundFormat = "{0} objects found.";

		// --- @listmotd command ---
		public const string ListMotdCurrentSettingsHeader = "Current Message of the Day settings:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdConnectFileFormat = "  Connect MOTD File: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdConnectHtmlFormat = "  Connect MOTD HTML: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdWizardFileFormat = "  Wizard MOTD File: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdWizardHtmlFormat = "  Wizard MOTD HTML: {0}";
		public const string ListMotdTemporaryHeader = "Temporary Message of the Day (cleared on restart):";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdConnectMotdFormat = "  Connect MOTD: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdWizardMotdFormat = "  Wizard MOTD:  {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdDownMotdFormat = "  Down MOTD:    {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdFullMotdFormat = "  Full MOTD:    {0}";

		// --- @verb command ---
		public const string VerbUsage = "Usage: @verb <victim>=<actor>,<what>,<whatd>,<owhat>,<owhatd>,<awhat>[,<args>]";

		// --- @whereis ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WhereIsTriedToLocateButUnableFormat = "{0} tried to locate you, but was unable to.";

		// --- @flag — missing sub-command constants ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotModifySystemFlagFormat = "Cannot modify system flag '{0}'.";
		public const string FlagDebugRequiresName = "@FLAG/DEBUG requires a flag name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagDisableEnableRequiresNameFormat = "@FLAG/{0} requires a flag name.";
		public const string FlagUsage = "Usage: @flag/list, @flag/add <name>=<symbol>, @flag/delete <name>, @flag/letter <name>=<symbol>, @flag/type <name>=<types>, @flag/alias <name>=<aliases>, @flag/restrict <name>=<permissions>, @flag/decompile <name>";

		// --- @power — missing sub-command constants ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotModifySystemPowerFormat = "Cannot modify system power '{0}'.";
		public const string PowerTypeRequiresNameAndTypes = "@POWER/TYPE requires power name and type restrictions.";
		public const string PowerNameAndTypesCannotBeEmpty = "Power name and types cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerTypeUpdatedFormat = "Power '{0}' type restrictions updated to: {1}.";
		public const string PowerRestrictRequiresNameAndPermissions = "@POWER/RESTRICT requires power name and permissions.";
		public const string PowerNameAndPermissionsCannotBeEmpty = "Power name and permissions cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerPermissionsUpdatedFormat = "Power '{0}' permissions updated to: {1}.";
		public const string PowerDecompileRequiresName = "@POWER/DECOMPILE requires a power name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerDisableEnableRequiresNameFormat = "@POWER/{0} requires a power name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotDisableSystemPowerFormat = "Cannot disable system power '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerDisabledFormat = "Power '{0}' disabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerEnabledFormat = "Power '{0}' enabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToDisablePowerFormat = "Failed to disable power '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToEnablePowerFormat = "Failed to enable power '{0}'.";
		public const string PowerUsage = "Usage: @power/list, @power/add <name>=<alias>, @power/delete <name>, @power/alias <name>=<alias>, @power/type <name>=<types>, @power/restrict <name>=<permissions>, @power/decompile <name>";

		// --- @rejectmotd / @wizmotd ---
		public const string FullMotdCleared = "Full MOTD cleared.";
		public const string RejectMotdUsage = "Usage: @rejectmotd <message>";
		public const string FullMotdSet = "Full MOTD set.";
		public const string WizMotdCleared = "Wizard MOTD cleared.";
		public const string WizMotdUsage = "Usage: @wizmotd <message>";
		public const string WizMotdSet = "Wizard MOTD set.";

		// --- @suggest ---
		public const string NoSuggestionCategoriesDefined = "No suggestion categories defined.";
		public const string SuggestAddUsage = "Usage: @suggest/add <category>=<word>";
		public const string SuggestCategoryAndWordCannotBeEmpty = "Category and word cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SuggestAddedWordToCategoryFormat = "Added '{0}' to category '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SuggestWordAlreadyExistsFormat = "Word '{0}' already exists in category '{1}'.";
		public const string SuggestDeleteUsage = "Usage: @suggest/delete <category>=<word>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SuggestCategoryDoesNotExistFormat = "Category '{0}' does not exist.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SuggestRemovedWordFromCategoryFormat = "Removed '{0}' from category '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SuggestWordNotFoundInCategoryFormat = "Word '{0}' not found in category '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SuggestCategoryWordCountFormat = "Category '{0}' ({1} words):";
		public const string SuggestUsage = "Usage: @suggest[/list], @suggest <category>, @suggest/add <category>=<word>, @suggest/delete <category>=<word>";

		// --- @boot ---
		public const string BootPortUsage = "Usage: @boot/port <descriptor number>";
		public const string BootDescriptorMustBeNumber = "Descriptor number must be a number.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BootNoSuchDescriptorFormat = "No such descriptor: {0}.";
		public const string BootUsage = "Usage: @boot <player> | @boot/me | @boot/port <descriptor>";
		public const string PlayerNotConnected = "That player is not connected.";
		public const string YouHaveBeenDisconnected = "You have been disconnected.";

		// --- @hook ---
		public const string HookMustSpecifyCommandName = "You must specify a command name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookNoHooksForCommandFormat = "No hooks set for command '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookListHeaderFormat = "Hooks for command '{0}':";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookEntryFormat = "  {0}: {1}/{2}{3}";
		public const string HookMustSpecifyType = "You must specify a hook type: /ignore, /override, /before, /after, or /extend";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookClearedFormat = "Hook '{0}' cleared for command '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookNotSetFormat = "No '{0}' hook set for command '{1}'.";
		public const string HookMustSpecifyObject = "You must specify an object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookAttributeNotFoundFormat = "Attribute '{0}' not found on object {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookSetFormat = "Hook '{0}' set for command '{1}'{2}.";

		// --- @newpassword ---
		public const string NewPasswordGenerateSwitchConflict = "@NEWPASSWORD: /GENERATE switch cannot be used with other arguments.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NewPasswordGeneratedFormat = "Generated password for {0}: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NewPasswordSetFormat = "Set new password for {0}: {1}";

		// --- @purge ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PurgeCompleteFormat = "Purge complete. {0} objects advanced to GOING_TWICE. {1} objects marked for final deletion.";
		public const string PurgeNoteBackgroundGc = "Note: Actual object deletion is handled by background garbage collection in SharpMUSH.";

		// --- @shutdown ---
		public const string ShutdownOnlyGodPanic = "Only God can perform a panic shutdown.";
		public const string ShutdownPanicInitiated = "PANIC SHUTDOWN initiated by God.";
		public const string ShutdownRebootInitiated = "REBOOT initiated. In SharpMUSH's web-based architecture:";
		public const string ShutdownRebootDocker = "- For Docker/Kubernetes: Update deployment to trigger rolling restart";
		public const string ShutdownRebootStandalone = "- For standalone: Restart the web application";
		public const string ShutdownRebootRedis = "- Player connections will be preserved via Redis state store";
		public const string ShutdownParanoidInitiated = "PARANOID SHUTDOWN initiated.";
		public const string ShutdownParanoidArangoDB = "Database state is continuously persisted in ArangoDB.";
		public const string ShutdownInitiated = "SHUTDOWN initiated.";
		public const string ShutdownNoteWebApp = "Note: SharpMUSH runs as a web application. Traditional shutdown is not applicable.";
		public const string ShutdownNoteOrchestration = "In cloud/container deployments, use your orchestration tools to manage server lifecycle.";
		public const string ShutdownNoteNoSave = "Database state is preserved automatically. No explicit save is needed.";

		// --- @chownall ---
		public const string ChownAllUsage = "Usage: @chownall <player>[=<new owner>]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChownAllCompleteFormat = "Changed ownership of {0} object(s) from {1} to {2}.";

		// --- @dump ---
		public const string DumpDoesNothing = "Dump command does nothing for SharpMUSH. Consider using @backup.";

		// --- @pcreate ---
		public const string PlayerCreateInvalidName = "That is not a valid player name.";
		public const string PlayerNameAlreadyExists = "That player name already exists.";
		public const string PlayerCreateInvalidPassword = "That is not a valid password.";

		// --- @quota ---
		public const string QuotaSetUsage = "Usage: @quota/set <player>=<amount>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string QuotaForPlayerSetFormat = "Quota for {0} set to {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YourQuotaSetToByFormat = "Your quota has been set to {0} by {1}.";
		public const string QuotaListingHeader = "Quota listing for all players:";
		public const string QuotaListingColumnHeader = "Player                      Used/Quota";
		public const string QuotaListingSeparator = "=========================================";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string QuotaPlayerRowFormat = "{0} {1,4}/{2,-4}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string QuotaPlayerObjectsFormat = "{0}'s quota: {1}/{2} objects used.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AllQuotaSetForPlayerFormat = "Your building quota has been set to {0} by {1}.";

		// --- @sitelock ---
		public const string SitelockCheckRequiresHost = "@SITELOCK/CHECK requires a hostname or IP address.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SitelockHostMatchesFormat = "Host '{0}' matches pattern '{1}' with options: {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SitelockHostNoMatchFormat = "Host '{0}' does not match any sitelock rules (default access allowed).";
		public const string SitelockNameRequiresName = "@SITELOCK/NAME requires a player name.";
		public const string SitelockNameNotImplemented = "@SITELOCK/NAME modification is not yet implemented. Use the admin UI to modify banned names.";
		public const string SitelockBanRequiresPattern = "@SITELOCK/BAN requires a host pattern.";
		public const string SitelockBanNotImplemented = "@SITELOCK/BAN modification is not yet implemented. Use the admin UI to add sitelock rules.";
		public const string SitelockRegisterRequiresPattern = "@SITELOCK/REGISTER requires a host pattern.";
		public const string SitelockRegisterNotImplemented = "@SITELOCK/REGISTER modification is not yet implemented. Use the admin UI to add sitelock rules.";
		public const string SitelockRemoveRequiresPattern = "@SITELOCK/REMOVE requires a host pattern.";
		public const string SitelockRemoveNotImplemented = "@SITELOCK/REMOVE modification is not yet implemented. Use the admin UI to remove sitelock rules.";
		public const string SitelockRuleNotImplemented = "@SITELOCK rule modification is not yet implemented. Use the admin UI to modify sitelock rules.";
		public const string SitelockInvalidSyntax = "Invalid @SITELOCK syntax. Use '@help @sitelock' for usage information.";

		// --- @chzoneall ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ZonesClearedForOwnerFormat = "Zones cleared for {0} object(s) owned by {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ZoneSetForOwnerFormat = "Zone set to {0} for {1} object(s) owned by {2}.";

		// --- @kick ---
		public const string KickUsage = "Usage: @kick <player>";

		// --- @poll ---
		public const string PollMessageCleared = "Poll message cleared.";
		public const string PollNoPollMessage = "No poll message is currently set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PollCurrentMessageFormat = "Current poll: {0}";
		public const string PollMessageSet = "Poll message set.";

		// --- @readcache ---
		public const string ReadCacheServiceNotAvailable = "Text file service not available.";
		public const string ReadCacheReindexing = "Reindexing text files...";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ReadCacheCompleteFormat = "Text file cache rebuilt in {0}ms.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ReadCacheErrorFormat = "Error reindexing text files after {0}ms: {1}";

		// --- @enable / @disable ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EnableDisableUsageSyntaxFormat = "Usage: @{0} <option>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EnableDisableNoOptionFormat = "No configuration option named '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EnableDisableNotBooleanFormat = "Option '{0}' is not a boolean option. Use @config/set instead.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EnableDisableEquivalentFormat = "@{0} is equivalent to @config/set {1}={2}";
		public const string RuntimeConfigNotImplemented = "Runtime configuration modification is not yet implemented. Changes require server restart.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigCurrentValueFormat = "Current value: {0}={1}";
	}
}
