using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "strdistance", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["source", "target"])]
	public ValueTask<CallState> StringDistanceFunction(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		var arguments = parser.CurrentState.Arguments;
		return StringDistance.TryCalculate(arguments["0"].Message!, arguments["1"].Message!, out var distance)
			? ValueTask.FromResult<CallState>(distance)
			: ValueTask.FromResult<CallState>(StringDistance.WorkLimitExceeded);
	}
}
