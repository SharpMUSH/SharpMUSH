namespace SharpMUSH.Implementation.Parsing;

/// <summary>
/// The built-in commands a parse marks, by the way each splits its arguments; see
/// <see cref="SoftcodeParsePipeline.Lex"/>.
/// </summary>
internal interface ICommandHeads
{
	/// <summary>
	/// The <c>CMD_*</c> token for the built-in <paramref name="name"/> (with any switches) runs, or 0 when
	/// it names none.
	/// </summary>
	int SplitToken(string name);
}
