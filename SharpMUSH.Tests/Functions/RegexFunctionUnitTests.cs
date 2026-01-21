using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class RegexFunctionUnitTests : TestsBase
{
	private IMUSHCodeParser Parser => FunctionParser;

	// regmatch tests
	[Test]
	[Arguments("regmatch(test,test)", "1")] // Exact match
	[Arguments("regmatch(test,t.*t)", "1")] // Pattern matches entire string
	[Arguments("regmatch(test123,t.*t)", "0")] // Pattern doesn't match entire string
	[Arguments("regmatch(test,TEST)", "0")] // Case sensitive
	[Arguments("regmatch(test,tes)", "0")] // Should match entire string only
	[Arguments("regmatch(test,.*)", "1")] // Matches entire string
	public async Task Regmatch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regmatchi(test,TEST)", "1")]
	[Arguments("regmatchi(TeSt,test)", "1")]
	[Arguments("regmatchi(test,tes)", "0")] // Should match entire string only
	public async Task Regmatchi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// regrab tests (from pennfunc.md)
	[Test]
	[Arguments("regrab(This is testing a test,test)", "testing")]
	[Arguments("regrab(This is testing a test,s$)", "This")] // First word ending in 's'
	[Arguments("regrab(one two three,t.*)", "two")]
	public async Task Regrab(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regrabi(This is testing a test,TEST)", "testing")]
	public async Task Regrabi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// regraball tests (from pennfunc.md)
	[Test]
	[Arguments("regraball(This is testing a test,test)", "testing test")]
	[Arguments("regraball(This is testing a test,s$)", "This is")] // All words ending in 's'
	public async Task Regraball(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regraballi(This is testing a TEST,test)", "testing TEST")]
	public async Task Regraballi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// reglmatch tests (from pennfunc.md)
	[Test]
	[Arguments("reglmatch(I am testing a test,test)", "3")]
	[Arguments("reglmatch(I am testing a test,test$)", "5")]
	[Arguments("reglmatch(I am testing a test,notfound)", "0")]
	public async Task Reglmatch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reglmatchi(I am testing a TEST,test$)", "5")]
	public async Task Reglmatchi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reglmatchall(I am testing a test,test,%b,|)", "3|5")]
	public async Task Reglmatchall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regmatchalli(I am testing a TEST,test,%b,|)", "3|5")]
	public async Task Regmatchalli(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// regedit tests (from pennfunc.md)
	[Test]
	[Arguments("regedit(test,t,T)", "Test")] // First match only
	[Arguments("regedit(test,e,a)", "tast")] // Simple replacement
	public async Task Regedit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regediti(test,T,X)", "Xest")] // Case insensitive
	public async Task Regediti(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regeditall(test,t,T)", "TesT")] // All matches
	// Note: The capstr function would need to be implemented for this test to work fully
	// [Arguments("regeditall(this test is the best string,(.)est,capstr($1)rash)", "this Trash is the Brash string")]
	public async Task Regeditall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regeditalli(TesT,t,X)", "XesX")] // Case insensitive, all matches
	public async Task Regeditalli(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// reswitch tests
	[Test]
	[Arguments("reswitch(test,t.*,match)", "match")]
	[Arguments("reswitch(test,x.*,nomatch,t.*,match)", "match")]
	[Arguments("reswitch(test,x.*,nomatch,default)", "default")]
	public async Task Reswitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchi(TEST,t.*,match)", "match")]
	public async Task Reswitchi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchall(test,t.*,match1,e.*,match2)", "match1 match2")]
	public async Task Reswitchall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchalli(TEST,t.*,match1,e.*,match2)", "match1 match2")]
	public async Task Reswitchalli(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// regrep tests - skipped as they require attribute service
	[Test]
	[Skip("Requires attribute service integration")]
	[Arguments("regrep(#0,*,pattern)", "")]
	public async Task Regrep(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Requires attribute service integration")]
	[Arguments("regrepi(#0,*,pattern)", "")]
	public async Task Regrepi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
