namespace SharpMUSH.Implementation.Common;

/// <summary>
/// PennMUSH's <c>##</c> and <c>#@</c> in an <c>iter()</c> pattern or an <c>@dolist</c> action list.
/// PennMUSH splices the element and its position into the text before evaluating it; SharpMUSH
/// rewrites them to reads of the outermost iteration instead, so element text is never evaluated
/// (<c>help compatibility parser</c>). The outermost one, because a nested <c>##</c> or <c>#@</c> is
/// already spliced by the outer loop in PennMUSH: <c>iter(a b,iter(x y,#@:##))</c> answers
/// <c>1:a 1:a 2:b 2:b</c>.
/// </summary>
public static class IterationTokens
{
	private const string Element = "##";
	private const string Position = "#@";

	/// <summary>The element: <c>%iL</c>, which is <c>itext(L)</c>.</summary>
	private static readonly MString ElementRead = MarkupText.Plain("%iL");

	/// <summary>
	/// The position. It has no substitution of its own, so it is a bracketed call, which evaluates
	/// wherever a substitution does.
	/// </summary>
	private static readonly MString PositionRead = MarkupText.Plain("[inum(L)]");

	/// <summary>
	/// <paramref name="text"/> with <c>##</c> and <c>#@</c> rewritten, or <see langword="null"/> when it
	/// holds neither, so a caller can keep its already-parsed argument. <c>##</c> goes first, which
	/// reads <c>###@</c> the way PennMUSH's left-to-right scan does (element, then position); neither
	/// replacement can form the other token.
	/// </summary>
	public static MString? Rewrite(MString text)
	{
		var plain = text.Text;
		var hasElement = plain.Contains(Element, StringComparison.Ordinal);
		var hasPosition = plain.Contains(Position, StringComparison.Ordinal);
		if (!hasElement && !hasPosition) return null;

		var rewritten = hasElement ? text.ReplaceAll(Element, ElementRead) : text;
		return hasPosition ? rewritten.ReplaceAll(Position, PositionRead) : rewritten;
	}
}
