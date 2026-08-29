using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// <see cref="SoftcodeLayout.ComputeDelimiterDepths"/> — the depth data bracket colouring is painted
/// from. It answers a *different* question from <see cref="SoftcodeLayout.Compute"/>: not "where may a
/// break go" but "which characters are a matched structural pair, and how deep". The two share
/// <c>BuildGroupTree</c>, and that is the whole point — a lexical bracket matcher gets softcode wrong,
/// because <c>\[</c> is literal, a bare <c>(</c> is prose, and a <c>lit()</c> body is source text.
/// </summary>
public class SoftcodeDelimiterDepthTests
{
	/// <inheritdoc cref="SoftcodeLayoutTests"/>
	private static readonly Func<string, SoftcodeCallKind> AllNamesEvaluateTheirArguments =
		_ => SoftcodeCallKind.EvaluatesArguments;

	private static IReadOnlyList<SoftcodeDelimiter> Depths(string source,
		Func<string, SoftcodeCallKind>? classify = null, ParseType parseType = ParseType.Function)
		=> SoftcodeLayout.ComputeDelimiterDepths(TestLexer.Lex(source),
			classify ?? AllNamesEvaluateTheirArguments, parseType);

	/// <summary>Renders the depth map as a ruler under the source, so a failure reads at a glance.</summary>
	private static string Ruler(string source, IReadOnlyList<SoftcodeDelimiter> depths)
	{
		var line = new char[source.Length];
		Array.Fill(line, ' ');
		foreach (var d in depths)
		{
			line[d.Offset] = (char)('0' + d.Depth % 10);
		}

		return new string(line);
	}

	[Test]
	public async Task MatchedCallParens_ShareTheirGroupsDepth()
	{
		//                     0         1
		//                     0123456789012345
		const string src = "add(sub(1,2),3)";
		var depths = Depths(src);

		//                                     add(sub(1,2),3)
		await Assert.That(Ruler(src, depths)).IsEqualTo("   0   1   1  0");
	}

	[Test]
	public async Task EscapedBracket_IsNotADelimiter()
	{
		// The headline case, lifted from FUN`FOOTER`DISPLAY`LEFT. A lexical matcher pairs the '\['
		// with the ']' that closes '[left(' and mis-depths everything after it.
		const string src = @"ljust(%b\[%b[left(%0)]%b\]%b,%1)";
		var depths = Depths(src);

		//                                    ljust(%b\[%b[left(%0)]%b\]%b,%1)
		await Assert.That(Ruler(src, depths)).IsEqualTo(@"     0      1    2  21         0");
	}

	[Test]
	public async Task BareParenInProse_IsNotADelimiter()
	{
		// No FUNCHAR, so the grammar never opens a function here: the '(' at offset 9 is text, and
		// colouring it would claim a structure the parser does not see.
		//
		// The ')' at 11 *is* strcat's closer, for the same reason: with the '(' being text, the first
		// ')' the grammar meets ends the call and " c)" is trailing prose. That is what the evaluator
		// does too, so the pairing shown here is the true one, not an artefact of ignoring the prose.
		const string src = "strcat(a (b) c)";
		var depths = Depths(src);

		//                                    strcat(a (b) c)
		await Assert.That(Ruler(src, depths)).IsEqualTo("      0    0   ");
	}

	[Test]
	public async Task SourceCopyingCall_ContributesNothingInside()
	{
		// lit() copies its body to the output instead of evaluating it, so the parens in there are
		// literal characters, not structure. Only lit's own parens are a pair.
		const string src = "lit(add(1,2))";
		var depths = Depths(src, name =>
			name.Equals("lit", StringComparison.OrdinalIgnoreCase)
				? SoftcodeCallKind.CopiesArgumentSource
				: SoftcodeCallKind.EvaluatesArguments);

		//                                    lit(add(1,2))
		await Assert.That(Ruler(src, depths)).IsEqualTo("   0        0");
	}

	[Test]
	public async Task UnmatchedOpener_YieldsNoPair()
	{
		const string src = "add(1,2";
		await Assert.That(Depths(src)).IsEmpty();
	}

	[Test]
	public async Task OrphanedCloser_IsNotADelimiter()
	{
		const string src = "add(1,2))";
		var depths = Depths(src);

		//                                    add(1,2))
		await Assert.That(Ruler(src, depths)).IsEqualTo("   0   0 ");
	}

	[Test]
	public async Task MatchPatternPrefix_IsExcluded()
	{
		// A '[' in a $-command's match pattern is glob data compiled to a regex, never parsed as code.
		const string src = "$foo [a]:@pemit %#=say(hi)";
		var depths = Depths(src, parseType: ParseType.CommandList);

		//                                    $foo [a]:@pemit %#=say(hi)
		await Assert.That(Ruler(src, depths)).IsEqualTo("                      0  0");
	}
}
