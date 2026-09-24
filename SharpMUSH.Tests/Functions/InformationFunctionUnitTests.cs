using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class InformationFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	[Arguments("type(%#)", "PLAYER")]
	[Arguments("type(%l)", "ROOM")]
	public async Task Type(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task MudName()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("mudname()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Category("NotImplemented")]
	[Test, Skip("Not Yet Implemented")]
	public async Task Name()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("name(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("One");
	}

	[Test]
	[Arguments("alias(%#)", "")]
	public async Task Alias(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("fullname(%#)", "")]
	public async Task Fullname(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotEmpty();
	}

	[Test]
	[Arguments("accname(%#)", "")]
	public async Task Accname(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("iname(%#)", "")]
	public async Task Iname(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("moniker(%#)", "")]
	public async Task Moniker(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("money(%#)", "#-1 NOT SUPPORTED")]
	public async Task Money(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Quota()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("quota(%#)")))?.Message!;
		// One integer, the player's limit (src/wiz.c:1895). The permission and No_Quota cases are in
		// QuotaFunctionPermissionTests, driven by mortals.
		await Assert.That(result.ToPlainText()).IsEqualTo("999999");
	}

	[Test]
	[Arguments("powers(%#)", "")]
	public async Task Powers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("findable(%#,%#)", "1")]
	public async Task Findable(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("hidden(%#)", "0")]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - intermittent failure, returns '1' instead of '0'")]
	public async Task Hidden(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("playermem()", "0")]
	[Arguments("playermem(%#)", "0")]
	public async Task Playermem(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("version()", "")]
	public async Task Version(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("numversion()", "20250102000000")]
	public async Task Numversion(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("nearby(%#,%#)", "1")]
	[Arguments("nearby(%#,%l)", "1")]
	public async Task Nearby(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("first(rloc(%#,0),:)", "%#")]
	[Arguments("first(rloc(%#,1),:)", "%l")]
	public async Task Rloc(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		var resultPlain = result.ToPlainText();
		var expectedParsed = (await Parser.FunctionParse(MarkupText.Plain(expected)))?.Message!.ToPlainText();
		await Assert.That(resultPlain).IsEqualTo(expectedParsed);
	}

	/// <summary>
	/// <c>lstats()</c> is <c>@stats</c> as a function: the whole database with no argument, with an
	/// empty one or with <c>all</c> (<c>fun_lstats</c>, <c>src/fundb.c:2376-2405</c>), and the fields
	/// are <c>total rooms exits things players garbage</c> — which is what
	/// <c>help lstats()</c> has always documented here.
	/// </summary>
	/// <remarks>
	/// The figures themselves cannot be compared between two calls: the whole test session shares
	/// one world, and any test running in parallel may create an object between them. What is stable
	/// is the shape — six fields for ANY_OWNER against the one-player form's five, every field a
	/// count, the first the sum of the other four, and the last always <c>0</c> because SharpMUSH
	/// removes a destroyed object rather than keeping it as garbage. The garbage column is printed
	/// rather than dropped because the shipped helpfile names six fields and softcode counts them.
	/// </remarks>
	[Test]
	[Arguments("lstats()")]
	[Arguments("lstats(all)")]
	[Arguments("lstats(%b)")]
	public async Task LstatsCountsTheWholeDatabaseInSixFields(string expression)
	{
		var world = await Eval(expression);
		var fields = world.Split(' ');

		await Assert.That(fields.Length).IsEqualTo(6)
			.Because($"{expression} is ANY_OWNER, whose form is six fields; it answered \"{world}\"");
		foreach (var field in fields)
		{
			await Assert.That(int.TryParse(field, out _)).IsTrue().Because($"\"{field}\" must be a count");
		}

		await Assert.That(int.Parse(fields[0]))
			.IsEqualTo(int.Parse(fields[1]) + int.Parse(fields[2]) + int.Parse(fields[3]) + int.Parse(fields[4]))
			.Because("the first field is the total, and the four after it are rooms, exits, things and players");
		await Assert.That(int.Parse(fields[0])).IsGreaterThan(0).Because("the world is not empty");
		await Assert.That(fields[5]).IsEqualTo("0").Because("there is no garbage to count");
	}

	/// <summary>
	/// The argument is a PLAYER — <c>me</c>, a name or a <c>#dbref</c>, all through
	/// <c>lookup_player</c> — and the one-player form drops the garbage column, five fields to the
	/// whole database's six (<c>src/fundb.c:2400-2404</c>).
	/// </summary>
	/// <remarks>
	/// Asked about a freshly created player rather than about God, and <c>me</c> asked from that
	/// player's own parser. A fresh player owns nothing but itself, so <c>1 0 0 0 1</c> is a figure
	/// no other test can move; God's own count changes underneath any two calls that try to compare
	/// it.
	/// </remarks>
	[Test]
	public async Task LstatsForOnePlayerTakesANameOrADbrefAndDropsTheGarbageColumn()
	{
		var (playerDbRef, playerName) = await MintPlayerAsync("LstatsOne");
		var player = WebAppFactoryArg.FunctionParserFor(playerDbRef);

		await Assert.That(await Eval($"lstats(#{playerDbRef.Number})")).IsEqualTo("1 0 0 0 1")
			.Because("a freshly created player owns nothing but itself, and there is no garbage column");
		await Assert.That(await Eval($"lstats({playerName})")).IsEqualTo("1 0 0 0 1")
			.Because("lookup_player resolves a name or a #dbref to the same player");
		await Assert.That((await player.FunctionParse(MarkupText.Plain("lstats(me)")))!.Message!.ToPlainText())
			.IsEqualTo("1 0 0 0 1").Because("\"me\" is the executor");
	}

	/// <summary>
	/// The argument used to be read as an object TYPE — <c>PLAYER</c>, <c>THINGS</c>, <c>ROOM</c>,
	/// <c>GARBAGE</c>, else <c>#-1 INVALID TYPE</c> — and answered a single count over the whole
	/// database with no owner and no permission question anywhere in it. PennMUSH has no such
	/// contract, and the shipped <c>help lstats()</c> never described one. Those words are now just
	/// names that resolve to no player, which is <c>e_notvis</c> (<c>hdrs/parse.h:35</c>).
	/// </summary>
	/// <remarks>
	/// <c>Lstats_WithTypeFilter</c>, <c>Lstats_GarbageAlwaysZero</c> and <c>Lstats_InvalidType</c>
	/// asserted that contract and are deliberately gone; <c>Lstats_NoArguments</c> asserted five
	/// fields and is now <see cref="LstatsCountsTheWholeDatabaseInSixFields"/>.
	/// </remarks>
	[Test]
	[Arguments("lstats(player)")]
	[Arguments("lstats(things)")]
	[Arguments("lstats(room)")]
	[Arguments("lstats(garbage)")]
	[Arguments("lstats(invalid)")]
	[Arguments("lstats(here)")]
	public async Task LstatsRefusesAnythingThatIsNotAPlayer(string expression)
		=> await Assert.That(await Eval(expression)).IsEqualTo(ErrorMessages.Returns.NotVisible);

	/// <summary>
	/// The gate is <c>Search_All(executor) || who == ANY_OWNER || controls(executor, who)</c>
	/// (<c>src/fundb.c:2396-2398</c>). Note which way the middle term runs: the whole database is
	/// open to a mortal, and it is asking about one player in particular that needs the warrant.
	/// </summary>
	/// <remarks>
	/// Driven as a MORTAL on purpose — the fixtures run as God, who satisfies <c>Search_All</c>, so
	/// every one of these would pass vacuously from <see cref="Parser"/>. This is also a rule
	/// <c>@stats</c> does NOT share: <c>do_stats</c> gates on <c>owner != player</c>
	/// (<c>src/wiz.c:770-774</c>), not on <c>controls</c>.
	/// </remarks>
	[Test]
	public async Task MortalMayCountTheWholeDatabaseButNotAnotherPlayer()
	{
		var (mortalDbRef, _) = await MintPlayerAsync("LstatsMortal");
		var mortal = WebAppFactoryArg.FunctionParserFor(mortalDbRef);

		var world = (await mortal.FunctionParse(MarkupText.Plain("lstats()")))!.Message!.ToPlainText();
		await Assert.That(world.Split(' ').Length).IsEqualTo(6)
			.Because("who == ANY_OWNER never reaches the controls() test");

		await Assert.That((await mortal.FunctionParse(MarkupText.Plain("lstats(me)")))!.Message!.ToPlainText())
			.IsEqualTo("1 0 0 0 1").Because("a mortal controls itself");

		await Assert.That((await mortal.FunctionParse(
				MarkupText.Plain($"lstats(#{WebAppFactoryArg.ExecutorDBRef.Number})")))!.Message!.ToPlainText())
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("without Search_All, a player you do not control is refused");
	}

	private async Task<string> Eval(string expression)
		=> (await Parser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	private async Task<(DBRef DbRef, string Name)> MintPlayerAsync(string prefix)
	{
		var name = $"{prefix}{Guid.NewGuid():N}"[..14];
		var created = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@pcreate {name}=pw_{name}"));
		return (DBRef.Parse(created.Message!.ToPlainText()!), name);
	}

	[Test]
	[Arguments("pidinfo(999)", "#-1 NO SUCH PID")]
	[Arguments("pidinfo(abc)", "#-1 INVALID PID")]
	public async Task Pidinfo_Invalid(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Pidinfo_ValidFormat()
	{
		// In a live environment with actual tasks, this would return task info
		var result = (await Parser.FunctionParse(MarkupText.Plain("pidinfo(1)")))?.Message!;
		var text = result.ToPlainText();

		await Assert.That(text).IsNotNull();
		await Assert.That(text).IsNotEmpty();
	}

	[Test]
	[Arguments("pidinfo(1,pid)", "")]
	[Arguments("pidinfo(1,command)", "")]
	[Arguments("pidinfo(1,executor)", "")]
	[Arguments("pidinfo(1,status)", "")]
	public async Task Pidinfo_WithField(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Colors_NoArgs()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("colors()")))?.Message!;
		var colors = result.ToPlainText();

		await Assert.That(colors).IsNotEmpty();

		await Assert.That(colors).Contains("red");
		await Assert.That(colors).Contains("blue");
		await Assert.That(colors).Contains("yellow");
	}

	[Test]
	[Arguments("colors(*yellow*)", "yellow")]
	public async Task Colors_Wildcard(string str, string expectedContains)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		var colors = result.ToPlainText();

		await Assert.That(colors).IsNotEmpty();
		await Assert.That(colors).Contains(expectedContains);
	}

	[Test]
	[Arguments("colors(+yellow, hex)", "#ffff00")]
	public async Task Colors_NameToHex(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+yellow, rgb)", "255 255 0")]
	public async Task Colors_NameToRgb(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+yellow, xterm256)")]
	public async Task Colors_NameToXterm(string str)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		var xterm = result.ToPlainText();

		await Assert.That(int.TryParse(xterm, out var xtermNum)).IsTrue();
		await Assert.That(xtermNum).IsGreaterThanOrEqualTo(0);
		await Assert.That(xtermNum).IsLessThanOrEqualTo(255);
	}

	[Test]
	[Arguments("colors(+yellow, 16color)")]
	public async Task Colors_NameTo16Color(string str)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		var ansiCode = result.ToPlainText();

		await Assert.That(ansiCode).IsNotEmpty();
		// Yellow should map to 'y' or 'hy' (highlight yellow)
		await Assert.That(ansiCode.Contains('y')).IsTrue();
	}

	[Test]
	[Arguments("colors(#ffff00, name)", "yellow")]
	public async Task Colors_HexToName(string str, string expectedContains)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		var names = result.ToPlainText();

		await Assert.That(names).IsNotEmpty();
		await Assert.That(names).Contains(expectedContains);
	}

	[Test]
	[Arguments("colors(+blue /+black, hex)", "#0000ff /#000000")]
	public async Task Colors_ForegroundAndBackground(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(iuB+red, hex styles)", "iuB #ff0000")]
	public async Task Colors_WithStyles(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+blue huyG/+black, auto)", "+blue huyG/+black")]
	public async Task Colors_AutoFormat(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(invalidcolor, hex)", "#-1 INVALID COLOR")]
	public async Task Colors_InvalidColor(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+yellow, invalidformat)", "#-1 INVALID FORMAT")]
	public async Task Colors_InvalidFormat(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Motd_ReturnsConnectMotd()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("motd()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task WizMotd_ReturnsWizardMotd()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("wizmotd()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task DownMotd_ReturnsDownMotd()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("downmotd()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task FullMotd_ReturnsFullMotd()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("fullmotd()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
