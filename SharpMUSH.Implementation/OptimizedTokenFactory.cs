using Antlr4.Runtime;

namespace SharpMUSH.Implementation;

internal sealed class OptimizedTokenFactory : ITokenFactory
{
	private static readonly ITokenFactory Fallback = CommonTokenFactory.Default;

	public static readonly ITokenFactory Default = new OptimizedTokenFactory();

	private OptimizedTokenFactory() { }

	public IToken Create(
		Tuple<ITokenSource, ICharStream> source,
		int type,
		string text,
		int channel,
		int start,
		int stop,
		int line,
		int charPositionInLine)
	{
		if (source?.Item2 is StringSpanInputStream stringInputStream && text is null)
		{
			var token = new OptimizedToken(source, type, channel, start, stop, stringInputStream.Input)
			{
				Line = line,
				Column = charPositionInLine
			};
			return token;
		}

		return Fallback.Create(source, type, text, channel, start, stop, line, charPositionInLine);
	}

	public IToken Create(int type, string text)
		=> Fallback.Create(type, text);
}
