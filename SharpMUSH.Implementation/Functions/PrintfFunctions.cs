using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// printf(): SharpMUSH's %-code grammar, or RhostMUSH's $-code grammar when <c>rhost_printf</c> is on.
	/// RhostMUSH's printf() wants at least one value after the format.
	/// </summary>
	[SharpFunction(Name = "printf", MinArgs = 1, MaxArgs = PrintfFormatter.MaxFields + 1,
		Flags = FunctionFlags.Regular, ParameterNames = ["format", "value..."])]
	public ValueTask<CallState> Printf(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		var arguments = parser.CurrentState.ArgumentsOrdered;
		var format = arguments["0"].Message;
		var values = arguments.Skip(1).Select(argument => argument.Value.Message).ToArray();
		string? error;
		MString result;

		if (Configuration.CurrentValue.Compatibility.RhostPrintf)
		{
			if (values.Length == 0)
				return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.TooFewArguments, "PRINTF", 2, 1)));
			if (RhostPrintfFormatter.TryFormat(format, values, out result, out error, parser.CurrentState.OutputLimit))
				return ValueTask.FromResult<CallState>(result);
		}
		else if (PrintfFormatter.TryFormat(format, values, out result, out error, parser.CurrentState.OutputLimit))
			return ValueTask.FromResult<CallState>(result);

		return ValueTask.FromResult(error == ErrorMessages.Returns.OutputTooLarge
			? FunctionLimits.RejectOutput(parser.CurrentState)
			: new CallState(error!));
	}
}
