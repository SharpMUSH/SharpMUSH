namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Every example in TinyMUX 2.14's help for the functions SharpMUSH took from it, written as the help writes it
/// (spaces after commas included), with the answer the help shows. A TinyMUX name SharpMUSH has under another
/// name runs under the <c>mux</c> dialect, as an imported attribute does.
/// </summary>
public class MuxHelpExampleTests : ServerTestBase
{
	private static IDisposable MuxDialect()
		=> TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { Dialect = "mux" }
		});

	[Test]
	[Arguments("strip(This is a test,is)", "Th  a tet")]
	[Arguments("strip(abcdefgABCDEFG,azGy!7$B)", "bcdefgACDEF")]
	[Arguments("between(1, 10, 5)", "1")]
	[Arguments("between(1, 10, 1)", "0")]
	[Arguments("between(1, 10, 1, 1)", "1")]
	[Arguments("choose(1 2 3,1 0 0)", "1")]
	[Arguments("roman(1999)", "MCMXCIX")]
	[Arguments("crc32(MUX)", "629126998")]
	[Arguments("strmem(This is a test)", "14")]
	[Arguments("strmem([chr(233)])", "2")]
	[Arguments("ledit(apple banana lime plum, banana plum, yellow purple)", "apple yellow lime purple")]
	[Arguments("ledit(apple|banana|lime|plum, banana|lime, yellow, |, +)", "apple+yellow++plum")]
	[Arguments("ledit(a b c, b, x)", "a x c")]
	[Arguments("ledit(a b c, d, x)", "a b c")]
	[Arguments("extract(This is a really neat example, 4, 2)", "really neat")]
	[Arguments("extract(Another@funky@test@for@extract, 3, 3, @)", "test@for@extract")]
	[Arguments("insert(This is a test, 4, new)", "This is a new test")]
	[Arguments("insert(Yet@Another@List, 3, Funky, @)", "Yet@Another@Funky@List")]
	[Arguments("remove(this is a test, is)", "this a test")]
	[Arguments("remove(You can't remove, this)", "You can't remove")]
	[Arguments("remove(How about an o-separated list, w ab, o)", "Hout an o-separated list")]
	public async Task FunctionExamples(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	/// <summary>
	/// TinyMUX refuses to remove more than one word, with #-1 CAN ONLY DELETE ONE ELEMENT; SharpMUSH's remove() is
	/// PennMUSH's and removes each word it is given.
	/// </summary>
	[Test]
	public async Task RemoveTakesSeveralWords()
		=> await Assert.That(await Eval("remove(You can't remove multiple words, You can't)")).IsEqualTo("remove multiple words");

	[Test]
	[Arguments("cpad(a,5,-)", "--a--")]
	[Arguments("cpad(*BAMF*,15)", "    *BAMF*     ")]
	[Arguments("cpad(RGB,31,--+)", "--+--+--+--+--RGB+--+--+--+--+-")]
	[Arguments("cpad(toolong,3)", "toolong")]
	[Arguments("[iabs(4)] [iabs(-4)] [iabs(0)]", "4 4 0")]
	[Arguments("[iadd(2,4)] [iadd(5,3,7,-4)]", "6 11")]
	[Arguments("[idiv(15,3)] [idiv(16,3)] [idiv(17,3)] [idiv(18,3)] [idiv(-17,3)] [idiv(-9,5)]", "5 5 5 6 -5 -1")]
	[Arguments("[imul(3,5)] [imul(3,5,-2)]", "15 -30")]
	[Arguments("[isign(-4)] [isign(4)] [isign(0)] [isign(-1)]", "-1 1 0 -1")]
	[Arguments("isub(5,2)", "3")]
	[Arguments("-[lpad(foo,6)]-", "-foo   -")]
	[Arguments("=[lpad(bar,5,.)]=", "=bar..=")]
	[Arguments("lpad(RGB,31,--+)", "RGB--+--+--+--+--+--+--+--+--+-")]
	[Arguments("lpad(toolong,3)", "toolong")]
	[Arguments("-[rpad(foo,6)]-", "-   foo-")]
	[Arguments("=[rpad(bar,5,.)]=", "=..bar=")]
	[Arguments("rpad(RGB,31,--+)", "--+--+--+--+--+--+--+--+--+-RGB")]
	[Arguments("rpad(toolong,3)", "toolong")]
	[Arguments("lpad(foo,12,=+)", "foo+=+=+=+=+")]
	[Arguments("cpad(foo,12,=+)", "=+=+foo+=+=+")]
	[Arguments("strtrunc(abcdef,3)", "abc")]
	[Arguments("parse(a b c,[strlen(##)])", "1 1 1")]
	[Arguments("ptimefmt($Y,17280000)", "1970")]
	[Arguments("[nonzero(1,yes,no)] [nonzero(0,yes,no)]", "yes no")]
	[Arguments("[secs2time(0,utc)]", "Thu Jan 01 00:00:00 1970")]
	[Arguments("[time2secs(Thu Jan 01 00:00:00 1970,utc)]", "0")]
	public async Task DialectNameExamples(string expression, string expected)
	{
		using var _ = MuxDialect();
		await Assert.That(await Eval(expression)).IsEqualTo(expected);
	}

	/// <summary>lpad(), rpad() and cpad() never cut, even with mux_justify making ljust() and the others cut.</summary>
	[Test]
	public async Task PaddingIgnoresMuxJustify()
	{
		using var _ = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { MuxJustify = true }
		});
		await Assert.That(await Eval("[lpad(toolong,3)] [rpad(toolong,3)] [cpad(toolong,3)] [ljust(toolong,3)]"))
			.IsEqualTo("toolong toolong toolong too");
	}

	[Test]
	public async Task SecsShowsTheDigitsAsked()
		=> await Assert.That(await Eval("secs(local,5)")).Matches(@"^\d+\.\d{5}$");

	[Test]
	public async Task LcmdsAndLattrcmdsExamples()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("MuxLcmds")})");
		await Eval($"[attrib_set({thing}/CMD1,lit($command1:@pemit me=blah))][attrib_set({thing}/CMD2,lit($command #2 *:@pemit me=blah blah))][attrib_set({thing}/CMD3,lit(^* says *:@pemit me=You heard something))]");

		await Assert.That(await Eval($"lcmds({thing})")).IsEqualTo("command1 command #2 *");
		await Assert.That(await Eval($"lcmds({thing},|,$)")).IsEqualTo("command1|command #2 *");
		await Assert.That(await Eval($"lcmds({thing},|,^)")).IsEqualTo("* says *");
		await Assert.That(await Eval($"lattrcmds({thing})")).IsEqualTo("CMD1 CMD2");
	}

	[Test]
	public async Task VerbExample()
	{
		using var _ = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { MuxQueueFunctions = true }
		});
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("MuxVerbHelp")})");
		var marker = TestIsolationHelpers.GenerateUniqueName("xtested");
		var god = WebAppFactoryArg.ExecutorDBRef;
		await Eval($"[attrib_set({thing}/xtest,You {marker} obj.)][attrib_set({thing}/oxtest,just {marker} obj.)]");
		var before = Notifications.CountFor(god);

		await Assert.That(await Eval($"verb({thing}, me, xtest, default msg, oxtest, odefault)")).IsEqualTo(string.Empty);

		await Notifications.WaitForAsync(god, $"You {marker} obj.", startIndex: before);
	}
}
