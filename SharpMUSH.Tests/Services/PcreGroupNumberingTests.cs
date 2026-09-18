using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Utilities;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// PennMUSH numbers capture groups as PCRE does: every capturing group in the order its <c>(</c>
/// opens, named or not. .NET numbers the unnamed groups first and the named ones after them, so in
/// <c>(?&lt;first&gt;a)(b)</c> PCRE's group 1 is <c>a</c> and .NET's is <c>b</c>. Softcode sees PCRE's
/// numbering everywhere a group is read by number.
/// </summary>
public class PcreGroupNumberingTests
{
	[Test]
	[Arguments("abc", new[] { 0 })]
	[Arguments("(a)(b)", new[] { 0, 1, 2 })]
	[Arguments("(?<first>a)(b)c", new[] { 0, 2, 1 })]
	[Arguments("(a)(?:b)(?<n>c)(d)", new[] { 0, 1, 3, 2 })]
	[Arguments("(?'n'a)(b)", new[] { 0, 2, 1 })]
	// Lookarounds, comments, escaped parentheses and parentheses in a class capture nothing.
	[Arguments("(?<=x)(?<!y)(?=z)(?!w)(?<n>a)(b)", new[] { 0, 2, 1 })]
	[Arguments(@"(?#x)\((?<n>a)[(](b)", new[] { 0, 2, 1 })]
	[Arguments(@"[\](](?<n>a)(b)", new[] { 0, 2, 1 })]
	// An unnamed group captures nothing where n is on: (?n: for its own group, (?n) to the end of the
	// enclosing one.
	[Arguments("(?<x>a)(?n:(b))(c)", new[] { 0, 2, 1 })]
	[Arguments("(?<x>a)((?n)(b))(c)", new[] { 0, 3, 1, 2 })]
	[Arguments("(?<x>a)(?n)(b)(?-n:(c))(d)", new[] { 0, 2, 1 })]
	[Arguments("(?i)(?<x>a)(b)", new[] { 0, 2, 1 })]
	public async Task NumbersGroupsInOpeningOrder(string pattern, int[] expected)
		=> await Assert.That(string.Join(",", SoftcodeRegex.PcreGroupNumbers(new Regex(pattern)))).IsEqualTo(string.Join(",", expected));

	/// <summary>A pattern the scan cannot account for keeps .NET's numbering rather than a wrong one.</summary>
	[Test]
	[Arguments("(?<n>a)(?<n>b)(c)")]
	public async Task FallsBackToDotNetNumbering(string pattern)
	{
		var regex = new Regex(pattern);
		await Assert.That(string.Join(",", SoftcodeRegex.PcreGroupNumbers(regex))).IsEqualTo(string.Join(",", regex.GetGroupNumbers()));
	}

	[Test]
	public async Task RegexpCommandArgumentsUsePcreNumbering()
	{
		var regex = new Regex("^go (?<dir>\\w+) (\\w+)$");
		var text = MarkupText.Plain("go north fast");

		var arguments = PatternArguments.Capture(regex, isRegex: true, text);

		await Assert.That(arguments["1"].Message!.ToPlainText()).IsEqualTo("north");
		await Assert.That(arguments["2"].Message!.ToPlainText()).IsEqualTo("fast");
		await Assert.That(arguments["dir"].Message!.ToPlainText()).IsEqualTo("north");
	}
}
