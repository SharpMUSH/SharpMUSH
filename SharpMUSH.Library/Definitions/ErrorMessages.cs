namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Internationalization-ready error messaging system.
/// Separates error codes (technical, never translated) from user-facing messages (i18n-ready).
/// </summary>
public static class ErrorMessages
{
	/// <summary>
	/// Error codes - technical identifiers that never change.
	/// These are API-stable and should never be translated.
	/// </summary>
	public static class Codes
	{
		public const string BadObjectName = "#-1";
		public const string NoMatch = "#-1";
		public const string PermissionDenied = "#-1";
		public const string InvalidArgument = "#-1";
		public const string NotARoom = "#-1";
		public const string NotAnExit = "#-1";
		public const string NotAThing = "#-1";
		public const string NotAPlayer = "#-1";
		public const string NoSuchObject = "#-1";
		public const string AmbiguousMatch = "#-2";
		public const string RecursionLimit = "#-1";
		public const string FunctionDisabled = "#-1";
		public const string InvalidDbref = "#-1";
	}

	/// <summary>
	/// English messages - default user-facing text.
	/// These serve as the template for translations.
	/// </summary>
	public static class English
	{
		public const string BadObjectName = "BAD OBJECT NAME";
		public const string NoMatch = "NO MATCH";
		public const string PermissionDenied = "PERMISSION DENIED";
		public const string InvalidArgument = "INVALID ARGUMENT";
		public const string NotARoom = "NOT A ROOM";
		public const string NotAnExit = "NOT AN EXIT";
		public const string NotAThing = "NOT A THING";
		public const string NotAPlayer = "NOT A PLAYER";
		public const string NoSuchObject = "NO SUCH OBJECT";
		public const string AmbiguousMatch = "AMBIGUOUS MATCH";
		public const string RecursionLimit = "RECURSION LIMIT EXCEEDED";
		public const string FunctionDisabled = "FUNCTION DISABLED";
		public const string InvalidDbref = "INVALID DBREF";
	}

	// Combined error messages - current implementation (Phase 1)
	// Future: Add optional culture parameter for i18n support
	public static string BadObjectName => $"{Codes.BadObjectName} {English.BadObjectName}";
	public static string NoMatch => $"{Codes.NoMatch} {English.NoMatch}";
	public static string PermissionDenied => $"{Codes.PermissionDenied} {English.PermissionDenied}";
	public static string InvalidArgument => $"{Codes.InvalidArgument} {English.InvalidArgument}";
	public static string NotARoom => $"{Codes.NotARoom} {English.NotARoom}";
	public static string NotAnExit => $"{Codes.NotAnExit} {English.NotAnExit}";
	public static string NotAThing => $"{Codes.NotAThing} {English.NotAThing}";
	public static string NotAPlayer => $"{Codes.NotAPlayer} {English.NotAPlayer}";
	public static string NoSuchObject => $"{Codes.NoSuchObject} {English.NoSuchObject}";
	public static string AmbiguousMatch => $"{Codes.AmbiguousMatch} {English.AmbiguousMatch}";
	public static string RecursionLimit => $"{Codes.RecursionLimit} {English.RecursionLimit}";
	public static string FunctionDisabled => $"{Codes.FunctionDisabled} {English.FunctionDisabled}";
	public static string InvalidDbref => $"{Codes.InvalidDbref} {English.InvalidDbref}";
}
