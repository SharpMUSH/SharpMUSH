namespace SharpMUSH.Implementation.Parsing;

/// <summary>One function call in a piece of softcode, as <see cref="SoftcodeSyntaxAnalyzer.Calls"/> reads it.</summary>
/// <param name="Name">The function's name as written, without a leading <c>!</c>.</param>
/// <param name="Arguments">The text of each argument as written, unevaluated; <c>f()</c> has one empty one.</param>
public sealed record SoftcodeCall(string Name, IReadOnlyList<string> Arguments)
{
	/// <summary>Whether the call is written <c>f()</c>, with nothing between its parentheses.</summary>
	public bool Empty => Arguments is [""];
}
