using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests;

public static class ParserEvaluation
{
	/// <summary>
	/// What <paramref name="text"/> evaluates to. A parse that returns no result fails the test here, naming
	/// the input, rather than at whichever member the test reads next.
	/// </summary>
	public static async ValueTask<MString> EvaluateAsync(this IMUSHCodeParser parser, MString text)
		=> await parser.FunctionParse(text) switch
		{
			{ } result => result.Message,
			null => throw new InvalidOperationException($"FunctionParse returned no result for: {text}")
		};
}
