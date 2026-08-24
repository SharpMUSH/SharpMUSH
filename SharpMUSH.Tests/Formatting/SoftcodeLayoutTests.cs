using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

public class SoftcodeLayoutTests
{
	/// <summary>Renders a token list plus its breaks back to text, so tests read as before/after.</summary>
	private static string Render(IReadOnlyList<TokenInfo> tokens, IReadOnlyList<SoftcodeBreak> breaks)
	{
		var byIndex = breaks.ToDictionary(b => b.TokenIndex, b => b.Indent);
		var sb = new System.Text.StringBuilder();
		for (var i = 0; i < tokens.Count; i++)
		{
			if (byIndex.TryGetValue(i, out var indent))
			{
				sb.Append(tokens[i].Text.TrimEnd());
				sb.Append('\n').Append(new string(' ', indent));
			}
			else
			{
				sb.Append(tokens[i].Text);
			}
		}

		return sb.ToString();
	}

	private static IReadOnlyList<TokenInfo> Lex(string source) => TestLexer.Lex(source);

	[Test]
	public async Task ShortInput_FitsFlat_NoBreaks()
	{
		var tokens = Lex("add(1,2)");
		var breaks = SoftcodeLayout.Compute(tokens, width: 78);
		await Assert.That(breaks).IsEmpty();
	}

	[Test]
	public async Task LongCall_BreaksAfterOpenParenAndCommas()
	{
		const string src = "switch(words(%0),0,nothing at all,1,just one,many words here)";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 30));

		await Assert.That(rendered).IsEqualTo(
			"""
			switch(
			  words(%0),
			  0,
			  nothing at all,
			  1,
			  just one,
			  many words here)
			""");
	}

	[Test]
	public async Task Closer_CuddlesLastItem_NeverOnItsOwnLine()
	{
		const string src = "switch(words(%0),0,nothing at all,1,just one,many words here)";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 30));

		await Assert.That(rendered).DoesNotContain("\n)");
		await Assert.That(rendered.TrimEnd()).EndsWith(")");
	}

	[Test]
	public async Task BraceGroups_AreNeverBrokenInside()
	{
		const string src = "switch(%0,1,{say a very long thing indeed, honestly},2,{other})";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 20));

		await Assert.That(rendered).Contains("{say a very long thing indeed, honestly}");
	}

	[Test]
	public async Task NestedGroups_IndentByDepth()
	{
		const string src = "switch(add(one thing,another thing),1,yes,no)";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 20));

		await Assert.That(rendered).Contains("\n  ");
		await Assert.That(rendered).Contains("\n    ");
	}

	[Test]
	public async Task SemicolonsBreakCommandLists()
	{
		const string src = "@pemit %#=first message here;@emit second message here;@wait 0=third";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 30));

		await Assert.That(rendered.Split('\n')).Count().IsEqualTo(3);
	}

	[Test]
	public async Task UnbalancedOpenParen_DoesNotThrow()
	{
		var tokens = Lex("switch(a,b,c");
		var breaks = SoftcodeLayout.Compute(tokens, width: 10);
		await Assert.That(breaks).IsNotNull();
	}

	[Test]
	public async Task UnbalancedCloseParen_DoesNotThrow()
	{
		var tokens = Lex("a,b,c)))");
		var breaks = SoftcodeLayout.Compute(tokens, width: 10);
		await Assert.That(breaks).IsNotNull();
	}

	[Test]
	public async Task IndentIsClampedToHalfWidth()
	{
		var src = string.Concat(Enumerable.Repeat("f(", 40)) + "x" + string.Concat(Enumerable.Repeat(")", 40));
		var tokens = Lex(src);
		var breaks = SoftcodeLayout.Compute(tokens, width: 40);

		await Assert.That(breaks.All(b => b.Indent <= 20)).IsTrue();
	}

	/// <summary>
	/// Re-derives, from the token text alone, the innermost delimiter enclosing a token — returning the
	/// opening token's index, or -1 at root. Deliberately independent of <see cref="SoftcodeLayout"/>'s
	/// own group bookkeeping, so a test built on it can contradict the implementation. A name followed
	/// by <c>(</c> opens a function call and <c>[</c>/<c>{</c> open their own groups; a bare <c>(</c>
	/// does not, because <c>SharpMUSHParser.g4</c> opens <c>function</c> on <c>FUNCHAR</c> only and
	/// routes a lone paren through <c>beginGenericText</c> as plain text.
	/// </summary>
	private static int EnclosingOpener(IReadOnlyList<TokenInfo> tokens, int index)
	{
		var stack = new Stack<int>();
		for (var i = 0; i < index; i++)
		{
			var text = tokens[i].Text.TrimEnd();
			if ((text.Length > 1 && text.EndsWith('(')) || text is "[" or "{")
			{
				stack.Push(i);
			}
			else if (text is ")" or "]" or "}" && stack.Count > 0)
			{
				stack.Pop();
			}
		}

		return stack.Count == 0 ? -1 : stack.Peek();
	}

	/// <summary>
	/// The semantic-safety claim, asserted structurally. A COMMAWS is an argument separator only inside
	/// name(...) and a SEMICOLON separates commands only at root; anywhere else the whitespace those
	/// tokens absorb is literal program data, because VisitBeginGenericText emits the raw token text.
	/// </summary>
	[Test]
	public async Task BreaksLandOnlyOnStructuralDelimiters()
	{
		string[] sources =
		[
			"switch(words(%0),0,nothing,1,[ucstr(%0)],{literal, text},done)",
			"@emit A long line of prose, and more prose here;@pemit %#=a, b, c",
			"switch(a,[ansi(hr,a long stretch of text),y],{b,c},trailing prose, and more)",
			"iter(%0,a long chunk (with a parenthetical) of prose here,%b,%b)"
		];

		foreach (var src in sources)
		{
			var tokens = Lex(src);
			var breaks = SoftcodeLayout.Compute(tokens, width: 20);

			// Without this the test would pass vacuously against a Compute that returned nothing at all.
			await Assert.That(breaks).IsNotEmpty().Because($"no breaks to check in [{src}]");

			foreach (var b in breaks)
			{
				var opener = EnclosingOpener(tokens, b.TokenIndex);
				switch (tokens[b.TokenIndex].Type)
				{
					case "COMMAWS":
						await Assert.That(opener).IsNotEqualTo(-1).Because($"comma break at root in [{src}]");
						await Assert.That(tokens[opener].Type).IsEqualTo("FUNCHAR")
							.Because($"comma break outside an argument list in [{src}]");
						break;
					case "SEMICOLON":
						await Assert.That(opener).IsEqualTo(-1).Because($"semicolon break inside a group in [{src}]");
						break;
					default:
						// The only other break position is a group opener, which must be the token that
						// this independent walk also treats as opening a group.
						await Assert.That(EnclosingOpener(tokens, b.TokenIndex + 1)).IsEqualTo(b.TokenIndex)
							.Because($"break after a non-delimiter in [{src}]");
						break;
				}
			}
		}
	}

	[Test]
	public async Task RootLevelProse_IsNeverBrokenAtItsCommas()
	{
		const string src = "@emit A long line of prose, and more prose here, and yet more besides";
		var tokens = Lex(src);
		var breaks = SoftcodeLayout.Compute(tokens, width: 20);

		await Assert.That(breaks).IsEmpty();
	}

	[Test]
	public async Task BareParens_AreTextNotGroups()
	{
		// A lone '(' is beginGenericText, so it neither opens a group nor absorbs whitespace
		// structurally; breaking after it would insert a literal newline into the emitted text.
		// Two content tokens inside the parens, so that treating '(' as an opener would produce a break
		// rather than being masked by the empty-group guard.
		const string src = "@emit a long parenthetical (with several words, inside it) and then some more";
		var tokens = Lex(src);
		var breaks = SoftcodeLayout.Compute(tokens, width: 20);

		await Assert.That(breaks).IsEmpty();
		await Assert.That(tokens.Select(t => t.Type)).Contains("OPAREN");
	}

	[Test]
	public async Task StrayCloser_NeverLandsOnItsOwnLine()
	{
		const string src = "aaaaaaaaaaaaaaaaaaaa;)";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 10));

		await Assert.That(rendered).DoesNotContain("\n)");
	}

	[Test]
	public async Task EmptyCall_KeepsItsDelimitersTogether()
	{
		var tokens = Lex("rand()");
		var breaks = SoftcodeLayout.Compute(tokens, width: 1);

		await Assert.That(breaks).IsEmpty();
	}

	[Test]
	public async Task MismatchedCloserInsideBraces_DoesNotPopTheBraceGroup()
	{
		// bracePattern (SharpMUSHParser.g4:96) resets inFunction, so the ')' between the braces is plain
		// text to the grammar and closes nothing. Popping the brace group on it would hand the comma
		// that follows to f's argument list, making it a break point inside literal text.
		const string src = "f(aaaa,{prose ) here, comma},b)";
		var tokens = Lex(src);
		var breaks = SoftcodeLayout.Compute(tokens, width: 12);

		var open = tokens.Index().First(x => x.Item.Text.TrimEnd() == "{").Index;
		var close = tokens.Index().First(x => x.Item.Text.TrimEnd() == "}").Index;

		await Assert.That(breaks).IsNotEmpty();
		await Assert.That(breaks.Any(b => b.TokenIndex >= open && b.TokenIndex < close)).IsFalse();
	}

	[Test]
	public async Task LiteralNewlines_ResetTheColumn()
	{
		// Attributes have held literal newlines since PR #775. The bracket group starts a fresh line,
		// so it is measured from column 0 and fits — accumulating the first line's width would break it.
		const string src = "aaaaaaaaaaaaaaaaaaaaaaaaaa\n[switch(1,a,b)]";
		var tokens = Lex(src);
		var breaks = SoftcodeLayout.Compute(tokens, width: 30);

		await Assert.That(breaks).IsEmpty();
	}
}
