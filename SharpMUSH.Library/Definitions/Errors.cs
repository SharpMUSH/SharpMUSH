using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Definitions;

public static class Errors
{
	public const string ErrorInvalidPassword = "#-1 INVALID PASSWORD";
	public const string ErrorNoSuchFlag = "#-1 NO SUCH FLAG";
	public const string ErrorNoSideFX = "#-1 SIDE EFFECTS DISABLED FOR THIS FUNCTION";
	public const string ErrorInteger = "#-1 ARGUMENT MUST BE INTEGER";
	public const string ErrorNoSuchTimezone = "#-1 NO SUCH TIMEZONE";
	public const string ErrorTimeInteger = "#-1 TIME INTEGER OUT OF RANGE";
	public const string ErrorPositiveInteger = "#-1 ARGUMENT MUST BE POSITIVE INTEGER";
	public const string ErrorIntegers = "#-1 ARGUMENTS MUST BE INTEGERS";
	public const string ErrorUInteger = "#-1 ARGUMENT MUST BE POSITIVE INTEGER";
	public const string ErrorUIntegers = "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS";
	public const string ErrorNumber = "#-1 ARGUMENT MUST BE NUMBER";
	public const string ErrorNumbers = "#-1 ARGUMENTS MUST BE NUMBERS";
	public const string ErrorDivideByZero = "#-1 DIVIDE BY ZERO";
	public const string ErrorInvoke = "#-1 FUNCTION INVOCATION LIMIT EXCEEDED";
	public const string ErrorRecursion = "#-1 FUNCTION RECURSION LIMIT EXCEEDED";
	public const string ErrorCall = "#-1 CALL LIMIT EXCEEDED";
	public const string ErrorPerm = "#-1 PERMISSION DENIED";
	public const string ErrorAttrPermissions = "#-1 NO PERMISSION TO GET ATTRIBUTE";
	public const string ErrorAttrEvalPermissions = "#-1 NO PERMISSION TO EVALUATE ATTRIBUTE";
	public const string ErrorAttrSetPermissions = "#-1 NO PERMISSION TO SET ATTRIBUTE";
	public const string ErrorAttrWipPermissions = "#-1 NO PERMISSION TO WIPE ATTRIBUTE";
	public const string ErrorObjectAttributeString = "#-1 INVALID OBJECT/ATTRIBUTE VALUE";
	public const string ErrorNoSuchAttribute = "#-1 NO SUCH ATTRIBUTE";
	public const string ErrorMatch = "#-1 NO MATCH";
	public const string ErrorNotVisible = "#-1 NO SUCH OBJECT VISIBLE";
	public const string ErrorCannotTeleport = "#-1 NO PERMISSION TO TELEPORT OBJECT";
	public const string ErrorDisabled = "#-1 FUNCTION DISABLED";
	public const string ErrorRange = "#-1 OUT OF RANGE";
	public const string ErrorArgRange = "#-1 ARGUMENT OUT OF RANGE";
	public const string ErrorBadRegName = "#-1 REGISTER NAME INVALID";
	public const string ErrorTooManyRegs = "#-1 TOO MANY REGISTERS";
	public const string ErrorTooManySwitches = "#-1 TOO MANY SWITCHES, OR A BAD COMBINATION OF SWITCHES";
	public const string ErrorCantSeeThat = "#-1 CAN'T SEE THAT HERE";
	public const string ErrorNoSuchFunction = "#-1 COULD NOT FIND FUNCTION: {0}";
	public const string ErrorTooFewArguments = "#-1 FUNCTION ({0}) EXPECTS AT LEAST {1} ARGUMENTS BUT GOT {2}";
	public const string ErrorTooManyArguments = "#-1 FUNCTION ({0}) EXPECTS AT MOST {1} ARGUMENTS BUT GOT {2}";
	public const string ErrorGotEvenArgs = "#-1 FUNCTION ({0}) EXPECTS AN ODD NUMBER OF ARGUMENTS";
	public const string ErrorGotUnEvenArgs = "#-1 FUNCTION ({0}) EXPECTS AN EVEN NUMBER OF ARGUMENTS";
	public const string ErrorWrongArgumentsRange = "#-1 FUNCTION ({0}) EXPECTS AT LEAST {1} ARGUMENTS AND AT MOST {2} BUT GOT {3}";
	public const string ErrorBadArgumentFormat = "#-1 BAD ARGUMENT FORMAT TO {0}";
	public const string ErrorAmbiguous = "#-2 I DON'T KNOW WHICH ONE YOU MEAN";
	public const string NothingToEvaluate = "#-1 NOTHING TO EVALUATE";
	public const string NothingToDo = "#-1 NOTHING TO DO";
	public const string ExitsCannotContainThings = "#-1 EXITS CANNOT CONTAIN THINGS";
	public const string ParentLoop = "#-1 PARENT LOOP DETECTED";
	public const string InvalidFlag = "#-1 INVALID FLAG FOR THIS OBJECT";
}