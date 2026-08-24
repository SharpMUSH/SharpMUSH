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
/// Softcode is whitespace-significant: whitespace is literal data almost everywhere. The single
/// exception is the lexer's <c>fragment WS: [ \r\n\f\t]*</c>, which is attached to exactly seven
/// token rules — <c>OBRACK</c>, <c>OBRACE</c>, <c>COMMAWS</c>, <c>EQUALS</c>, <c>SEMICOLON</c>,
/// <c>OPAREN</c> and <c>FUNCHAR</c>. Whitespace immediately following one of those is absorbed by
/// the token and is semantically invisible, so a newline plus indent there is free. Anywhere else
/// it changes the program's meaning.
/// </para>
/// <para>
/// This engine therefore only ever breaks immediately after a group opener
/// (<c>FUNCHAR</c>/<c>OPAREN</c>/<c>OBRACK</c>) or after a <c>COMMAWS</c>/<c>SEMICOLON</c>
/// separator. <c>EQUALS</c> and <c>OBRACE</c> are equally safe but reserved for a later version.
/// Two consequences follow and must not be weakened:
/// </para>
/// <list type="number">
/// <item><description>
/// Never break before a closing delimiter. There is no whitespace absorption at <c>CPAREN</c>,
/// <c>CBRACK</c> or <c>CBRACE</c>, so a newline there becomes literal text inside the final
/// argument. Closers cuddle the last item.
/// </description></item>
/// <item><description>
/// Never break inside a brace group. Brace contents are literal in some contexts and re-parsed as
/// code in others, so a brace group is atomic here: always rendered flat, never recursed into.
/// </description></item>
/// </list>
/// <para>
/// Because every break sits immediately after a whitespace-absorbing token, a poor grouping
/// decision can only produce ugly output — never a change of meaning.
/// </para>
/// </summary>
public static class SoftcodeLayout
{
	private static readonly string[] Openers = ["FUNCHAR", "OPAREN", "OBRACK", "OBRACE"];
	private static readonly string[] Closers = ["CPAREN", "CBRACK", "CBRACE"];
	private static readonly string[] Separators = ["COMMAWS", "SEMICOLON"];

	/// <summary>
	/// Computes the breaks needed to fit <paramref name="tokens"/> within <paramref name="width"/> columns.
	/// </summary>
	/// <param name="tokens">The lexed softcode, in source order.</param>
	/// <param name="width">Target line width in columns.</param>
	/// <param name="indentUnit">Columns of indent added per nesting level.</param>
	/// <returns>The breaks, ordered by token index. Empty when everything fits flat.</returns>
	public static IReadOnlyList<SoftcodeBreak> Compute(IReadOnlyList<TokenInfo> tokens, int width, int indentUnit = 2)
	{
		if (tokens.Count == 0)
		{
			return [];
		}

		var effectiveWidth = Math.Max(1, width);
		var effectiveIndentUnit = Math.Max(0, indentUnit);
		var root = BuildGroupTree(tokens);
		ComputeFlatWidths(root, tokens);

		var breaks = new List<SoftcodeBreak>();
		Layout(root, tokens, depth: 0, column: 0, effectiveWidth, effectiveIndentUnit, breaks);
		breaks.Sort((a, b) => a.TokenIndex.CompareTo(b.TokenIndex));

		return breaks;
	}

	/// <summary>
	/// Walks the tokens with a stack, from a synthetic root group. Unbalanced input never throws:
	/// a closer with nothing but the root on the stack is ignored, and groups still open at end of
	/// input close implicitly at the last token.
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
			else if (Array.IndexOf(Closers, type) >= 0)
			{
				// A closer with only the root left is stray text; drop it rather than unwinding the root.
				if (stack.Count > 1)
				{
					var closed = stack.Pop();
					closed.CloseIndex = i;
					closed.HasCloser = true;
				}
			}
			else if (Array.IndexOf(Separators, type) >= 0)
			{
				// A separator belongs to the innermost group enclosing it, never to an ancestor —
				// so a comma inside {...} is the brace group's, and is therefore never a break point.
				stack.Peek().Separators.Add(i);
			}
		}

		var last = tokens.Count - 1;
		while (stack.Count > 0)
		{
			stack.Pop().CloseIndex = last;
		}

		return root;
	}

	/// <summary>Bottom-up: a group's flat width is the summed text length of its whole token span.</summary>
	private static void ComputeFlatWidths(Group group, IReadOnlyList<TokenInfo> tokens)
	{
		foreach (var child in group.Children)
		{
			ComputeFlatWidths(child, tokens);
		}

		var start = group.OpenIndex < 0 ? 0 : group.OpenIndex;
		var flat = 0;
		for (var i = start; i <= group.CloseIndex; i++)
		{
			flat += tokens[i].Text.Length;
		}

		group.FlatWidth = flat;
	}

	/// <summary>
	/// Renders one group top-down, returning the column the following text starts at.
	/// </summary>
	private static int Layout(Group group, IReadOnlyList<TokenInfo> tokens, int depth, int column, int width,
		int indentUnit, List<SoftcodeBreak> breaks)
	{
		// A brace group is atomic: its contents may be literal text, so it is never broken into.
		if (group.OpenType == "OBRACE" || column + group.FlatWidth <= width)
		{
			return column + group.FlatWidth;
		}

		var indent = Math.Min(depth * indentUnit, width / 2);
		var start = group.OpenIndex < 0 ? 0 : group.OpenIndex;
		var end = group.CloseIndex;

		// Breaking immediately before a closer would put a literal newline inside the last argument,
		// so the final content token never carries a break.
		var lastContent = group.HasCloser ? end - 1 : end;

		var i = start;
		if (group.OpenIndex >= 0)
		{
			// The synthetic root has no opening delimiter and so emits no opener break; it breaks
			// only at its own direct-child separators.
			breaks.Add(new SoftcodeBreak(group.OpenIndex, indent));
			column = indent;
			i++;
		}

		var separators = group.Separators;
		var separatorCursor = 0;
		var childCursor = 0;

		while (i <= end)
		{
			if (childCursor < group.Children.Count && group.Children[childCursor].OpenIndex == i)
			{
				var child = group.Children[childCursor++];
				column = Layout(child, tokens, depth + 1, column, width, indentUnit, breaks);
				i = child.CloseIndex + 1;
				continue;
			}

			column += tokens[i].Text.Length;

			while (separatorCursor < separators.Count && separators[separatorCursor] < i)
			{
				separatorCursor++;
			}

			if (separatorCursor < separators.Count && separators[separatorCursor] == i && i < lastContent)
			{
				separatorCursor++;
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

		/// <summary>False when the group was closed implicitly by end of input rather than by a closer.</summary>
		public bool HasCloser { get; set; }

		/// <summary>Indices of this group's own direct-child separators, ascending.</summary>
		public List<int> Separators { get; } = [];

		/// <summary>Nested groups, in source order.</summary>
		public List<Group> Children { get; } = [];

		/// <summary>Width of the group rendered on one line.</summary>
		public int FlatWidth { get; set; }
	}
}
