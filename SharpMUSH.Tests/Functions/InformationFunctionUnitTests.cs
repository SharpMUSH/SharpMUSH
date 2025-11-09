using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class InformationFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("type(%#)", "PLAYER")]
	[Arguments("type(%l)", "ROOM")]
	public async Task Type(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task MudName()
	{
		var result = (await Parser.FunctionParse(MModule.single("mudname()")))?.Message!;
		// Should return a non-empty string
		await Assert.That(result.ToPlainText()).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test, Skip("Not Yet Implemented")]
	public async Task Name()
	{
		var result = (await Parser.FunctionParse(MModule.single("name(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("One");
	}

	[Test]
	[Arguments("alias(%#)", "")]
	public async Task Alias(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("fullname(%#)", "")]
	public async Task Fullname(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotEmpty();
	}

	[Test]
	[Arguments("accname(%#)", "")]
	public async Task Accname(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("iname(%#)", "")]
	public async Task Iname(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("moniker(%#)", "")]
	public async Task Moniker(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("money(%#)", "0")]
	public async Task Money(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("quota(%#)", "0 999999")]
	public async Task Quota(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("powers(%#)", "")]
	public async Task Powers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("findable(%#,%#)", "1")]
	public async Task Findable(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("hidden(%#)", "0")]
	public async Task Hidden(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("playermem()", "0")]
	[Arguments("playermem(%#)", "0")]
	public async Task Playermem(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
	
	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("version()", "")]
	public async Task Version(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("numversion()", "20250102000000")]
	public async Task Numversion(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("nearby(%#,%#)", "1")]
	[Arguments("nearby(%#,%l)", "1")]
	public async Task Nearby(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("first(rloc(%#,0),:)", "%#")]
	[Arguments("first(rloc(%#,1),:)", "%l")]
	public async Task Rloc(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var resultPlain = result.ToPlainText();
		var expectedParsed = (await Parser.FunctionParse(MModule.single(expected)))?.Message!.ToPlainText();
		await Assert.That(resultPlain).IsEqualTo(expectedParsed);
	}

	[Test]
	[Arguments("lstats()", "0 0 0 0 0")]
	public async Task Lstats(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("pidinfo(999)", "#-1 NO SUCH PID")]
	public async Task Pidinfo(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Motd_ReturnsConnectMotd()
	{
		// motd() should return the connect MOTD (empty by default)
		var result = (await Parser.FunctionParse(MModule.single("motd()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task WizMotd_ReturnsWizardMotd()
	{
		// wizmotd() should return the wizard MOTD (empty by default)
		var result = (await Parser.FunctionParse(MModule.single("wizmotd()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task DownMotd_ReturnsDownMotd()
	{
		// downmotd() should return the down MOTD (empty by default)
		var result = (await Parser.FunctionParse(MModule.single("downmotd()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task FullMotd_ReturnsFullMotd()
	{
		// fullmotd() should return the full MOTD (empty by default)
		var result = (await Parser.FunctionParse(MModule.single("fullmotd()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
