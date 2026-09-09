using System.Collections.Frozen;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	// Deliberately small: each operation transforms explicit text or bounded decimal operands.
	// Data access, attribute evaluation, plugins, deferred work and side effects have no entry.
	private static readonly FrozenSet<string> RestrictedOperations = new[]
	{
		"add", "sub", "mul", "div", "cat", "strcat", "strlen", "ucstr", "lcstr", "trim",
		"space", "words", "first", "rest", "extract", "fn", "restrictedexpr"
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	[SharpFunction(Name = "restrictedexpr", MinArgs = 2, MaxArgs = 12, Flags = FunctionFlags.NoParse,
		ParameterNames = ["allowlist", "expression", "literal inputs..."])]
	public async ValueTask<CallState> RestrictedExpression(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		var args = parser.CurrentState.Arguments;
		var allowlist = args["0"].Message?.ToPlainText() ?? "";
		if (allowlist.Length > 1024) return new CallState(EvaluationRestrictions.Error);
		var operations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var name in allowlist.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (!parser.FunctionLibrary.TryGetValue(name, out var definition)
				|| definition.LibraryInformation.RestrictedOperation is not { } operation)
				return new CallState(EvaluationRestrictions.Error);
			operations.Add(operation);
		}
		if (FunctionLimits.ExceedsCombinedOutput(args.Values.Select(value => value.Message ?? MarkupText.Empty)))
			return FunctionLimits.RejectOutput(parser.CurrentState);
		using var scope = new EvaluationRestrictions(operations).Enter();
		var parent = parser.CurrentState;
		var inputs = Enumerable.Range(0, args.Count - 2).ToDictionary(i => i.ToString(), i => new CallState(args[(i + 2).ToString()].Message));
		// A fresh root drops every evaluated register/history/response frame. Only explicit inputs
		// and shared execution accounting cross this boundary.
		var state = ParserState.RootFor(parent.Executor!.Value) with
		{
			Flags = ParserStateFlags.NoDebug,
			EnvironmentRegisters = inputs,
			CallDepth = parent.CallDepth,
			TotalInvocations = parent.TotalInvocations,
			FunctionRecursionDepths = parent.FunctionRecursionDepths,
			LimitExceeded = parent.LimitExceeded,
			ExecutionBudget = ExecutionBudget.Current ?? parent.ExecutionBudget,
			Restrictions = EvaluationRestrictions.Current
		};
		return await parser.FromState(state).FunctionParse(args["1"].Message ?? MarkupText.Empty) ?? CallState.Empty;
	}
}
