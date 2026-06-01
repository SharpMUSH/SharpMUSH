using Antlr4.Runtime;

namespace SharpMUSH.Implementation;

/// <summary>
/// An error recovery strategy for lenient (command argument) parsing.
/// Overrides <see cref="DefaultErrorStrategy.ConstructToken"/> so that
/// synthetic tokens inserted during error recovery carry empty text and
/// sit at the same position as the last real token. This prevents
/// ANTLR's "&lt;missing ')'&gt;" annotations from leaking into stored
/// attribute values when the visitor serialises recovered parse trees.
/// </summary>
internal sealed class LenientErrorStrategy : DefaultErrorStrategy
{
	protected override IToken ConstructToken(
		ITokenSource tokenSource,
		int expectedTokenType,
		string tokenText,
		IToken current)
	{
		// Use empty text instead of "<missing X>" so that context.GetText() and
		// MModule.substring(…, context.Stop.StopIndex, …) produce clean output.
		// Position at current.StopIndex so that StopIndex-based length calculations
		// remain correct for the surrounding context.
		return tokenSource.TokenFactory.Create(
			Tuple.Create(tokenSource, current.TokenSource.InputStream),
			expectedTokenType,
			"",
			TokenConstants.DefaultChannel,
			current.StopIndex,
			current.StopIndex,
			current.Line,
			current.Column + 1);
	}
}
