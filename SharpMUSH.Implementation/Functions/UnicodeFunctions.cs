using MarkupString;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "displaywidth", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> DisplayWidth(IMUSHCodeParser parser, SharpFunctionAttribute _)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!.DisplayWidth);

	[SharpFunction(Name = "graphemecount", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> GraphemeCount(IMUSHCodeParser parser, SharpFunctionAttribute _)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!.GraphemeCount);

	[SharpFunction(Name = "graphemes", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "output-separator"])]
	public ValueTask<CallState> Graphemes(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		var text = parser.CurrentState.Arguments["0"].Message!;
		var separator = parser.CurrentState.Arguments.TryGetValue("1", out var argument)
			? argument.Message! : MarkupText.Space;
		var outputLength = text.Length + (long)Math.Max(0, text.GraphemeCount - 1) * separator.Length;
		if (outputLength > FunctionLimits.MaxOutputCodeUnits)
		{
			if (parser.CurrentState.LimitExceeded is { } limit)
			{
				limit.IsExceeded = true;
				limit.ErrorMessage ??= ErrorMessages.Returns.OutputTooLarge;
			}
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.OutputTooLarge);
		}
		return ValueTask.FromResult<CallState>(separator.Length == 0
			? text : MarkupText.Join(separator, text.EnumerateGraphemes()));
	}
}
