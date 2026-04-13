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

		// Permission notifications
		public const string PermissionDenied = "Permission denied.";
		public const string NoPermission = "You don't have permission to do that.";
		public const string YouDoNotControlThatObject = "You do not control that object.";
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
	}
}
