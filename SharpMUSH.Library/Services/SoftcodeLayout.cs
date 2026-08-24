using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// A single line break in a softcode rendering: after emitting <paramref name="TokenIndex"/>'s text
/// <em>with its trailing whitespace trimmed</em>, emit <c>\n</c> followed by <paramref name="Indent"/> spaces.
/// </summary>
/// <param name="TokenIndex">Index into the token list the break follows.</param>
/// <param name="Indent">Number of spaces to open the next line with.</param>
public readonly record struct SoftcodeBreak(int TokenIndex, int Indent);

/// <summary>
/// Decides where a line break may be inserted when displaying MUSH softcode.
/// <para>
/// Softcode is whitespace-significant: whitespace is literal data almost everywhere. Seven lexer
/// rules carry <c>fragment WS: [ \r\n\f\t]*</c> and so swallow the whitespace that follows them —
/// <c>OBRACK</c>, <c>OBRACE</c>, <c>COMMAWS</c>, <c>EQUALS</c>, <c>SEMICOLON</c>, <c>OPAREN</c> and
/// <c>FUNCHAR</c>. That absorption is <b>not</b> on its own a licence to break.
/// <c>VisitBeginGenericText</c> emits <c>GetContextText</c> — the raw token text, absorbed
/// whitespace included — so wherever one of those tokens is not acting as a structural delimiter,
/// its whitespace is literal program data and a newline inserted there changes the output.
/// </para>
/// <para>
/// A break is therefore emitted only where the token is <em>structural</em>:
/// </para>
/// <list type="bullet">
/// <item><description>
/// After a <c>FUNCHAR</c>, which is the only way <c>SharpMUSHParser.g4</c> opens a
/// <c>function</c> (see its <c>function:</c> rule). A bare <c>OPAREN</c> reaches the parser through
/// <c>beginGenericText</c> and is plain text, so it is neither a group nor a break position.
/// </description></item>
/// <item><description>
/// After a <c>COMMAWS</c> whose immediately-enclosing group is opened by a <c>FUNCHAR</c> — that is,
/// a genuine argument separator. A comma anywhere else is prose.
/// </description></item>
/// <item><description>
/// After a <c>SEMICOLON</c> at root, where it separates commands. A <c>;</c> inside a function's
/// arguments is literal.
/// </description></item>
/// <item><description>
/// After an <c>OBRACK</c>. <b>Provisional</b>: bracket groups are treated as structural for now, but
/// this is gated on Task 3's equivalence corpus proving it by evaluating formatted output. If that
/// disproves it, drop <c>OBRACK</c> from <see cref="Openers"/> exactly as <c>OPAREN</c> was dropped.
/// </description></item>
/// </list>
/// <para>
/// Two further rules, neither of which may be weakened:
/// </para>
/// <list type="number">
/// <item><description>
/// Never break immediately before a closing delimiter. There is no whitespace absorption at
/// <c>CPAREN</c>, <c>CBRACK</c> or <c>CBRACE</c>, so a newline there becomes literal text inside the
/// final argument. Closers cuddle the last item.
/// </description></item>
/// <item><description>
/// Never break inside a brace group. Brace contents are literal in some contexts and re-parsed as
/// code in others, so a brace group is atomic here: always rendered flat, never recursed into.
/// </description></item>
/// </list>
/// <para>
/// <c>EQUALS</c> and <c>OBRACE</c> absorb whitespace too, and are structural in some positions, but
/// v1 never breaks at either.
/// </para>
/// </summary>
public static class SoftcodeLayout
{
	/// <summary>
	/// Token types that open a group. <c>OPAREN</c> is deliberately absent: the grammar opens
	/// <c>function</c> on <c>FUNCHAR</c> alone, so a bare <c>(</c> is text, not structure.
	/// </summary>
	private static readonly string[] Openers = ["FUNCHAR", "OBRACK", "OBRACE"];

	private static readonly string[] Closers = ["CPAREN", "CBRACK", "CBRACE"];

	/// <summary>
	/// The opener type a closing token may close, or <c>null</c> if the type is not a closer. A closer
	/// only ever matches its own kind: <c>)</c> closes a call, <c>]</c> a bracket group, <c>}</c> a
	/// brace group. Anything else is literal text.
	/// </summary>
	private static string? OpenerClosedBy(string closerType) => closerType switch
	{
		"CPAREN" => "FUNCHAR",
		"CBRACK" => "OBRACK",
		"CBRACE" => "OBRACE",
		_ => null
	};

	/// <summary>
	/// Computes the breaks needed to fit <paramref name="tokens"/> within <paramref name="width"/> columns.
	/// </summary>
	/// <param name="tokens">The lexed softcode, in source order.</param>
	/// <param name="width">Target line width in columns.</param>
	/// <param name="indentUnit">Columns of indent added per nesting level.</param>
	/// <returns>The breaks, in ascending token order. Empty when everything fits flat.</returns>
	public static IReadOnlyList<SoftcodeBreak> Compute(IReadOnlyList<TokenInfo> tokens, int width, int indentUnit = 2)
	{
		if (tokens.Count == 0)
		{
			return [];
		}

		var effectiveWidth = Math.Max(1, width);
		var effectiveIndentUnit = Math.Max(0, indentUnit);
		var root = BuildGroupTree(tokens);
		MeasureFlat(root, tokens);

		var breaks = new List<SoftcodeBreak>();
		Layout(root, tokens, depth: 0, column: 0, effectiveWidth, effectiveIndentUnit, breaks);

		return breaks;
	}

	/// <summary>
	/// Walks the tokens with a stack, from a synthetic root group, recording each group's structural
	/// break points as it goes. Unbalanced input never throws: a closer with nothing but the root on
	/// the stack is ignored, and groups still open at end of input close implicitly at the last token.
	/// </summary>
	private static Group BuildGroupTree(IReadOnlyList<TokenInfo> tokens)
	{
		var root = new Group(openIndex: -1, openType: string.Empty);
		var stack = new Stack<Group>();
		stack.Push(root);

		for (var i = 0; i < tokens.Count; i++)
		{
			var type = tokens[i].Type;
			if (Array.IndexOf(Openers, type) >= 0)
			{
				var child = new Group(i, type);
				stack.Peek().Children.Add(child);
				stack.Push(child);
			}
			else if (OpenerClosedBy(type) is { } closedOpener)
			{
				// A closer that does not match the innermost group is text, not structure. bracePattern
				// (SharpMUSHParser.g4:96) resets inFunction, so a stray ')' inside {...} is plain text to
				// the grammar — popping the brace group on it would hand the brace's own commas to the
				// enclosing call and make them break points inside literal text. Ignore such a closer,
				// exactly as a closer that would unwind the root is ignored.
				if (stack.Count > 1 && stack.Peek().OpenType == closedOpener)
				{
					stack.Pop().CloseIndex = i;
				}
			}
			else if (type == "COMMAWS")
			{
				// Only an argument separator inside name(...). Elsewhere — at root, or inside a bracket
				// or brace group — the comma is prose and its absorbed whitespace is literal.
				if (stack.Peek().OpenType == "FUNCHAR")
				{
					stack.Peek().BreakPoints.Add(i);
				}
			}
			else if (type == "SEMICOLON")
			{
				// Only a command separator at root. Inside a function's arguments a ';' is literal.
				if (stack.Peek().OpenIndex < 0)
				{
					stack.Peek().BreakPoints.Add(i);
				}
			}
		}

		var last = tokens.Count - 1;
		while (stack.Count > 0)
		{
			stack.Pop().CloseIndex = last;
		}

		return root;
	}

	/// <summary>
	/// Bottom-up measurement of each group rendered on one line. Attribute text may contain literal
	/// newlines, so this records both the column a flat rendering ends on and whether it spans lines.
	/// </summary>
	private static void MeasureFlat(Group group, IReadOnlyList<TokenInfo> tokens)
	{
		foreach (var child in group.Children)
		{
			MeasureFlat(child, tokens);
		}

		var start = group.OpenIndex < 0 ? 0 : group.OpenIndex;
		var column = 0;
		var hasNewline = false;
		for (var i = start; i <= group.CloseIndex; i++)
		{
			var text = tokens[i].Text;
			hasNewline |= text.Contains('\n');
			column = Advance(column, text);
		}

		group.FlatWidth = column;
		group.HasNewline = hasNewline;
	}

	/// <summary>
	/// Advances a column past a token's text. Attributes have held literal newlines since PR #775, so
	/// a token carrying one starts a fresh line and the column becomes the width of that final line
	/// rather than an accumulation across the whole token.
	/// </summary>
	private static int Advance(int column, string text)
	{
		var lastNewline = text.LastIndexOf('\n');

		return lastNewline < 0
			? column + text.Length
			: text.Length - lastNewline - 1;
	}

	/// <summary>
	/// Renders one group top-down, returning the column the following text starts at.
	/// </summary>
	private static int Layout(Group group, IReadOnlyList<TokenInfo> tokens, int depth, int column, int width,
		int indentUnit, List<SoftcodeBreak> breaks)
	{
		// A brace group is atomic: its contents may be literal text, so it is never broken into.
		// Anything else stays flat only if it is genuinely one line and that line fits.
		if (group.OpenType == "OBRACE" || (!group.HasNewline && column + group.FlatWidth <= width))
		{
			return group.HasNewline ? group.FlatWidth : column + group.FlatWidth;
		}

		var indent = Math.Min(depth * indentUnit, width / 2);
		var start = group.OpenIndex < 0 ? 0 : group.OpenIndex;

		// Breaking immediately before a closer would put a literal newline inside the last argument, so
		// the last real content token never carries a break. Trailing closers are skipped rather than
		// assumed to be exactly one: a group closed implicitly by end of input has none, and the root
		// may end in any number of stray ones.
		var lastContent = group.CloseIndex;
		while (lastContent >= start && Array.IndexOf(Closers, tokens[lastContent].Type) >= 0)
		{
			lastContent--;
		}

		var i = start;
		if (group.OpenIndex >= 0)
		{
			// The synthetic root has no opening delimiter and so emits no opener break; it breaks only
			// at its own structural separators. An empty group emits none either — there is nothing to
			// put on the next line but the closer.
			if (group.OpenIndex + 1 < lastContent)
			{
				breaks.Add(new SoftcodeBreak(group.OpenIndex, indent));
				column = indent;
			}
			else
			{
				column = Advance(column, tokens[group.OpenIndex].Text);
			}

			i++;
		}

		var breakPoints = group.BreakPoints;
		var breakCursor = 0;
		var childCursor = 0;

		while (i <= group.CloseIndex)
		{
			if (childCursor < group.Children.Count && group.Children[childCursor].OpenIndex == i)
			{
				var child = group.Children[childCursor++];
				column = Layout(child, tokens, depth + 1, column, width, indentUnit, breaks);
				i = child.CloseIndex + 1;
				continue;
			}

			column = Advance(column, tokens[i].Text);

			while (breakCursor < breakPoints.Count && breakPoints[breakCursor] < i)
			{
				breakCursor++;
			}

			if (breakCursor < breakPoints.Count && breakPoints[breakCursor] == i && i < lastContent)
			{
				breakCursor++;
				breaks.Add(new SoftcodeBreak(i, indent));
				column = indent;
			}

			i++;
		}

		return column;
	}

	/// <summary>A delimiter-bounded span of tokens. The root has no delimiters and covers everything.</summary>
	private sealed class Group(int openIndex, string openType)
	{
		/// <summary>Index of the opening token, or -1 for the synthetic root.</summary>
		public int OpenIndex { get; } = openIndex;

		/// <summary>Token type of the opener, or empty for the synthetic root.</summary>
		public string OpenType { get; } = openType;

		/// <summary>Index of the last token in the group, inclusive — the closer when there is one.</summary>
		public int CloseIndex { get; set; }

		/// <summary>
		/// Indices of this group's own structural separators, ascending. Only separators that are
		/// genuinely delimiters in this group's context are recorded; prose commas and literal
		/// semicolons never reach here.
		/// </summary>
		public List<int> BreakPoints { get; } = [];

		/// <summary>Nested groups, in source order.</summary>
		public List<Group> Children { get; } = [];

		/// <summary>Column a flat rendering of the group ends on, measured from column 0.</summary>
		public int FlatWidth { get; set; }

		/// <summary>Whether the group's text already contains a literal newline.</summary>
		public bool HasNewline { get; set; }
	}
}
