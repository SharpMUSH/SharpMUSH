using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "printf", MinArgs = 1, MaxArgs = PrintfFormatter.MaxFields + 1,
		Flags = FunctionFlags.Regular, ParameterNames = ["format", "value..."])]
	public ValueTask<CallState> Printf(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		var arguments = parser.CurrentState.ArgumentsOrdered;
		if (PrintfFormatter.TryFormat(arguments["0"].Message!,
			arguments.Skip(1).Select(argument => argument.Value.Message!).ToArray(), out var result, out var error,
			parser.CurrentState.OutputLimit))
			return ValueTask.FromResult<CallState>(result);
		return ValueTask.FromResult(error == ErrorMessages.Returns.OutputTooLarge
			? FunctionLimits.RejectOutput(parser.CurrentState)
			: new CallState(error!));
	}
}
