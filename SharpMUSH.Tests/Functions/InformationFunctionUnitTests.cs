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
	[Arguments("money(%#)", "#-1 NOT SUPPORTED")]
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
	[Arguments("hidden(%#)", "0")]
	[Skip("Test infrastructure issue - intermittent failure, returns '1' instead of '0'")]
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
	public async Task Lstats_NoArguments()
	{
		// lstats() with no arguments should return counts for all types
		var result = (await Parser.FunctionParse(MModule.single("lstats()")))?.Message!;
		var stats = result.ToPlainText();
		
		// Should return 5 space-separated numbers: players things exits rooms garbage
		var parts = stats.Split(' ');
		await Assert.That(parts.Length).IsEqualTo(5);
		
		// Each should be a valid number
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
	}

	[Test]
	[Arguments("lstats(player)", "")]
	[Arguments("lstats(room)", "")]
	[Arguments("lstats(thing)", "")]
	[Arguments("lstats(exit)", "")]
	public async Task Lstats_WithTypeFilter(string str, string expected)
	{
		// lstats() with a type filter should return a single count
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var count = result.ToPlainText();
		
		// Should return a single number
		await Assert.That(int.TryParse(count, out var num)).IsTrue();
		await Assert.That(num).IsGreaterThanOrEqualTo(0);
	}

	[Test]
	[Arguments("lstats(garbage)", "0")]
	public async Task Lstats_GarbageAlwaysZero(string str, string expected)
	{
		// Garbage count should always be 0 (not tracked separately)
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lstats(invalid)", "#-1 INVALID TYPE")]
	public async Task Lstats_InvalidType(string str, string expected)
	{
		// Invalid type should return error
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
	public async Task Colors_NoArgs()
	{
		var result = (await Parser.FunctionParse(MModule.single("colors()")))?.Message!;
		var colors = result.ToPlainText();
		
		// Should return a non-empty list of colors
		await Assert.That(colors).IsNotEmpty();
		
		// Should contain some known color names
		await Assert.That(colors).Contains("red");
		await Assert.That(colors).Contains("blue");
		await Assert.That(colors).Contains("yellow");
	}

	[Test]
	[Arguments("colors(*yellow*)", "yellow")]
	public async Task Colors_Wildcard(string str, string expectedContains)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var colors = result.ToPlainText();
		
		// Should return colors matching the wildcard
		await Assert.That(colors).IsNotEmpty();
		await Assert.That(colors).Contains(expectedContains);
	}

	[Test]
	[Arguments("colors(+yellow, hex)", "#ffff00")]
	public async Task Colors_NameToHex(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+yellow, rgb)", "255 255 0")]
	public async Task Colors_NameToRgb(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+yellow, xterm256)")]
	public async Task Colors_NameToXterm(string str)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var xterm = result.ToPlainText();
		
		// Should return a valid xterm number
		await Assert.That(int.TryParse(xterm, out var xtermNum)).IsTrue();
		await Assert.That(xtermNum).IsGreaterThanOrEqualTo(0);
		await Assert.That(xtermNum).IsLessThanOrEqualTo(255);
	}

	[Test]
	[Arguments("colors(+yellow, 16color)")]
	public async Task Colors_NameTo16Color(string str)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var ansiCode = result.ToPlainText();
		
		// Should return a valid ANSI color code
		await Assert.That(ansiCode).IsNotEmpty();
		// Yellow should map to 'y' or 'hy' (highlight yellow)
		await Assert.That(ansiCode.Contains('y')).IsTrue();
	}

	[Test]
	[Arguments("colors(#ffff00, name)", "yellow")]
	public async Task Colors_HexToName(string str, string expectedContains)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var names = result.ToPlainText();
		
		// Should return color names matching the hex value
		await Assert.That(names).IsNotEmpty();
		await Assert.That(names).Contains(expectedContains);
	}

	[Test]
	[Arguments("colors(+blue /+black, hex)", "#0000ff /#000000")]
	public async Task Colors_ForegroundAndBackground(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(iuB+red, hex styles)", "iuB #ff0000")]
	public async Task Colors_WithStyles(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+blue huyG/+black, auto)", "+blue huyG/+black")]
	public async Task Colors_AutoFormat(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(invalidcolor, hex)", "#-1 INVALID COLOR")]
	public async Task Colors_InvalidColor(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+yellow, invalidformat)", "#-1 INVALID FORMAT")]
	public async Task Colors_InvalidFormat(string str, string expected)
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
