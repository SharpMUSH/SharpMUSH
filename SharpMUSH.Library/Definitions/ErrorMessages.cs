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
		public const string InvalidFlag = "#-1 INVALID FLAG FOR THIS OBJECT";
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
		public const string AmbiguousMatch = "I don't know which one you mean.";
		
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
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PermissionDeniedSetAttribute = "Permission denied to set attribute on {0}.";
		public const string LackSpoofingPermissions = "Permission denied: You lack spoofing permissions.";
		
		// Argument and validation notifications
		public const string InvalidArgument = "Invalid argument.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InvalidArguments = "Invalid arguments to {0}.";
		public const string InvalidDbref = "That's not a valid object reference.";
		
		// Name and alias notifications
		public const string PlayerNameInUse = "That player name is already in use.";
		public const string PlayerAliasInUse = "That player alias is already in use.";
		
		// Function notifications
		public const string RecursionLimit = "That caused too much recursion.";
		public const string FunctionDisabled = "That function is disabled.";
		
		// Operation result notifications
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string Created = "Created {0} ({1}).";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WipedAttributes = "Wiped attributes matching {0}.";
		public const string CouldNotFindNewOwner = "Could not find new owner.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CouldNotFindDestination = "Could not find destination: {0}";
	}
}
