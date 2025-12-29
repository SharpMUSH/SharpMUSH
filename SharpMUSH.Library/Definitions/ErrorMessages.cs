namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Internationalization-ready error messaging system.
/// Error returns use technical MUSH format (e.g., "#-1 PERMISSION DENIED")
/// User notifications use friendly messages (e.g., "You don't have permission to do that.")
/// </summary>
public static class ErrorMessages
{
	/// <summary>
	/// Technical error messages for error returns (parsing/API).
	/// Format: "#-1 ERROR NAME" - matches existing Errors class pattern.
	/// These are used in CallState returns and should NOT be translated.
	/// </summary>
	public static class Returns
	{
		public const string BadObjectName = "#-1 BAD OBJECT NAME";
		public const string NoMatch = "#-1 NO MATCH";
		public const string PermissionDenied = "#-1 PERMISSION DENIED";
		public const string InvalidArgument = "#-1 INVALID ARGUMENT";
		public const string NotARoom = "#-1 NOT A ROOM";
		public const string NotAnExit = "#-1 NOT AN EXIT";
		public const string NotAThing = "#-1 NOT A THING";
		public const string NotAPlayer = "#-1 NOT A PLAYER";
		public const string NoSuchObject = "#-1 NO SUCH OBJECT";
		public const string AmbiguousMatch = "#-2 I DON'T KNOW WHICH ONE YOU MEAN";  // #-2 for ambiguity
		public const string RecursionLimit = "#-1 RECURSION LIMIT EXCEEDED";
		public const string FunctionDisabled = "#-1 FUNCTION DISABLED";
		public const string InvalidDbref = "#-1 INVALID DBREF";
	}

	/// <summary>
	/// User-friendly notification messages for player notifications.
	/// These are more natural English and SHOULD be translated for i18n.
	/// </summary>
	public static class Notifications
	{
		public const string BadObjectName = "I don't understand that object name.";
		public const string NoMatch = "I don't see that here.";
		public const string PermissionDenied = "You don't have permission to do that.";
		public const string InvalidArgument = "Invalid argument.";
		public const string NotARoom = "That's not a room.";
		public const string NotAnExit = "That's not an exit.";
		public const string NotAThing = "That's not a thing.";
		public const string NotAPlayer = "That's not a player.";
		public const string NoSuchObject = "I can't find that.";
		public const string AmbiguousMatch = "I don't know which one you mean.";
		public const string RecursionLimit = "That caused too much recursion.";
		public const string FunctionDisabled = "That function is disabled.";
		public const string InvalidDbref = "That's not a valid object reference.";
	}
}
