using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class FlagFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	[Arguments("andflags(%#,P)", "1")]
	public async Task Andflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orflags(%#,PW)", "1")]
	public async Task Orflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("andlflags(%#,PLAYER)", "1")]
	public async Task Andlflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orlflags(%#,PLAYER WIZARD)", "1")]
	public async Task Orlflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("andlpowers(%#,)", "")]
	public async Task Andlpowers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("orlpowers(%#,)", "")]
	public async Task Orlpowers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	// Penn testflags.t: hasflag tests
	[Test]
	[Arguments("hasflag(%#, wizard)", "1")]
	[Arguments("hasflag(%#, flunky)", "0")]
	[Arguments("hasflag(%#, puppet)", "0")]
	public async Task Hasflag(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn testflags.t: andlflags tests
	[Test]
	[Arguments("andlflags(%#, wizard connected)", "1")]
	[Arguments("andlflags(%#, wizard flunky)", "0")]
	[Arguments("andlflags(%#, wizard !noaccents)", "1")]
	[Arguments("andlflags(%#, wizard !puppet)", "1")]
	[Arguments("andlflags(%#, puppet wizard)", "0")]
	[Arguments("andlflags(%#, noaccents wizard)", "0")]
	[Arguments("andlflags(%#, player connected)", "1")]
	public async Task AndlflagsPenn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn andlflags.9: invalid syntax (space before !) → #-1 error
	[Test]
	public async Task AndlflagsInvalidSyntax()
	{
		var result = (await Parser.FunctionParse(MModule.single("andlflags(%#, connected ! myopic)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// Penn testflags.t: andflags tests
	[Test]
	[Arguments("andflags(%#, Wc)", "1")]
	[Arguments("andflags(%#, W_)", "0")]
	[Arguments("andflags(%#, W~)", "0")]
	[Arguments("andflags(%#, W!~)", "1")]
	[Arguments("andflags(%#, WP)", "1")]
	[Arguments("andflags(%#, WT)", "0")]
	public async Task AndflagsPenn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn andflags.5: W! → invalid flag string → #-1
	[Test]
	public async Task AndflagsInvalidSyntax()
	{
		var result = (await Parser.FunctionParse(MModule.single("andflags(%#, W!)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// Penn testflags.t: orlflags tests
	[Test]
	[Arguments("orlflags(%#, wizard connected)", "1")]
	[Arguments("orlflags(%#, wizard flunky)", "1")]
	[Arguments("orlflags(%#, flunky wizard)", "1")]
	[Arguments("orlflags(%#, myopic noaccents)", "0")]
	[Arguments("orlflags(%#, myopic !noaccents)", "1")]
	[Arguments("orlflags(%#, thing player)", "1")]
	public async Task OrlflagsPenn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn orlflags.8: invalid syntax → #-1
	[Test]
	public async Task OrlflagsInvalidSyntax()
	{
		var result = (await Parser.FunctionParse(MModule.single("orlflags(%#, noaccents ! myopic)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// Penn testflags.t: orflags tests
	[Test]
	[Arguments("orflags(%#, ~W)", "1")]
	[Arguments("orflags(%#, ~_)", "0")]
	[Arguments("orflags(%#, v!~)", "1")]
	[Arguments("orflags(%#, ET)", "0")]
	[Arguments("orflags(%#, EP)", "1")]
	public async Task OrflagsPenn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn orflags.4: v! → invalid → #-1
	[Test]
	public async Task OrflagsInvalidSyntax()
	{
		var result = (await Parser.FunctionParse(MModule.single("orflags(%#, v!)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// Penn testhastype.t: hastype tests
	[Test]
	[Arguments("hastype(#0, room)", "1")]
	[Arguments("hastype(#1, player)", "1")]
	public async Task HastypePenn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task HastypeThing()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HasTypeTest");
		var result = (await Parser.FunctionParse(MModule.single($"hastype({objDbRef}, thing)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}
}
