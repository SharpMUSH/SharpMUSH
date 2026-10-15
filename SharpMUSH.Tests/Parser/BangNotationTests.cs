using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Bang notation, an idea from RhostMUSH: <c>!name(...)</c> answers 1 or 0 about the call's result, and only
/// where a call is recognised.
/// </summary>
public class BangNotationTests : ServerTestBase
{
	[Test]
	[Arguments("!strlen(abc)", "0")]
	[Arguments("!!strlen(abc)", "1")]
	[Arguments("!strlen()", "1")]
	[Arguments("!!strlen()", "0")]
	[Arguments("!lit(abc)", "1")]
	[Arguments("!!lit(3abc)", "1")]
	[Arguments("!lit(-2)", "0")]
	[Arguments("!$null(x)", "1")]
	[Arguments("!!$null(x)", "0")]
	[Arguments("!$lit(0)", "0")]
	[Arguments("!!$lit(0)", "1")]
	[Arguments("!^lit(#-1 NO)", "1")]
	[Arguments("!!^lit(#-1 NO)", "0")]
	[Arguments("!!^lit(abc)", "1")]
	[Arguments("!^lit(0)", "1")]
	[Arguments("[!strlen(abc)]", "0")]
	[Arguments("[!!strlen(abc)]x", "1x")]
	[Arguments("add(!strlen(abc),5)", "5")]
	[Arguments("add(!!strlen(abc),5)", "6")]
	[Arguments("[if(!!$squish(%b),yes,no)]", "no")]
	public async Task NegatesOrTestsTheResult(string code, string expected) =>
		await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("![strlen(abc)]", "!3")]
	[Arguments("!abc", "!abc")]
	[Arguments("!0", "!0")]
	[Arguments("x!strlen(abc)", "x!strlen(abc)")]
	[Arguments("!!!strlen(abc)", "!!!strlen(abc)")]
	[Arguments("!nosuchfunction(abc)", "!nosuchfunction(abc)")]
	[Arguments("add(1,2)!strlen(abc)", "3!strlen(abc)")]
	public async Task StaysTextWhereNoCallIsRecognised(string code, string expected) =>
		await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	public async Task UnknownFunctionInBracketsKeepsItsBangs() =>
		await Assert.That(await Eval("[!nosuchfunction(abc)]")).StartsWith("#-1 FUNCTION (!NOSUCHFUNCTION) NOT FOUND");

	[Test]
	public async Task WorksOnAtFunctions()
	{
		var unique = Guid.NewGuid().ToString("N")[..8];
		await Cmd($"&BANG{unique} me=%0");
		await Cmd($"@function bang{unique}=me,BANG{unique}");

		await Assert.That(await Eval($"[bang{unique}(0)] [!bang{unique}(0)] [!!bang{unique}(7)] [!$bang{unique}()]"))
			.IsEqualTo("0 1 1 1");
	}

	[Test]
	[Arguments("[setq(n,5)]%q<!n>%q<!!n>", "01")]
	[Arguments("[setq(e,)]%q<!$e>%q<!!$e>", "10")]
	[Arguments("[setq(t,#-1)]%q<!^t>%q<!!^t>", "10")]
	[Arguments("%q<!$unsetregister>", "1")]
	public async Task WorksOnRegisters(string code, string expected) =>
		await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("[iter(0 2,%i!0)]", "1 0")]
	[Arguments("[iter(0 2,%i!!0)]", "0 1")]
	[Arguments("[iter(a,iter(b,%i!!$1))]", "1")]
	[Arguments("[iter(a,%i!5)]", "#-1 REGISTER OUT OF RANGE")]
	public async Task WorksOnIterationText(string code, string expected) =>
		await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	public async Task WorksOnDolistElements()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("Bang");
		await Cmd($"@dolist/inline 0 3=@pemit me={token}-%d!0-%d!!0");

		await Assert.That(Notifications.For(WebAppFactoryArg.ExecutorDBRef).Count(m => m == $"{token}-1-0")).IsEqualTo(1);
		await Assert.That(Notifications.For(WebAppFactoryArg.ExecutorDBRef).Count(m => m == $"{token}-0-1")).IsEqualTo(1);
	}

	/// <summary>Every <c>&gt; think</c> example in <c>help bang notation</c> gives the line written under it.</summary>
	[Test]
	public async Task HelpExamplesGiveTheirOutput()
	{
		var text = await File.ReadAllTextAsync(Path.Join(TestPaths.Helpfiles.FullName, "bang-notation.md"));
		var examples = Regex.Matches(text, @"^> think (?<code>[^\n]*)\n(?<output>[^\n]*)$", RegexOptions.Multiline);
		await Assert.That(examples.Count).IsGreaterThan(0);

		foreach (Match example in examples)
		{
			await Assert.That(await Eval(example.Groups["code"].Value)).IsEqualTo(example.Groups["output"].Value)
				.Because(example.Groups["code"].Value);
		}
	}
}
