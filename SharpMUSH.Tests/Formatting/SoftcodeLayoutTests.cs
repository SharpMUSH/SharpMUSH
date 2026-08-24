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

	[Test]
	public async Task EveryBreakFollowsAWhitespaceAbsorbingToken()
	{
		const string src = "switch(words(%0),0,nothing,1,[ucstr(%0)],{literal, text},done)";
		var tokens = Lex(src);
		var breaks = SoftcodeLayout.Compute(tokens, width: 20);

		string[] safe = ["FUNCHAR", "OPAREN", "OBRACK", "OBRACE", "COMMAWS", "EQUALS", "SEMICOLON"];
		foreach (var b in breaks)
		{
			await Assert.That(safe).Contains(tokens[b.TokenIndex].Type);
		}
	}
}
