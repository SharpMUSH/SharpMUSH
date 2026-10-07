using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Internationalization-ready error messaging system.
/// Consolidates all error constants from Errors class plus new ones.
/// Returns: Technical MUSH format (e.g., "#-1 PERMISSION DENIED") - never translated
/// Notifications: User-friendly messages (e.g., "You don't have permission to do that.") - translatable
/// </summary>
public static partial class ErrorMessages
{
	/// <summary>
	/// Technical error messages for error returns (parsing/API).
	/// Format: "#-1 ERROR NAME" or "#-2 ERROR NAME" for ambiguity.
	/// These are used in CallState returns and should NOT be translated.
	/// Merged from Errors class for full consistency.
	/// </summary>
	public static class Returns
	{
		public const string BadObjectName = "#-1 BAD OBJECT NAME";
		public const string NoMatch = "#-1 NO MATCH";
		public const string NoSuchObject = "#-1 NO SUCH OBJECT";
		public const string AmbiguousMatch = "#-2 I DON'T KNOW WHICH ONE YOU MEAN";
		public const string NotVisible = "#-1 NO SUCH OBJECT VISIBLE";
		public const string CantSeeThat = "#-1 CAN'T SEE THAT HERE";
		public const string InvalidDbref = "#-1 INVALID DBREF";

		public const string NotARoom = "#-1 NOT A ROOM";

		/// <summary>
		/// <c>fun_open</c> (<c>src/fundb.c:2144-2173</c>) when its third argument names nothing that can
		/// source an exit. The command form reports <see cref="Notifications.ExitsOnlyFromRooms"/> instead;
		/// the function has no one to notify, so the reason is the return value.
		/// </summary>
		public const string InvalidSourceRoom = "#-1 INVALID SOURCE ROOM";
		public const string NotAPlayer = "#-1 NOT A PLAYER";
		public const string InvalidPlayer = "#-1 INVALID PLAYER";
		public const string InvalidRoom = "#-1 INVALID ROOM";
		public const string InvalidDestination = "#-1 INVALID DESTINATION";
		public const string InvalidObjectType = "#-1 INVALID OBJECT TYPE";

		public const string PermissionDenied = "#-1 PERMISSION DENIED";

		/// <summary>
		/// <c>can_pay_fees</c> refusing <c>pay_quota</c> (<c>src/predicat.c:453-456</c>). Penn's building
		/// functions answer a refusal with a bare <c>#-1</c> and leave the notification to carry the
		/// reason; this says which of the several ways a build can be refused happened.
		/// </summary>
		public const string BuildingQuotaExhausted = "#-1 BUILDING QUOTA EXHAUSTED";

		/// <summary>
		/// PennMUSH <c>fun_ssl</c> / <c>fun_terminfo</c> (src/bsd.c) when <c>lookup_desc</c> finds no
		/// descriptor. The rest of the connection family answers a miss with a bare <c>#-1</c> or
		/// <c>-1</c> instead; the wording is per-function and not a house style.
		/// </summary>
		public const string NotConnected = "#-1 NOT CONNECTED";

		/// <summary>
		/// PennMUSH <c>fun_terminfo</c> / <c>fun_width</c> / <c>fun_height</c> on an empty first
		/// argument. Those three check for it explicitly; the rest of the family lets an empty name
		/// fall through <c>lookup_desc</c> and fail as an ordinary miss.
		/// </summary>
		public const string FunctionRequiresOneArgument = "#-1 FUNCTION REQUIRES ONE ARGUMENT";
		public const string AttrPermissions = "#-1 NO PERMISSION TO GET ATTRIBUTE";
		public const string AttrEvalPermissions = "#-1 NO PERMISSION TO EVALUATE ATTRIBUTE";
		public const string AttrSetPermissions = "#-1 NO PERMISSION TO SET ATTRIBUTE";

		public const string InvalidArgument = "#-1 INVALID ARGUMENT";
		public const string Integer = "#-1 ARGUMENT MUST BE INTEGER";
		public const string PositiveInteger = "#-1 ARGUMENT MUST BE POSITIVE INTEGER";
		public const string Integers = "#-1 ARGUMENTS MUST BE INTEGERS";
		public const string UInteger = "#-1 ARGUMENT MUST BE POSITIVE INTEGER";
		public const string UIntegers = "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS";
		public const string Number = "#-1 ARGUMENT MUST BE NUMBER";
		public const string Numbers = "#-1 ARGUMENTS MUST BE NUMBERS";
		public const string DomainError = "#-1 DOMAIN ERROR";
		public const string InvalidPassword = "#-1 INVALID PASSWORD";
		public const string InvalidFlag = "#-1 INVALID FLAG";
		public const string InvalidPower = "#-1 INVALID POWER";
		public const string ObjectAttributeString = "#-1 INVALID OBJECT/ATTRIBUTE VALUE";
		/// <summary>
		/// PennMUSH's single failure for every bad flag argument (<c>src/set.c:583-585</c>,
		/// "Unrecognized attribute flag."): an unknown name, an empty name, and a privileged
		/// name the player may not use all fail the whole argument identically, so the wording
		/// never reveals that a flag the player cannot use exists.
		/// </summary>
		public const string UnrecognizedAttributeFlag = "#-1 UNRECOGNIZED ATTRIBUTE FLAG";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BadArgumentFormat = "#-1 BAD ARGUMENT FORMAT TO {0}";
		public const string ArgRange = "#-1 ARGUMENT OUT OF RANGE";
		/// <summary>PennMUSH's <c>chr()</c> answer for a control character (<c>fun_chr</c>, src/funstr.c).</summary>
		public const string UnprintableCharacter = "#-1 UNPRINTABLE CHARACTER";
		public const string TimeInteger = "#-1 TIME INTEGER OUT OF RANGE";

		public const string NoSuchAttribute = "#-1 NO SUCH ATTRIBUTE";

		/// <summary>
		/// Wording matches PennMUSH's <c>#-1 FUNCTION (NAME) NOT FOUND</c> (src/parse.c), which
		/// <c>fn()</c> already emitted verbatim while the parser used a different phrasing for the
		/// same condition. Takes the function name, conventionally upper-cased by the caller.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NoSuchFunction = "#-1 FUNCTION ({0}) NOT FOUND";
		/// <summary>
		/// PennMUSH <c>src/parse.c:3049-3059</c>: a global <c>@function</c> whose attribute is gone. Takes the
		/// function name, the object's dbref and the attribute name.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string UserFunctionMissingAttribute = "#-1 @FUNCTION ({0}) MISSING ATTRIBUTE ({1}/{2})";
		public const string NoSuchPower = "#-1 NO SUCH POWER";
		public const string NoSuchFlag = "#-1 NO SUCH FLAG";
		public const string NoSuchTimezone = "#-1 NO SUCH TIMEZONE";
		public const string FunctionDisabled = "#-1 FUNCTION DISABLED";
		public const string NoSideFx = "#-1 SIDE EFFECTS DISABLED FOR THIS FUNCTION";

		public const string Invoke = "#-1 FUNCTION INVOCATION LIMIT EXCEEDED";
		public const string Recursion = "#-1 FUNCTION RECURSION LIMIT EXCEEDED";
		public const string Call = "#-1 CALL LIMIT EXCEEDED";
		public const string OutputTooLarge = "#-1 OUTPUT EXCEEDED MAXIMUM SIZE";
		public const string RegisterRange = "#-1 REGISTER OUT OF RANGE";
		public const string BadRegName = "#-1 REGISTER NAME INVALID";
		public const string TooManySwitches = "#-1 TOO MANY SWITCHES, OR A BAD COMBINATION OF SWITCHES";
		public const string OutOfRange = "#-1 OUT OF RANGE";
		/// <summary><c>fun_die</c>'s answer for a count of dice outside 1..700 (<c>src/funmisc.c:851-852</c>).</summary>
		public const string NumberOutOfRange = "#-1 NUMBER OUT OF RANGE";
		/// <summary><c>fun_wrap</c>'s answer for a width below 2 (<c>src/funstr.c:1666-1668</c>).</summary>
		public const string WidthTooSmall = "#-1 WIDTH TOO SMALL";

		public const string NoSuchConfigOption = "#-1 NO SUCH CONFIG OPTION";
		public const string InvalidZone = "#-1 INVALID ZONE";
		/// <summary>lmath() given an operator it does not know (<c>fun_lmath</c>, src/funmath.c).</summary>
		public const string UnknownOperation = "#-1 UNKNOWN OPERATION";
		/// <summary>An lmath() comparison over fewer than two numbers (<c>lmathcomp</c>, src/funmath.c).</summary>
		public const string ComparisonRequiresTwoNumbers = "#-1 COMPARISON REQUIRES 2 OR MORE NUMBERS";
		public const string SeparatorMustBeOneChar = "#-1 SEPARATOR MUST BE ONE CHARACTER";
		public const string MissingArguments = "#-1 MISSING ARGUMENTS";

		public const string AmbiguousChannelName = "#-2 AMBIGUOUS CHANNEL NAME";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TooFewArguments = "#-1 FUNCTION ({0}) EXPECTS AT LEAST {1} ARGUMENTS BUT GOT {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TooManyArguments = "#-1 FUNCTION ({0}) EXPECTS AT MOST {1} ARGUMENTS BUT GOT {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TooFewCommandArguments = "#-1 COMMAND ({0}) EXPECTS AT LEAST {1} ARGUMENTS BUT GOT {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GotEvenArgs = "#-1 FUNCTION ({0}) EXPECTS AN ODD NUMBER OF ARGUMENTS";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GotUnEvenArgs = "#-1 FUNCTION ({0}) EXPECTS AN EVEN NUMBER OF ARGUMENTS";
		/// <summary>An lmath() distance over the wrong number of values (<c>math_dist2d</c>, src/funmath.c).</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ExpectsExactArguments = "#-1 FUNCTION ({0}) EXPECTS {1} ARGUMENTS";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WrongArgumentsRange = "#-1 FUNCTION ({0}) EXPECTS AT LEAST {1} ARGUMENTS AND AT MOST {2} BUT GOT {3}";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ParserFailure = "#-1 PARSER FAILURE: {0}";

		public const string NothingToDo = "#-1 NOTHING TO DO";
		public const string CantSeeThroughThat = "#-1 CANNOT SEE THROUGH THAT";
		public const string ParentLoop = "#-1 PARENT LOOP DETECTED";
		public const string TooManyAncestors = "#-1 TOO MANY ANCESTORS";
		public const string SafeObject = "#-1 OBJECT IS SAFE";

		public const string ArgMustEndInInteger = "#-1 ARGUMENT MUST END IN AN INTEGER";
		public const string ConnectionNotFound = "#-1 CONNECTION NOT FOUND";
		public const string DecryptionError = "#-1 DECRYPTION ERROR";
		public const string Depth = "#-1 MAX DEPTH EXCEEDED";
		public const string DivisionByZero = "#-1 DIVISION BY ZERO";
		public const string EntryNotFound = "#-1 ENTRY NOT FOUND";
		public const string Error = "#-1 ERROR";
		public const string ErrorNotSupported = "#-1 NOT SUPPORTED";
		/// <summary>A named text file that does not exist, PennMUSH's wording (<c>src/help.c:1144</c>).</summary>
		public const string NoSuchFile = "#-1 NO SUCH FILE";
		public const string ImaginaryNumber = "#-1 IMAGINARY NUMBER";
		public const string InvalidAction = "#-1 INVALID ACTION";
		public const string InvalidAlignString = "#-1 INVALID ALIGN STRING";
		public const string InvalidArguments = "#-1 INVALID ARGUMENTS";
		public const string InvalidBase64String = "#-1 INVALID BASE64 STRING";
		public const string InvalidClass = "#-1 INVALID CLASS";
		public const string InvalidColor = "#-1 INVALID COLOR";
		public const string InvalidConnectionId = "#-1 INVALID CONNECTION ID";
		public const string InvalidEscapeCode = "#-1 INVALID ESCAPE CODE";
		public const string InvalidField = "#-1 INVALID FIELD";
		public const string InvalidFieldWidth = "#-1 INVALID FIELD WIDTH";
		public const string InvalidFilter = "#-1 INVALID FILTER";
		public const string InvalidFormat = "#-1 INVALID FORMAT";
		public const string InvalidInfoType = "#-1 INVALID INFO TYPE";
		public const string InvalidJsonMessage = "#-1 INVALID JSON MESSAGE";
		public const string InvalidLineWidth = "#-1 INVALID LINE WIDTH";
		public const string InvalidLocation = "#-1 INVALID LOCATION";
		public const string InvalidLock = "#-1 INVALID LOCK";
		public const string InvalidObject = "#-1 INVALID OBJECT";
		public const string InvalidObjectAttribute = "#-1 INVALID OBJECT/ATTRIBUTE";
		public const string InvalidOperation = "#-1 INVALID OPERATION";
		public const string InvalidPid = "#-1 INVALID PID";
		public const string InvalidPrecision = "#-1 INVALID PRECISION";
		public const string InvalidQueueType = "#-1 INVALID QUEUE TYPE";
		public const string InvalidRegex = "#-1 INVALID REGEX";
		public const string InvalidSearchType = "#-1 INVALID SEARCH TYPE";
		public const string InvalidSecondArgument = "#-1 INVALID SECOND ARGUMENT";
		public const string InvalidSeconds = "#-1 INVALID SECONDS";
		public const string InvalidSpecPair = "#-1 INVALID SPEC PAIR";
		public const string InvalidSpecType = "#-1 INVALID SPEC TYPE";
		public const string InvalidTagName = "#-1 INVALID TAG NAME";
		public const string InvalidTimezone = "#-1 INVALID TIMEZONE";
		public const string InvalidTimestring = "#-1 INVALID TIMESTRING";
		public const string InvalidType = "#-1 INVALID TYPE";
		public const string InvalidValue = "#-1 INVALID VALUE";
		public const string InvalidWidth = "#-1 INVALID WIDTH (MUST BE 10-1000)";
		public const string MalformedNumber = "#-1 MALFORMED NUMBER";
		public const string MissingJson2 = "#-1 MISSING JSON2";
		public const string NoMatchingColorName = "#-1 NO MATCHING COLOR NAME";
		public const string NoQuerySpecified = "#-1 NO QUERY SPECIFIED";
		public const string NoSuchMail = "#-1 NO SUCH MAIL";
		/// <summary>PennMUSH <c>fun_mail</c> (<c>src/extmail.c:2144</c>) when <c>mailfun_fetch</c> found nothing to return.</summary>
		public const string InvalidMessageOrPlayer = "#-1 INVALID MESSAGE OR PLAYER";
		public const string NoSuchOption = "#-1 NO SUCH OPTION";
		public const string NoSuchWikiPage = "#-1 NO SUCH WIKI PAGE";
		public const string NoSuchPid = "#-1 NO SUCH PID";
		public const string NoSuchPlayer = "#-1 NO SUCH PLAYER";
		/// <summary><c>pagerecall()</c> and <c>pageconversations()</c> while <c>page_log</c> is off.</summary>
		public const string PageLoggingIsOff = "#-1 PAGE LOGGING IS OFF";
		public const string PlayerNameInUse = "#-1 PLAYER NAME ALREADY IN USE";
		public const string BadPlayerName = "#-1 BAD PLAYER NAME";
		public const string BadPassword = "#-1 BAD PASSWORD";
		public const string NoSuchType = "#-1 NO SUCH TYPE";
		public const string NoZoneSet = "#-1 NO ZONE SET";
		public const string NoDropTo = "#-1 NO DROP-TO";
		public const string NotLinked = "#-1 NOT LINKED";
		public const string VariableDestination = "#-2 VARIABLE DESTINATION";
		public const string HomeDestination = "#-3 HOME";
		public const string NoContents = "#-1 NO CONTENTS";
		public const string NoExits = "#-1 NO EXITS";
		public const string NoNextObject = "#-1 NO NEXT OBJECT";
		public const string ConnectionLogDisabled = "#-1 CONNECTION LOG DISABLED";
		public const string NoSuchDescriptor = "#-1 NO SUCH DESCRIPTOR";

		/// <summary>
		/// A descriptor lookup by a caller who may not see every descriptor: "no such descriptor" and
		/// "not yours to see" have to read the same, or the answer says whether the descriptor exists.
		/// </summary>
		public const string NoSuchDescriptorOrPermissionDenied = "#-1 NO SUCH DESCRIPTOR OR PERMISSION DENIED";
		public const string NeedAWord = "#-1 NEED A WORD";
		public const string NonNegativeInteger = "#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER";
		public const string NotAMember = "#-1 NOT A MEMBER OF THAT CHANNEL";
		public const string NotAnArray = "#-1 NOT AN ARRAY";
		public const string NotEnoughColumnsForAlign = "#-1 NOT ENOUGH COLUMNS FOR ALIGN";
		public const string NumberOfWordsMustBeEqual = "#-1 NUMBER OF WORDS MUST BE EQUAL";
		public const string ObjectHasNoParent = "#-1 OBJECT HAS NO PARENT";
		public const string PasswordRequired = "#-1 PASSWORD REQUIRED";
		public const string PathMustBeSingular = "#-1 PATH MUST BE SINGULAR";
		public const string PathNotFound = "#-1 PATH NOT FOUND";
		public const string RecipientDoesNotAcceptMail = "#-1 RECIPIENT DOES NOT ACCEPT MAIL FROM YOU";
		public const string RegexpInvalid = "#-1 REGEXP ERROR: INVALID REGULAR EXPRESSION";
		public const string SecondsMustNotBeNegative = "#-1 SECONDS MUST NOT BE NEGATIVE";
		public const string SingleCharArgument = "#-1 ARGUMENT MUST BE A SINGLE CHARACTER";
		public const string SqlNotEnabled = "#-1 SQL IS NOT ENABLED";
		public const string StringLengthsMustBeEqual = "#-1 STRING LENGTHS MUST BE EQUAL";
		public const string ThisIsARoom = "#-1 THIS IS A ROOM";
		public const string TooManyColumnsForAlign = "#-1 TOO MANY COLUMNS FOR ALIGN";
		public const string TooManyWords = "#-1 TOO MANY WORDS";
		public const string UseTagwrapInstead = "#-1 USE TAGWRAP INSTEAD";
		public const string VectorsMustBe3D = "#-1 VECTORS MUST BE 3-DIMENSIONAL";
		public const string VectorsMustMatchDimensions = "#-1 VECTORS MUST BE SAME DIMENSIONS";
		public const string WidthMustBeANumber = "#-1 WIDTH MUST BE A NUMBER";
		public const string WordNumberOutOfRange = "#-1 WORD NUMBER OUT OF RANGE";
		public const string WouldCreateLoop = "#-1 WOULD CREATE LOOP";
		public const string ZoneLoop = "#-1 ZONE LOOP DETECTED";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BaseArgRange = "#-1 ARGUMENT {0} MUST BE BETWEEN 2 AND 64";

		public const string AhelpSystemNotInitialized = "#-1 AHELP SYSTEM NOT INITIALIZED";
		public const string AllGuestsInUse = "#-1 ALL GUESTS IN USE";
		public const string AlreadyConnected = "#-1 ALREADY CONNECTED";
		public const string BadArgumentFormatToSet = "#-1 BAD ARGUMENT FORMAT TO @SET";
		public const string BadArgumentsToMailCommand = "#-1 BAD ARGUMENTS TO MAIL COMMAND";
		public const string BadArgumentsToWikiCommand = "#-1 BAD ARGUMENTS TO WIKI COMMAND";

		/// <summary>
		/// A wiki write lost a race with another writer. Distinct from
		/// <see cref="BadArgumentsToWikiCommand"/> on purpose: the request was well-formed and the answer
		/// is "read what landed and re-apply", not "fix your syntax". Softcode has to be able to tell those
		/// apart, and it is never a signal to retry.
		/// </summary>
		public const string WikiWriteConflict = "#-1 WIKI WRITE CONFLICT";
		public const string CannotSetContentLengthHeader = "#-1 CANNOT SET CONTENT-LENGTH HEADER";
		public const string CommandNotFound = "#-1 COMMAND NOT FOUND";
		public const string ContentTypeCannotBeEmpty = "#-1 CONTENT-TYPE CANNOT BE EMPTY";
		public const string CopyFailed = "#-1 COPY FAILED";
		public const string CreateFailed = "#-1 CREATE FAILED";
		public const string Failed = "#-1 FAILED";
		public const string FunctionNotFound = "#-1 FUNCTION NOT FOUND";
		public const string GuestLoginsDisabled = "#-1 GUEST LOGINS DISABLED";
		public const string GuestSelectionFailed = "#-1 GUEST SELECTION FAILED";
		public const string GuestsCannotModifyChannels = "#-1 GUESTS MAY NOT MODIFY CHANNELS";
		public const string HeaderNameCannotBeEmpty = "#-1 HEADER NAME CANNOT BE EMPTY";
		public const string HeaderRequired = "#-1 HEADER REQUIRED";
		public const string HelpSystemNotInitialized = "#-1 HELP SYSTEM NOT INITIALIZED";
		public const string Huh = "#-1 HUH";
		public const string InvalidCombination = "#-1 INVALID COMBINATION";
		public const string InvalidLockType = "#-1 INVALID LOCK TYPE";
		public const string InvalidNumber = "#-1 INVALID NUMBER";
		public const string InvalidOption = "#-1 INVALID OPTION";
		public const string InvalidPath = "#-1 INVALID PATH";
		public const string InvalidPattern = "#-1 INVALID PATTERN";
		public const string InvalidQregPairs = "#-1 INVALID QREG PAIRS";
		public const string InvalidRegexp = "#-1 INVALID REGEXP";
		public const string InvalidSemaphoreAttribute = "#-1 INVALID SEMAPHORE ATTRIBUTE";
		public const string InvalidSource = "#-1 INVALID SOURCE";
		public const string InvalidTarget = "#-1 INVALID TARGET";
		public const string InvalidTime = "#-1 INVALID TIME";
		public const string InvalidUriFormat = "#-1 INVALID URI FORMAT";
		public const string LibraryUnavailable = "#-1 LIBRARY UNAVAILABLE";
		public const string MailNotFound = "#-1 MAIL NOT FOUND";
		public const string MaxGuestsReached = "#-1 MAX GUESTS REACHED";
		public const string MissingCommandListArgument = "#-1 MISSING COMMAND LIST ARGUMENT";
		public const string MissingQregAssignments = "#-1 MISSING QREG ASSIGNMENTS";
		public const string MoveFailed = "#-1 MOVE FAILED";
		public const string NewsSystemNotInitialized = "#-1 NEWS SYSTEM NOT INITIALIZED";
		public const string NoAliasSpecified = "#-1 NO ALIAS SPECIFIED";
		public const string NoAttribute = "#-1 NO ATTRIBUTE";
		public const string NoAttributeSpecified = "#-1 NO ATTRIBUTE SPECIFIED";
		public const string NoCloneNameSpecified = "#-1 NO CLONE NAME SPECIFIED";
		public const string NoCommandSpecified = "#-1 NO COMMAND SPECIFIED";
		public const string NoFlagsSpecified = "#-1 NO FLAGS SPECIFIED";
		public const string NoFunctionSpecified = "#-1 NO FUNCTION SPECIFIED";
		public const string NoGuestCharacters = "#-1 NO GUEST CHARACTERS";
		public const string NoHook = "#-1 NO HOOK";
		public const string NoHookType = "#-1 NO HOOK TYPE";
		public const string NoMatchString = "#-1 NO MATCH STRING";
		public const string NoNewNameSpecified = "#-1 NO NEW NAME SPECIFIED";
		public const string NoObject = "#-1 NO OBJECT";
		public const string NoObjectSpecified = "#-1 NO OBJECT SPECIFIED";
		public const string NoPidSpecified = "#-1 NO PID SPECIFIED";
		public const string NoPlayerSpecified = "#-1 NO PLAYER SPECIFIED";
		public const string NoRoomNameSpecified = "#-1 NO ROOM NAME SPECIFIED";
		public const string NoTargetSpecified = "#-1 NO TARGET SPECIFIED";
		public const string NoTestString = "#-1 NO TEST STRING";
		public const string NoWaitingTask = "#-1 NO WAITING TASK";
		public const string NotFound = "#-1 NOT FOUND";
		public const string NotImplemented = "#-1 NOT IMPLEMENTED";
		public const string ObjectNotFound = "#-1 OBJECT NOT FOUND";
		public const string PatternTimeout = "#-1 PATTERN TIMEOUT";
		public const string PlayerNotFound = "#-1 PLAYER NOT FOUND";
		public const string RegexpTimeout = "#-1 REGEXP TIMEOUT";
		public const string RetryNoCommandToRetry = "#-1 RETRY: NO COMMAND TO RETRY";
		public const string RetryNoConditionProvided = "#-1 RETRY: NO CONDITION PROVIDED";
		public const string Safe = "#-1 SAFE";
		public const string StatusCodeMustBe3Digit = "#-1 STATUS CODE MUST BE A 3-DIGIT NUMBER";
		public const string StatusCodeRequired = "#-1 STATUS CODE REQUIRED";
		public const string StatusLineTooLong = "#-1 STATUS LINE MUST BE LESS THAN 40 CHARACTERS";
		/// <summary>
		/// PennMUSH's refusal when the executor has no standing to ask about either argument —
		/// <c>fun_nearby</c> (src/fundb.c:896) and <c>fun_zwho</c>/<c>fun_lwho</c>'s neighbours.
		/// </summary>
		public const string NoObjectsControlled = "#-1 NO OBJECTS CONTROLLED";

		/// <summary>The bare failure Penn writes with <c>safe_str("#-1", ...)</c>, with no reason.</summary>
		public const string Nothing = "#-1";

		public const string Unfindable = "#-1 UNFINDABLE";
		public const string UnknownFlag = "#-1 UNKNOWN FLAG";
		public const string YouCannotModifyThisChannel = "#-1 YOU CANNOT MODIFY THIS CHANNEL";
		public const string ChannelAlreadyExists = "#-1 CHANNEL ALREADY EXISTS";
		public const string ChannelCreationFailed = "#-1 CHANNEL COULD NOT BE CREATED";
		public const string ChannelNotFound = "#-1 CHANNEL NOT FOUND";
		public const string InvalidChannelName = "#-1 INVALID CHANNEL NAME";
		public const string InvalidPrivileges = "#-1 INVALID PRIVILEGES";
		public const string TooManyChannels = "#-1 TOO MANY CHANNELS";
		public const string NoSuchChannel = "#-1 NO SUCH CHANNEL";
		public const string NotOnChannel = "#-1 NOT ON CHANNEL";
		public const string NoTextGiven = "#-1 NO TEXT GIVEN";
		public const string NoSuchLockType = "#-1 NO SUCH LOCK TYPE";
		public const string ChannelPermissionDenied = "#-1 CHANNEL PERMISSION DENIED";
		public const string GetRequestsCannotHaveBody = "#-1 GET REQUESTS CANNOT HAVE A BODY";
		public const string CannotRenameInbox = "#-1 CANNOT RENAME THE INBOX FOLDER";
		public const string InvalidMailArguments = "#-1 INVALID ARGUMENTS FOR MAIL COMMAND";
		public const string InvalidMailFolderArguments = "#-1 INVALID ARGUMENTS FOR MAIL FOLDER COMMAND";
		public const string AliasCannotBeEmpty = "#-1 ALIAS NAME CANNOT BE EMPTY";
		public const string UsageAddcom = "#-1 USAGE: ADDCOM <ALIAS>=<CHANNEL>";
		public const string UsageDelcom = "#-1 USAGE: DELCOM <ALIAS>";
		public const string UsageComtitle = "#-1 USAGE: COMTITLE <ALIAS>=<TITLE>";

		// Function-family failures (#965): one home for the literals the Functions partials returned inline.
		public const string NoSuchLock = "#-1 NO SUCH LOCK";
		public const string InvalidBoolexp = "#-1 INVALID BOOLEXP";
		/// <summary><c>fun_atrlock</c>'s own wording (<c>src/fundb.c:2437,2441</c>).</summary>
		public const string ArgumentMustBeObjectAttribute = "#-1 ARGUMENT MUST BE OBJ/ATTR";
		public const string InvalidHashType = "#-1 INVALID HASH TYPE";
		public const string UnknownWikiField = "#-1 UNKNOWN WIKI FIELD";
		public const string NoSuchWikiNamespace = "#-1 NO SUCH WIKI NAMESPACE";
		public const string NoFunctionNameGiven = "#-1 FUNCTION (No function name given)";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InvalidRegisterTypeFormat = "#-1 R: INVALID REGISTER TYPE '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string JsonSetFailedFormat = "#-1 SET FAILED: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string JsonReplaceFailedFormat = "#-1 REPLACE FAILED: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string JsonRemoveFailedFormat = "#-1 REMOVE FAILED: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DuplicateKeysFormat = "#-1 DUPLICATE KEYS: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MarkdownRenderErrorFormat = "#-1 ERROR RENDERING MARKDOWN: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SqlErrorFormat = "#-1 SQL ERROR: {0}";

		/// <summary>A failure whose reason is supplied by the caller, rendered as <c>#-1 REASON</c>.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ReasonFormat = "#-1 {0}";
		public const string UpdateToWhat = "#-1 UPDATE TO WHAT?";
		public const string QueryWhere = "#-1 QUERY WHERE?";
		public const string WhatDoYouWantToQuery = "#-1 WHAT DO YOU WANT TO QUERY?";
		public const string NotGoing = "#-1 OBJECT NOT MARKED FOR DESTRUCTION";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InternalErrorFormat = "#-1 INTERNAL SHARPMUSH ERROR:\n{0}";

		/// <summary>
		/// A command threw. <c>{0}</c> is a single-line JSON object built by
		/// <see cref="ExceptionReport"/>; see that type for the disclosure rule that decides which
		/// fields a given recipient gets. Never build this string by hand — the whole point of the
		/// builder is that the mortal payload is an allowlist rather than a scrubbed dump.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ExceptionFormat = "#-1 EXCEPTION: {0}";
	}

	/// <summary>
	/// User-friendly notification messages for player notifications.
	/// These are more natural English and SHOULD be translated for i18n.
	/// Found in actual Notify calls across the codebase.
	/// </summary>
	public static partial class Notifications
	{
		public const string LocalFunctionMessage = "{0}";
		public const string LocalFunctionHeader = "Owner-local functions (call with localfun):";
		public const string LocalFunctionReset = "Removed {0} unpreserved owner-local functions.";
		public const string LocalFunctionChanged = "Owner-local function {0}: {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AccountClosedFormat = "Account '{0}' closed; active sessions revoked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AccountMarkedDeletedFormat = "Account '{0}' marked deleted; active sessions revoked. The account record is retained.";
		public const string InvalidNameThing = "Invalid name for a thing.";
		public const string NoMatch = "I don't see that here.";
		public const string CantSeeThat = "I can't see that here.";
		public const string CouldNotFind = "Could not find that.";
		public const string CantFindThatPlayer = "I can't find that player";
		// match.c:481 — with the exclamation mark. The neighbouring two are already exact.
		public const string AmbiguousMatch = "I don't know which one you mean!";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LocateUnknownSwitchFormat = "I don't understand switch '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontKnowWhichYouMean = "I don't know which {0} you mean!";
		public const string DontSeeWhatYouWantToLock = "I don't see what you want to lock!";
		public const string DontKnowWhichOneToLock = "I don't know which one you want to lock!";
		public const string DontSeeThatHere = "I don't see that here.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontSeeThatHereFormat = "I don't see {0} here.";
		public const string DontKnowWhoYouMean = "I don't know who you mean!";

		public const string MustBePlayer = "New owner must be a player.";
		public const string InvalidDestinationExit = "Invalid destination for exit.";
		public const string HomeMustBeRoom = "Home must be a room.";
		public const string HomeIsAnExit = "That is an exit.";
		public const string CannotLinkToItself = "You may not link something to itself.";
		public const string DropToMustBeRoom = "Drop-to must be a room.";
		public const string InvalidObjectTypeForLinking = "Invalid object type for linking.";
		public const string InvalidObjectTypeGeneric = "Invalid object type.";
		public const string CannotClonePlayers = "You cannot clone players.";
		/// <summary>PennMUSH <c>do_clone</c> (<c>src/create.c:712</c>): /PRESERVE is wizard-only, and refused rather than downgraded.</summary>
		public const string ClonePreserveWizardOnly = "You cannot @CLONE/PRESERVE. Use normal @CLONE instead.";
		/// <summary>PennMUSH <c>clone_object</c> (<c>src/create.c:653-654</c>), on what /PRESERVE actually carried across.</summary>
		public const string ClonePreserveCarriedPrivileges = "Warning: @CLONE/PRESERVE on an object with WIZ, ROY, @powers, or @warnings.";
		public const string CannotCloneThisObjectType = "Cannot clone this object type.";
		public const string NotMarkedForDestruction = "That object is not marked for destruction.";

		public const string PermissionDenied = "Permission denied.";
		public const string NoPermission = "You don't have permission to do that.";
		public const string YouDoNotControlThatObject = "You do not control that object.";

		public const string EmptyLine = "";
		public const string CantLinkToThat = "You can't link to that.";
		public const string DontPassLinkLock = "You don't pass the link lock.";
		public const string FailedToTransferOwnership = "Failed to transfer ownership.";
		public const string PermissionDeniedCannotZoneTo = "Permission denied: You cannot zone to that object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PermissionDeniedSetAttribute = "Permission denied to set attribute on {0}.";
		public const string LackSpoofingPermissions = "Permission denied: You lack spoofing permissions.";
		public const string NoPermissionToChown = "You don't have the permission to chown that.";
		public const string CantRemakeWorld = "You can't remake the world in your image.";
		public const string CannotDoWhileGagged = "You cannot do that while gagged.";
		public const string CantTeleportToNothing = "You can't teleport to nothing!";
		public const string HavenFlagSet = "You are set HAVEN and cannot receive pages.";

		public const string InvalidArgument = "Invalid argument.";
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

		public const string RecursionLimit = "That caused too much recursion.";

		/// <summary>
		/// <c>do_create</c>'s confirmation (<c>src/create.c:604</c>):
		/// <c>notify_format(player, T("Created: Object %s."), unparse_dbref(thing))</c>. The argument is
		/// <c>unparse_dbref</c>'s bare <c>#N</c> — no name, and no creation time.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CreatedObject = "Created: Object {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string UnableToWipeAttribute = "Unable to wipe attribute {0}.";
		public const string NoAttributesWiped = "No attributes wiped.";
		public const string OneAttributeWiped = "One attribute wiped.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributesWipedCount = "{0} attributes wiped.";
		public const string CouldNotFindNewOwner = "Could not find new owner.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CouldNotFindDestination = "Could not find destination: {0}";

		public const string DontRecognizeThatChannel = "CHAT: I don't recognize that channel.";
		public const string DontKnowWhichChannel = "CHAT: I don't know which channel you mean.";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontThinkWantsToHearFrom = "I don't think #{0} wants to hear from {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontThinkWantsMail = "I don't think #{0} wants {1}'s mail.";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NotEnoughMoneyToLink = "You don't have enough {0} to link.";
		public const string CantBuyThingsByTakingMoney = "You can't buy things by taking money.";

		public const string DontLookLikeGod = "You don't look like God.";
		public const string NotEnoughMagic = "You don't have enough magic for that.";
		public const string NotAnAdmin = "You don't look like an admin to me.";
		public const string CantAliasCommandToThat = "I can't alias a command to that!";
		public const string CantMakeMultipleRequests = "You can't make multiple requests at the same time!";

		// --- Destruction edge-case notifications (PennMUSH src/destroy.c) ---
		public const string GuestCantDestroy = "I'm sorry, Dave, I'm afraid I can't do that.";
		/// <summary>PennMUSH <c>can_pay_fees</c> (<c>src/predicat.c:439</c>): a guest may not build at all.</summary>
		public const string GuestCantBuild = "Sorry, you aren't allowed to build.";
		/// <summary>PennMUSH <c>can_pay_fees</c> (<c>src/predicat.c:455</c>) when <c>pay_quota</c> refuses.</summary>
		public const string BuildingQuotaExhausted = "Sorry, your building quota has run out.";
		public const string DestroyGodBlasphemous = "Destroying God would be blasphemous.";
		public const string TooSpecialToDestroy = "That is too special to be destroyed.";
		public const string FloorDisappearsNothingness =
			"The floor disappears under your feet, you fall through NOTHINGness and then:";

		// --- Movement default notifications (PennMUSH src/move.c) ---
		// Movement's room defaults remain literal until the broadcast path can carry resource keys.
		public const string DefaultOLeave = "has left.";
		public const string DefaultOEnter = "has arrived.";
		public const string HomeNoPlaceLikeHome = "There's no place like home...";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HomeGoesHome = "{0} goes home.";
		public const string AmbiguousExitDirection = "I don't know which way you mean!";

		// --- Speech lock enforcement (PennMUSH src/speech.c) ---
		public const string MayNotSpeakHere = "You may not speak here!";

		/// <summary>
		/// The Speech-lock refusal for a room the speaker is not standing in — what @remit and
		/// @lemit report, against MayNotSpeakHere for the speaker's own room
		/// (PennMUSH src/speech.c, do_one_remit and do_lemit).
		/// </summary>
		public const string MayNotSpeakThere = "You may not speak there!";
		public const string OemitTooManyRecipients = "Too many people to oemit to.";
		public const string NoMatchingObjects = "No matching objects.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InvalidPortNumber = "'{0}' is not a port number.";
		public const string PortNotActive = "That port is not active.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PemitTargetWishesAlone = "I'm sorry, but {0} wishes to be left alone now.";

		// --- @force guardrails (PennMUSH src/wiz.c) ---
		public const string CantForceGod = "You can't force God!";

		// --- Admin / Wizard guardrails (PennMUSH src/wiz.c, src/flags.c) ---
		public const string CantBootOtherPeople = "You can't boot other people!";
		public const string CantTeleportRooms = "You can't teleport rooms.";

		/// <summary>
		/// PennMUSH <c>src/wiz.c:440</c>: the destination is inside the thing being moved, or is the
		/// thing itself. Refused before the move is announced anywhere.
		/// </summary>
		public const string BadDestination = "Bad destination.";

		/// <summary>PennMUSH <c>src/wiz.c:588</c>: told to the teleporter, not to the teleported.</summary>
		public const string Teleported = "Teleported.";
		public const string TeleportsNotAllowed = "Teleports are not allowed in this room.";
		public const string NoZoneTeleport = "You may not teleport out of the zone from this room.";

		/// <summary>
		/// PennMUSH <c>src/wiz.c:453</c>: teleporting an exit relocates its source, and only a room
		/// can source an exit.
		/// </summary>
		public const string ExitsOnlyTeleportToRooms = "Exits can only be teleported to other rooms.";

		/// <summary>PennMUSH <c>src/wiz.c:456</c>: the would-be new source room is already GOING.</summary>
		public const string ExitDestinationCrumbling = "You can't move an exit to someplace that's crumbling.";
		public const string InTheVoid = "You're in the Void. This is not a good thing.";
		public const string VoidSendingHome = "You're in the void - sending you home.";
		public const string TooManyContainers = "You're in too many containers.";
		public const string CantGrantPowersUnregistered = "You can't grant powers to unregistered players.";
		public const string CantMakeAdminGuests = "You can't make admin into guests.";
		public const string WhoDoYouThinkYouAre = "Who do you think you are, GOD?";
		public const string NoPowerOverBodyAndMind = "You do not have the power over body and mind!";


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

		// --- get / drop / give / use triad and lock defaults (src/move.c, src/rob.c, src/set.c) ---
		// Literal text on the same terms as the movement defaults above: they reach the game through
		// DidItRequest.Def / DidItRequest.ODef, which have no per-recipient key seam.
		/// <summary>PennMUSH <c>src/move.c:736</c>: the object's drop lock refused.</summary>
		public const string CantSeemToGetRidOfThat = "You can't seem to get rid of that.";
		/// <summary>PennMUSH <c>src/move.c:671</c>: the source container's take lock refused.</summary>
		public const string CantTakeThatFromThere = "You can't take that from there.";
		/// <summary>PennMUSH <c>src/move.c:689</c>: the object's own basic lock refused.</summary>
		public const string CantPickThatUp = "You can't pick that up.";
		/// <summary>PennMUSH <c>src/rob.c:323</c>: the object's give lock refused.</summary>
		public const string CantGiveThatAway = "You can't give that away.";
		/// <summary>
		/// PennMUSH <c>src/rob.c:287</c>, <c>:296</c> and <c>:538</c>: <c>give</c> named no recipient, or
		/// named one <c>match_result</c> did not find. The same line answers both, which is why a failed
		/// recipient match cannot go through the notifying locate — that one says "I can't see that here."
		/// </summary>
		public const string GiveToWhom = "Give to whom?";
		/// <summary>
		/// PennMUSH <c>src/rob.c:309</c>: the gift name matched more than one thing the giver carries.
		/// <c>rob.c</c> is the only place this bare wording appears — every other ambiguity report names
		/// what it was looking for (<see cref="DontKnowWhichYouMean"/>) or says "one"
		/// (<see cref="AmbiguousMatch"/>).
		/// </summary>
		public const string DontKnowWhichYouMeanBare = "I don't know which you mean!";
		/// <summary>PennMUSH <c>src/rob.c:329</c>: the recipient's from lock refused.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DoesntWantAnythingFromYou = "{0} doesn't want anything from you.";
		/// <summary>PennMUSH <c>src/rob.c:335</c>: the recipient's receive lock refused the object.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DoesntWantThat = "{0} doesn't want that.";
		/// <summary>PennMUSH <c>src/move.c:632</c>, the possessive-get actor message.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouTakeFrom = "You take {0} from {1}.";
		/// <summary>PennMUSH <c>src/move.c:635</c>, the possessive-get o-message, name-prefixed.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TakesFrom = "takes {0} from {1}.";
		/// <summary>PennMUSH <c>src/move.c:682</c>, the plain-get actor message.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouTake = "You take {0}.";
		/// <summary>PennMUSH <c>src/move.c:686</c>, the plain-get o-message, name-prefixed.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string Takes = "takes {0}.";
		/// <summary>PennMUSH <c>src/move.c:628</c> and <c>:678</c>: what the object itself is told.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TookYou = "{0} took you.";
		/// <summary>PennMUSH <c>src/move.c:627</c>: what the robbed container is told.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WasTakenFromYou = "{0} was taken from you.";
		/// <summary>PennMUSH <c>src/move.c:763</c>, the drop actor message.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouDrop = "You drop {0}.";
		/// <summary>PennMUSH <c>src/move.c:766</c>, the drop o-message, name-prefixed.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string Drops = "drops {0}.";
		/// <summary>PennMUSH <c>src/move.c:752</c> and <c>:757</c>: what the dropped object is told.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DropsYou = "{0} drops you.";
		/// <summary>PennMUSH <c>src/move.c:749</c>: what a STICKY object sent home is told instead.</summary>
		public const string Dropped = "Dropped.";
		/// <summary>PennMUSH <c>src/move.c:906</c>: what EMPTY tells the emptier when one item moved.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RemovedOneObjectFrom = "You remove 1 object from {0}.";
		/// <summary>PennMUSH <c>src/move.c:909</c>: the same, for any other count.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RemovedObjectsFrom = "You remove {0} objects from {1}.";
		/// <summary>PennMUSH <c>src/rob.c:412</c>, the give actor message.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouGaveTo = "You gave {0} to {1}.";
		/// <summary>PennMUSH <c>src/rob.c:420</c>: what the given object is told.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GaveYouTo = "{0} gave you to {1}.";
		/// <summary>PennMUSH <c>src/rob.c:427</c>, the recipient's receive message.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GaveYou = "{0} gave you {1}.";
		/// <summary>PennMUSH <c>src/set.c:1417</c>: the use triad's actor default.</summary>
		public const string Used = "Used.";
		/// <summary>PennMUSH <c>src/speech.c:945</c>: the target's page lock refused the pager.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NotAcceptingYourPages = "{0} is not accepting your pages.";
		/// <summary>PennMUSH <c>src/speech.c:939</c>: the target is HAVEN, so it refuses every pager.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NotAcceptingAnyPages = "{0} is not accepting any pages.";
		/// <summary>PennMUSH <c>src/speech.c:930</c>: the named player holds no connection.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NotConnected = "{0} is not connected.";
		/// <summary>PennMUSH <c>src/speech.c:912</c>: the name named no player at all.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotFindWhoToPage = "I can't find who you're trying to page with: {0}";
		/// <summary>PennMUSH <c>src/speech.c:918</c>: the name fit more than one connected player.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NotSureWhoToPage = "I'm not sure who you want to page with: {0}";
		/// <summary>PennMUSH <c>src/speech.c:981</c>: every name the scan could not page, space-separated.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string UnableToPage = "Unable to page: {0}";

		// --- page/recall and page/conversations: the page log, a SharpMUSH extension (PennMUSH keeps none) ---
		/// <summary><c>page/recall</c> or <c>page/conversations</c> while <c>page_log</c> is off.</summary>
		public const string NoPageLog = "This game keeps no page log.";
		/// <summary><c>page/recall</c>: the name matched no player, as <c>page</c> matches them.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotFindWhoYouPaged = "I can't find who you paged with: {0}";
		/// <summary><c>page/recall</c>: the name fit more than one connected player.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NotSureWhoYouPaged = "I'm not sure who you paged with: {0}";
		/// <summary><c>page/recall</c>'s header for one conversation; {0} names the others in it.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PageRecallWith = "PAGE: Recall of your pages with {0}:";
		/// <summary><c>page/recall</c>'s header for the latest pages across every conversation.</summary>
		public const string PageRecallLatest = "PAGE: Recall of your pages:";
		/// <summary><c>page/recall</c>'s footer.</summary>
		public const string PageRecallEnd = "PAGE: End recall";
		/// <summary><c>page/recall</c> of a conversation with nothing logged in it.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NoLoggedPagesWith = "You have no logged pages with {0}.";
		/// <summary><c>page/recall</c> with nothing logged at all.</summary>
		public const string NoLoggedPages = "You have no logged pages.";
		/// <summary><c>page/conversations</c> with nothing logged.</summary>
		public const string NoLoggedPageConversations = "You have no logged page conversations.";
		/// <summary><c>page/conversations</c>' header.</summary>
		public const string PageConversationsHeader = "PAGE: Your page conversations, latest first:";
		/// <summary><c>page/conversations</c>' footer.</summary>
		public const string PageConversationsEnd = "PAGE: End of list";
		/// <summary><c>page/timestamps</c> without <c>/recall</c>.</summary>
		public const string PageTimestampsNeedsRecall = "PAGE: /timestamps goes with /recall.";

		// --- Destruction SAFE messages aligned with PennMUSH src/destroy.c ---
		/// <summary>PennMUSH: when object is SAFE and REALLY_SAFE is true (strict mode).</summary>
		public const string SafeObjectMustUnset = "That object is set SAFE. You must set it !SAFE before destroying it.";
		/// <summary>PennMUSH: when object is marked SAFE (standard mode).</summary>
		public const string SafeObjectUseNuke = "That object is marked SAFE. Use @nuke to destroy it.";
		/// <summary>PennMUSH do_destroy: @nuke of a SAFE object when REALLY_SAFE is off.</summary>
		public const string SafeTargetScheduledAnyway = "Warning: Target is set SAFE, but scheduling for destruction anyway.";
		/// <summary>PennMUSH what_to_destroy: plain @destroy of an object the actor does not own.</summary>
		public const string NotYoursUseNuke = "That object does not belong to you. Use @nuke to destroy it.";
		/// <summary>PennMUSH what_to_destroy: plain @destroy of a WIZARD thing.</summary>
		public const string WizardThingUseNuke = "That object is set WIZARD. You must use @nuke to destroy it.";
		/// <summary>PennMUSH what_to_destroy: a player destroyed by anything but a player.</summary>
		public const string ProgramsDontKillPeople = "Programs don't kill people; people kill people!";

		// --- Zone messages aligned with PennMUSH src/set.c ---
		/// <summary>
		/// <c>do_chzone</c>'s only success report (<c>src/set.c:487</c>) — it says the same thing when the
		/// zone is cleared, which is why there is no separate "Zone cleared." here.
		/// </summary>
		public const string ZoneChanged = "Zone changed.";
		/// <summary>PennMUSH <c>do_chown</c> (<c>src/set.c:238</c>): to the enactor, after every successful <c>@chown</c>; QUIET does not suppress it.</summary>
		public const string OwnerChanged = "Owner changed.";
		public const string CantMakeCircularZones = "You can't make circular zones!";
		/// <summary><c>do_chzone</c>'s no-op guard (<c>src/set.c:394</c>).</summary>
		public const string ObjectAlreadyInThatZone = "That object is already in that zone.";
		/// <summary><c>do_chzone</c>'s <c>controls(player, thing)</c> refusal (<c>src/set.c:400</c>).</summary>
		public const string NoPowerToShiftReality = "You don't have the power to shift reality.";
		/// <summary><c>do_chzone</c>'s self-zone guard, mortals only (<c>src/set.c:423</c>).</summary>
		public const string CantZoneObjectsToThemselves = "You shouldn't zone objects to themselves!";
		/// <summary><c>do_chzoneall</c>'s empty right-hand side (<c>src/wiz.c:1033</c>).</summary>
		public const string NoZoneSpecified = "No zone specified.";
		/// <summary><c>do_chzoneall</c>'s summary (<c>src/wiz.c:1055</c>).</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ZoneChangedForObjectsFormat = "Zone changed for {0} objects.";
		/// <summary>
		/// <c>check_zone_lock</c>'s report of the <c>=me</c> Zone lock it just installed
		/// (<c>src/lock.c:968-971</c>). No full stop: PennMUSH's string has none.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ZoneAutomaticallyLockedFormat =
			"Unlocked zone {0} - automatically zone-locking to itself";
		/// <summary>
		/// <c>check_zone_lock</c>'s trivial-lock advisory (<c>src/lock.c:977-981</c>): the zone's Zone lock
		/// admits the player start room and the master room as readily as the player's own location, so it
		/// is gating on nothing.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ZoneShouldHaveMoreSecureLockFormat =
			"Zone {0} really should have a more secure zone-lock.";
		/// <summary>
		/// <c>check_zone_lock</c>'s loose-lock advisory (<c>src/lock.c:983-987</c>): the lock passes for the
		/// player's location, which is what <c>@lock/zone &lt;zone&gt;=player</c> rather than
		/// <c>=player</c> does.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ZoneMayHaveLooseLockFormat =
			"Warning: Zone {0} may have loose zone lock. Lock zones to =player, not player";
		/// <summary><c>do_chzone</c>'s admin-owned warning (<c>src/set.c:453-456</c>).</summary>
		public const string ChzoningAdminOwnedObject = "Warning: @chzoning admin-owned object!";
		/// <summary><c>do_chzone</c>'s warning for a target that keeps its privileges (<c>src/set.c:479</c>).</summary>
		public const string ChzoningPrivilegedPlayer = "Warning: @chzoning a privileged player.";
		/// <summary><c>do_chzone</c>'s warning for a target that keeps TRUST (<c>src/set.c:481</c>).</summary>
		public const string ChzoningTrustPlayer = "Warning: @chzoning a TRUST player.";

		// --- Lock/Unlock messages aligned with PennMUSH src/lock.c ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectLocked = "{0}(#{1}) - {2} locked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectUnlocked = "{0}(#{1}) - {2} unlocked.";
		/// <summary>PennMUSH src/lock.c:676.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectAlreadyUnlocked = "{0}(#{1}) - {2} (already) unlocked.";
		/// <summary>PennMUSH src/lock.c:913, do_lset.</summary>
		public const string NoLockNameGiven = "No lock name given.";
		/// <summary>PennMUSH src/lock.c:949, do_lset.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LockFlagsSet = "{0}/{1} - lock flags set.";
		/// <summary>PennMUSH src/lock.c:949, do_lset.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LockFlagsUnset = "{0}/{1} - lock flags unset.";
		/// <summary>PennMUSH src/lock.c:726, do_lock: the object matched but the locker does not control it.</summary>
		public const string CantLockThat = "You can't lock that!";
		/// <summary>PennMUSH src/lock.c:633, check_lock_type.</summary>
		public const string UnknownLockType = "Unknown lock type.";
		/// <summary>PennMUSH src/lock.c:646, check_lock_type.</summary>
		public const string InvalidLockName = "That is not a valid lock name.";
		/// <summary>PennMUSH src/lock.c:638, check_lock_type.</summary>
		public const string LockNameHasPipe = "The character '|' may not be used in lock names.";
		/// <summary>PennMUSH src/lock.c:928, do_lset.</summary>
		public const string UnrecognizedLockFlag = "Unrecognized lock flag.";
		/// <summary>PennMUSH src/lock.c:934, do_lset.</summary>
		public const string NoSuchLock = "No such lock.";

		// --- Link/Unlink messages aligned with PennMUSH src/create.c ---
		/// <summary>
		/// <c>do_real_open</c>'s link report (<c>src/create.c:175</c>), which prints both dbrefs bare —
		/// <c>@open</c>, <c>open()</c> and the exits <c>@dig</c> makes.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LinkedExitToRoom = "Linked exit #{0} to #{1}";
		/// <summary>
		/// <c>do_link</c>'s exit report (<c>src/create.c:385</c>): the destination through
		/// <c>unparse_object</c>, so <c>*HOME*</c> and <c>*VARIABLE*</c> for the two keywords.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LinkedExitToObject = "Linked exit #{0} to {1}";
		/// <summary><c>do_unlink</c> (<c>src/create.c:275-276</c>): the old destination through <c>unparse_object</c>.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string UnlinkedExit = "Unlinked exit #{0} (Used to lead to {1}).";
		/// <summary><c>do_open</c>'s source-room refusal (<c>src/create.c:214</c>).</summary>
		public const string OpenFromWhere = "Open from where?";
		/// <summary><c>parse_linkable_room</c> (<c>src/create.c:56</c>): not here, home or a dbref.</summary>
		public const string NotAValidObject = "That is not a valid object.";
		/// <summary><c>parse_linkable_room</c> (<c>src/create.c:59</c>).</summary>
		public const string RoomBeingDestroyed = "That room is being destroyed. Sorry.";
		/// <summary>
		/// <c>do_unlink</c>'s <c>NOTHING</c> arm (<c>src/create.c:261</c>). Its match is silent, so a name
		/// that resolves to nothing — including one dropped because a mortal does not control it — reports
		/// this rather than the locator's "I can't see that here."
		/// </summary>
		public const string UnlinkWhat = "Unlink what?";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string OpenedExit = "Opened exit {0}";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WelcomeBackFormat = "Welcome back, {0}!";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WelcomeFirstLoginFormat = "Welcome, {0}! This is your first time connecting.";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotNameObjectFormat = "You cannot name that object {0}.";
		public const string InvalidPasswordText = "That password is not a valid password.";
		public const string InvalidPasswordForCommand = "Invalid password.";
		public const string OnlyPlayersHavePasswords = "Only players have passwords.";

		// PennMUSH do_parent (src/set.c:1432-1446) notifies distinct text for each of its three
		// guards - verbatim, including punctuation:
		public const string SelfAncestor = "A thing cannot be its own ancestor!";
		public const string CyclicAncestor = "You are not allowed to be your own ancestor!";
		public const string TooManyAncestors = "Too many ancestors.";
		public const string ParentSet = "Parent changed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ClearedPowersFromFormat = "Cleared {0} power(s) from {1}.";

		public const string NeedObjectAttributePair = "You need to give an object/attribute pair.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InvalidArgumentsToCommandFormat = "Invalid arguments to {0}.";
		public const string InvalidSourceFormat = "Invalid source format. Use: object/attribute";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InvalidDestinationFormat = "Invalid destination format: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToCopyAttributeToFormat = "Failed to copy attribute to {0}: {1}";
		public const string FailedToCopyAttributeAny = "Failed to copy attribute to any destinations.";
		public const string FailedToMoveAttributeAny = "Failed to move attribute to any destinations.";
		public const string CanOnlyChownToYourself = "You can only chown an attribute to yourself.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToChangeOwnershipFormat = "Failed to change ownership: {0}";
		public const string WipeWhat = "Wipe what?";
		public const string ObjectIsProtectedSafe = "That object is protected (SAFE).";

		public const string Destroyed = "Destroyed.";
		public const string HomeSet = "Home set.";
		/// <summary><c>do_name</c>'s confirmation (<c>src/set.c:154</c>), gated on <c>AreQuiet</c>.</summary>
		public const string NameSet = "Name set.";
		public const string DropToSet = "Dropto set.";
		public const string DropToRemoved = "Dropto removed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SparedFromDestructionFormat = "Spared from destruction: {0}";
		/// <summary>
		/// <c>do_real_open</c>'s first refusal (<c>src/create.c:108-110</c>): an exit is sourced in a
		/// room or nowhere. Reached by <c>@open</c> with a source that is not a room, and by cloning an
		/// exit while standing somewhere that is not one.
		/// </summary>
		public const string ExitsOnlyFromRooms = "You can only make exits out of rooms.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LinkedToNameFormat = "Linked to {0}.";
		/// <summary>
		/// <c>do_clone</c>'s thing branch (<c>src/create.c:727</c>):
		/// <c>notify_format(player, T("Cloned: Object %s."), unparse_dbref(clone))</c>, the same bare
		/// <c>#N</c> as <see cref="CreatedObject"/>. A room says <see cref="ClonedRoom"/>; an exit says
		/// nothing, because its branch is a <c>do_real_open</c>.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ClonedObject = "Cloned: Object {0}.";
		/// <summary><c>do_clone</c>'s room branch (<c>src/create.c:744</c>).</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ClonedRoom = "Cloned: Room {0}.";
		public const string MonikerCleared = "Moniker cleared.";
		public const string MonikerSet = "Moniker set.";
		public const string ThemeSet = "Theme set.";
		public const string ThemeCleared = "Theme cleared.";
		public const string ThemeUnreadableFormat = "Your @theme does not read ({0}), so layouts use the game's theme. @theme me=<theme> sets another; @theme me= clears it.";
		public const string DigWhat = "Dig what?";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RoomCreatedWithNumberFormat = "{0} created with room number {1}.";
		public const string TryingToLink = "Trying to link...";

		public const string CantGoThatWay = "You can't go that way.";
		public const string CantSeeThroughThat = "You can't see through that.";
		public const string ExitGoesNowhere = "That exit doesn't go anywhere.";
		public const string ExitNoValidLocation = "That exit doesn't go to a valid location.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string VariableExitDestinationInvalidFormat =
			"Variable exit destination #{0} is invalid or not permitted.";
		public const string CantGoThatWayContainmentLoop = "You can't go that way - it would create a containment loop.";
		public const string YouHaveBeenTeleported = "You have been teleported.";

		/// <summary>PennMUSH <c>do_leave</c>'s <c>fail_lock</c> default (<c>src/move.c:983</c>).</summary>
		public const string CantLeave = "You can't leave.";

		/// <summary>PennMUSH <c>do_move</c>'s home branch (<c>src/move.c:415-417</c>), sent three times.</summary>
		public const string NoPlaceLikeHome = "There's no place like home...";

		/// <summary>PennMUSH <c>do_move</c>'s home broadcast (<c>src/move.c:409-412</c>).</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GoesHomeFormat = "{0} goes home.";

		/// <summary>PennMUSH <c>do_enter</c>'s self-entry refusal (<c>src/move.c:957-959</c>).</summary>
		public const string MustRemainBesideYourself = "Sorry, you must remain beside yourself!";

		// Emit-family sender echoes, suppressed by /silent (PennMUSH speech.c, bsd.c).
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouPemitToObjectFormat = "You pemit \"{0}\" to {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouPemitToCountFormat = "You pemit \"{0}\" to {1} objects.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouPemitToConnectionsFormat = "You pemit \"{0}\" to {1} connections.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouRemitInFormat = "You remit, \"{0}\" in {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouLemitFormat = "You lemit: \"{0}\"";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouZemitInZoneFormat = "You zemit, \"{0}\" in zone {1}";
		public const string ThereCantBeAnythingInThat = "There can't be anything in that!";
		public const string LemitTooManyContainers = "Too many containers.";

		public const string DontYouHaveAnythingToSay = "Don't you have anything to say?";
		public const string HuhTypeHelp = "Huh?  (Type \"help\" for help.)";
		public const string Notified = "Notified.";
		/// <summary>PennMUSH <c>cmd_notify_drain</c> (<c>src/cque.c:1539</c>), unless the executor or its owner is QUIET.</summary>
		public const string Drained = "Drained.";
		public const string YouDoNotHavePermissionToSpoofEmits = "You do not have permission to spoof emits.";
		public const string NoSuchCommandAtLogin = "No such command available at login.";
		public const string InvalidRoomSpecified = "Invalid room specified.";
		public const string YouMustProvideMatchString = "You must provide a string to match when using /match.";
		public const string YouMustSpecifyObjectToDecompile = "You must specify an object to decompile.";
		public const string YouMustSpecifyObjectToRestart = "You must specify an object to restart.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AmbiguousChannelNameFormat = "Ambiguous channel name '{0}'. Please be more specific.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AmbiguousChannelNameMatchesFormat = "Ambiguous channel name '{0}'. Matches: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string UsageAtCommandFormat = "Usage: @{0} <object>=<value>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ErrorDetailFormat = "Error: {0}";
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

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToCreateFlagFormat = "Failed to create flag '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotDeleteSystemFlagFormat = "Cannot delete system flag '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToDeleteFlagFormat = "Failed to delete flag '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToUpdateFlagFormat = "Failed to update flag '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToDisableFlagFormat = "Failed to disable flag '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToEnableFlagFormat = "Failed to enable flag '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToCreatePowerFormat = "Failed to create power '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotDeleteSystemPowerFormat = "Cannot delete system power '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToDeletePowerFormat = "Failed to delete power '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToUpdatePowerFormat = "Failed to update power '{0}'.";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NoLogEntriesForCategoryFormat = "No log entries found for category '{0}'.";
		public const string LogUsage = "Usage: @log[/<switch>] <message> or @log/recall[/<switch>] [<number>]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MessageLoggedToCategoryFormat = "Message logged to {0} log.";
		public const string PoorUsage = "Usage: @poor <player>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PlayerSetToPoorFormat = "{0} has been set to poor status (quota: 0).";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YourQuotaSetToZeroByFormat = "Your building quota has been set to 0 by {0}.";
		public const string CantLookAtOthersQuota = "You can't look at someone else's quota.";
		public const string AllQuotaUsage = "Usage: @allquota <amount>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SetQuotaForPlayersFormat = "Set quota to {0} for {1} players.";
		public const string NotSupportedForSharpMUSH = "Not Supported for SharpMUSH.";
		// PennMUSH bsd.c:7239,7246 (hide_player's self-target branch, the only one @hide implements).
		public const string NoLongerAppearOnWho = "You no longer appear on the WHO list.";
		public const string NowAppearOnWho = "You now appear on the WHO list.";
		public const string NeedAnnouncePower = "Permission denied. You need the Announce power.";

		public const string NoSuicideAllowed = "Sorry, no suicide allowed.";
		public const string EvenYouCantDoThat = "Even you can't do that!";
		public const string MayNotDestroyConnectedPlayer = "How gruesome. You may not destroy players who are connected.";
		public const string MustUseNukeToDestroyPlayer = "You must use @nuke to destroy a player.";
		/// <summary>
		/// <c>do_destroy</c>'s TYPE_PLAYER branch with <c>destroy_possessions</c> on and
		/// <c>really_safe</c> off (<c>src/destroy.c:374</c>). <c>{0}</c> is <c>unparse_object</c>.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PlayerAndObjectsScheduledDestroyedFormat = "{0} and all their objects are scheduled to be destroyed.";
		/// <summary>
		/// The same branch with <c>really_safe</c> on, which spares the player's SAFE objects
		/// (<c>src/destroy.c:372</c>).
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PlayerAndNonSafeObjectsScheduledDestroyedFormat = "{0} and all their (non-SAFE) objects are scheduled to be destroyed.";
		/// <summary>
		/// <c>do_destroy</c>'s message for anything whose possessions stay behind
		/// (<c>src/destroy.c:377</c>, <c>:391</c>, <c>:405</c>). <c>{0}</c> is <c>unparse_object</c>.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectScheduledDestroyedFormat = "{0} is scheduled to be destroyed.";
		/// <summary>
		/// <c>do_halt</c>'s report to the halted object's owner (<c>src/cque.c:2177</c>), which
		/// <c>free_object</c> reaches on the way to deleting the object.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedObjectReportFormat = "Halted: {0}(#{1})";

		public const string DefaultHomeLocationInvalid = "Default home location is invalid.";
		/// <summary>
		/// PennMUSH <c>make_first_free_wrapper</c> (<c>src/destroy.c:940</c>) for every way a requested
		/// dbref can fail to be one the player may build on: unparseable, out of range, or in use. The
		/// wording deliberately does not distinguish them, so it cannot be used to probe the database.
		/// </summary>
		public const string CreateDbrefUnavailable = "That is not a valid dbref.";
		public const string MoneyFunctionNotSupported = "The money() function is not supported. SharpMUSH does not track money or pennies.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MessageSentToRecipientsFormat = "Message sent to {0} recipient(s).";

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

		public const string DoListWhatToDoWithList = "What do you want to do with the list?";

		public const string ExitNoValidLocationDetail = "That exit doesn't go to a valid location.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ExitNameToDestFormat = "{0} to {1}";

		/// <summary>PennMUSH <c>follower_command</c> — src/move.c:1485.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouFollowFormat = "You follow {0}.";

		/// <summary>PennMUSH <c>do_find</c> (<c>src/look.c:1102</c>).</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FindObjectsFoundFormat = "*** {0} objects found ***";
		/// <summary>PennMUSH <c>do_find</c> (<c>src/look.c:1079</c>): a range bound that is not an object.</summary>
		public const string FindInvalidRange = "Invalid range argument";
		/// <summary>PennMUSH <c>pay_queue</c> (<c>src/cque.c:304</c>): the owner of an object that ran past its queue quota is told so.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RunawayObjectFormat = "Runaway object: {0}({1}). Commands halted.";
		/// <summary>PennMUSH <c>do_halt</c> (<c>src/cque.c:2176-2178</c>): the owner of an object whose queue is wiped by hardcode, unless QUIET.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedNoticeFormat = "Halted: {0}({1})";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ObjectDoesNotWantToHearFromYouFormat = "{0} does not want to hear from you.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YouPromptedFormat = "You prompted {0}.";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SwitchInvalidRegexpFormat = "Invalid regexp: {0}: {1}";

		/// <summary>PennMUSH <c>do_force</c> (<c>src/wiz.c:637</c>), after match_controlled refuses the target.</summary>
		public const string ForceSorry = "Sorry.";
		public const string ForceThemToDoWhat = "Force them to do what?";

		public const string YouDoNotHavePermissionToSpoofEmitsDetail = "You do not have permission to spoof emits.";

		public const string DontYouHaveAnythingToSayDetail = "Don't you have anything to say?";
		public const string InvalidRoomSpecifiedDetail = "Invalid room specified.";

		public const string SelectMustSpecifyTestString = "You must specify a test string.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SelectInvalidRegexPatternFormat = "Invalid regex pattern: {0}";

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

		/// <summary><c>examine_atrs</c>'s answer to a named pattern that matched nothing (<c>src/look.c:384</c>).</summary>
		public const string ExamineNoMatchingAttributes = "No matching attributes.";

		/// <summary><c>do_edit</c>'s answers (<c>src/set.c:963</c>) and <c>edit_helper</c>'s per-attribute lines (<c>src/set.c:917</c>).</summary>
		public const string EditInvalidFormat = "I need to know what you want to edit.";
		public const string EditMustSpecifySearchAndReplace = "Nothing to do.";
		public const string EditNoMatchingAttributesFound = "No matching attributes.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EditAttributeSetFormat = "{0} - Set: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EditAttributeUnchangedFormat = "{0} - Unchanged.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EditQuietSummaryFormat = "{0} attributes edited, {1} skipped.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EditInvalidRegexpFormat = "Invalid regexp: {0}";

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

		public const string IncludeMustSpecifyAttributePath = "You must specify an object/attribute to include.";
		public const string IncludeMustSpecifyObjectAttributePath = "You must specify an object/attribute path.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string IncludeNoSuchAttributeFormat = "No such attribute: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string IncludeAttributeIsEmptyFormat = "Attribute {0} is empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string IncludeErrorExecutingFormat = "Error executing included attribute: {0}";

		public const string MailNoSuchUniquePlayer = "No such unique player: {0}.";
		public const string MailTooManySwitches = "Error: Too many switches passed to @mail.";
		public const string MailBadArguments = "MAIL: Bad arguments to @mail. See 'help @mail' for usage.";

		public const string PasswordOnlyPlayersHavePasswords = "Only players have passwords.";
		public const string PasswordInvalid = "Invalid password.";

		// @scan's section headings and match lines, verbatim from do_scan (pennmush/src/game.c:1890-1996)
		// and confirmed against a live PennMUSH 1.8.8. A heading prints whether or not its section
		// matched anything; the attribute list in {2} arrives with its own leading space.
		public const string ScanMatchesOnRoomContents = "Matches on contents of this room:";
		public const string ScanMatchesOnCarriedObjects = "Matches on carried objects:";
		public const string ScanMatchesOnZoneMasterRoomOfLocation = "Matches on zone master room of location:";
		public const string ScanMatchesOnPersonalZoneMasterRoom = "Matches on personal zone master room:";
		public const string ScanMatchesOnMasterRoomObjects = "Matches on objects in the Master Room:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ScanMatchEntryFormat = "{0}  [{1}:{2}]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ScanMatchedHereFormat = "Matched here: {0}  [{1}:{2}]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ScanMatchedSelfFormat = "Matched self: {0}  [{1}:{2}]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ScanMatchedZoneOfLocationFormat = "Matched zone of location: {0}  [{1}:{2}]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ScanMatchedPersonalZoneFormat = "Matched personal zone: {0}  [{1}:{2}]";

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

		public const string RetryUsage = "Usage: @retry <condition>[=<arg0>,<arg1>,...]";
		public const string RetryNothingToRetry = "Nothing to retry.";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LogWipeUnsupportedFormat = "@logwipe: SharpMUSH cannot {0} the {1} log. Its logs go to the logging sinks in its configuration, which the game does not own; rotate or clear them there.";

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

		public const string SearchUnknownOwner = "Unknown owner.";
		public const string SearchUnknownParent = "Unknown parent.";
		public const string SearchNothingFound = "Nothing found.";
		// PennMUSH fill_search_spec (src/wiz.c:2388-2399): START and COUNT are 1-based and at least one.
		public const string SearchInvalidStart = "Invalid start index";
		public const string SearchInvalidCount = "Invalid count index";
		// PennMUSH do_search's report (src/wiz.c:1323-1414). Each heading is preceded by a blank line.
		public const string SearchRoomsHeader = "\nROOMS:";
		public const string SearchExitsHeader = "\nEXITS:";
		public const string SearchThingsHeader = "\nTHINGS:";
		public const string SearchPlayersHeader = "\nPLAYERS:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SearchOwnedEntryFormat = "{0} [owner: {1}]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SearchExitEntryFormat = "{0} [from {1} to {2}]";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SearchPlayerLocationFormat = "{0} [location: {1}]";
		public const string SearchNowhere = "NOWHERE";
		public const string SearchDone = "----------  Search Done  ----------";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SearchTotalsFormat = "Totals: Rooms...{0}  Exits...{1}  Things...{2}  Players...{3}";

		public const string ListMotdCurrentSettingsHeader = "Current Message of the Day settings:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdSourceFormat = "  Messages come from: {0}";
		public const string ListMotdTemporaryHeader = "Temporary Message of the Day (cleared on restart):";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdConnectMotdFormat = "  Connect MOTD: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdWizardMotdFormat = "  Wizard MOTD:  {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdDownMotdFormat = "  Down MOTD:    {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ListMotdFullMotdFormat = "  Full MOTD:    {0}";

		public const string VerbUsage = "Usage: @verb <victim>=<actor>,<what>,<whatd>,<owhat>,<owhatd>,<awhat>[,<args>]";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string WhereIsTriedToLocateButUnableFormat = "{0} tried to locate you, but was unable to.";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotModifySystemFlagFormat = "Cannot modify system flag '{0}'.";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotModifySystemPowerFormat = "Cannot modify system power '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CannotDisableSystemPowerFormat = "Cannot disable system power '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToDisablePowerFormat = "Failed to disable power '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FailedToEnablePowerFormat = "Failed to enable power '{0}'.";

		public const string FullMotdCleared = "Full MOTD cleared.";
		public const string RejectMotdUsage = "Usage: @rejectmotd <message>";
		public const string FullMotdSet = "Full MOTD set.";

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
		public const string PlayerNotConnected = "That player is not connected.";
		public const string YouHaveBeenDisconnected = "You have been disconnected.";

		public const string NewPasswordGenerateSwitchConflict = "@NEWPASSWORD: /GENERATE switch cannot be used with other arguments.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NewPasswordGeneratedFormat = "Password for {0} changed to {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NewPasswordSetFormat = "Password for {0} changed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NewPasswordChangedByFormat = "Your password has been changed by {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LastConnectFormat = "Last connect was from {0} on {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string LastFailedConnectFormat = "Last FAILED connect was from {0}.";

		public const string PurgeComplete = "Purge complete.";

		public const string ChownAllUsage = "Usage: @chownall <player>[=<new owner>]";
		/// <summary>PennMUSH <c>do_chownall</c> (<c>src/wiz.c:1002</c>), to the executor: <c>{0}</c> objects chowned.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChownAllCompleteFormat = "Ownership changed for {0} objects.";

		/// <summary>PennMUSH src/cmds.c do_list, for a missing or unrecognised <c>@list</c> type.</summary>
		public const string ListNotUnderstood = "I don't understand what you want to @list.";

		public const string DumpDoesNothing = "Dump command does nothing for SharpMUSH. Consider using @backup.";

		public const string PlayerCreateInvalidName = "That is not a valid player name.";
		public const string PlayerNameAlreadyExists = "That player name already exists.";
		public const string PlayerCreateInvalidPassword = "That is not a valid password.";
		/// <summary>PennMUSH src/wiz.c do_pcreate: "New player '%s' (#%d) created with password '%s'".</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PlayerCreatedFormat = "New player '{0}' (#{1}) created with password '{2}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string YourQuotaSetToByFormat = "Your quota has been set to {0} by {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AllQuotaSetForPlayerFormat = "Your building quota has been set to {0} by {1}.";

		public const string KickUsage = "Usage: @kick <player>";

		public const string ReadCacheServiceNotAvailable = "Text file service not available.";
		public const string ReadCacheReindexing = "Reindexing text files...";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ReadCacheCompleteFormat = "Text file cache rebuilt in {0}ms.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ReadCacheErrorFormat = "Error reindexing text files after {0}ms: {1}";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EnableDisableUsageSyntaxFormat = "Usage: @{0} <option>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EnableDisableNoOptionFormat = "No configuration option named '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string EnableDisableNotBooleanFormat = "Option '{0}' is not a boolean option. Use @config/set instead.";

		// PennMUSH's forward-list validation in do_set_atr (src/attrib.c:2326-2358), refusing the whole
		// set before atr_add ever runs. Captured live on 2026-09-22.
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ForwardListRequiresDbrefsFormat = "{0} should contain only dbrefs.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ForwardListInvalidDbrefFormat = "Invalid dbref #{0} in {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ForwardListTargetRefusesSpeechFormat = "I don't think #{0} wants to hear from {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ForwardListTargetRefusesMailFormat = "I don't think #{0} wants {1}'s mail.";
	}
}
