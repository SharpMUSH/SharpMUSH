using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace SharpMUSH.Implementation.Parsing;

/// <summary>
/// A token the lexer made from a <see cref="StringSpanInputStream"/>: <see cref="CommonToken"/> without the line, column
/// and channel fields, 48 bytes against 64. Its line and column are looked up from its start offset when read, which
/// gives the numbers the lexer would have stored. It is always on the default channel.
/// </summary>
/// <remarks>
/// A parse tree keeps its tokens for as long as <see cref="SoftcodeParseCache"/> keeps the tree, and a lexed input has
/// one token for each delimiter in it, so these fields are what a long argument costs to keep.
/// </remarks>
internal sealed class SoftcodeToken(Tuple<ITokenSource, ICharStream> source, int type, int start, int stop)
	: IWritableToken
{
	private readonly Tuple<ITokenSource, ICharStream> _source = source;
	private string? _text;

	public int Type { get; set; } = type;

	public int StartIndex { get; } = start;

	public int StopIndex { get; } = stop;

	public int TokenIndex { get; set; } = -1;

	public int Channel
	{
		get => TokenConstants.DefaultChannel;
		set => throw new NotSupportedException("A softcode token is always on the default channel.");
	}

	public int Line
	{
		get => Input.PositionOf(StartIndex).Line;
		set => throw new NotSupportedException("A softcode token's line follows from its start.");
	}

	public int Column
	{
		get => Input.PositionOf(StartIndex).Column;
		set => throw new NotSupportedException("A softcode token's column follows from its start.");
	}

	/// <summary>
	/// The token's text, read from the input once and kept, since a cached tree is visited many times. A token past the
	/// end of the input, the EOF token, reads <c>&lt;EOF&gt;</c>, as <see cref="CommonToken"/>'s does.
	/// </summary>
	public string Text
	{
		get => _text ??= StartIndex < Input.Size && StopIndex < Input.Size
			? Input.GetText(Interval.Of(StartIndex, StopIndex))
			: "<EOF>";
		set => _text = value;
	}

	public ITokenSource TokenSource => _source.Item1;

	public ICharStream InputStream => _source.Item2;

	private StringSpanInputStream Input => (StringSpanInputStream)_source.Item2;

	public override string ToString()
		=> $"[@{TokenIndex},{StartIndex}:{StopIndex}='{Text}',<{Type}>,{Line}:{Column}]";
}

/// <summary>
/// Makes a <see cref="SoftcodeToken"/> for each token the lexer reads from a <see cref="StringSpanInputStream"/>, and a
/// <see cref="CommonToken"/> for any other: one with text of its own (error recovery's), on another channel, or from
/// another kind of input.
/// </summary>
internal sealed class SoftcodeTokenFactory : ITokenFactory
{
	public static readonly SoftcodeTokenFactory Instance = new();

	public IToken Create(Tuple<ITokenSource, ICharStream> source, int type, string? text, int channel, int start,
		int stop, int line, int charPositionInLine)
		=> text is null && channel == TokenConstants.DefaultChannel && source.Item2 is StringSpanInputStream
			? new SoftcodeToken(source, type, start, stop)
			: CommonTokenFactory.Default.Create(source, type, text, channel, start, stop, line, charPositionInLine);

	public IToken Create(int type, string text) => new CommonToken(type, text);
}
