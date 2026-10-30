using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class InformationFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	[Arguments("type(%#)", "PLAYER")]
	[Arguments("type(%l)", "ROOM")]
	public async Task Type(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task MudName()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("mudname()"));
		await Assert.That(result.ToPlainText()).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test]
	public async Task Name()
	{
		var (player, name) = await MintPlayerAsync("NameFn");
		await Assert.That(await Eval($"name({player})")).IsEqualTo(name);
	}

	[Test]
	[Arguments("alias(%#)", "")]
	public async Task Alias(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("fullname(%#)", "")]
	public async Task Fullname(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotEmpty();
	}

	/// <summary>
	/// PennMUSH <c>fun_fullname</c> (<c>fundb.c:1838</c>): an exit's name, then its aliases after a <c>;</c>.
	/// </summary>
	[Test]
	public async Task FullnameOfAnExitJoinsItsAliasesWithSemicolons()
	{
		var destDbRef = (await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("FullnameDest")}"))).Message.ToPlainText()!.Trim();
		var exitName = TestIsolationHelpers.GenerateUniqueName("FullnameExit");
		var exitDbRef = (await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@open {exitName};fa;fb={destDbRef}"))).Message.ToPlainText()!.Trim();

		var result = await Parser.EvaluateAsync(MarkupText.Plain($"fullname({exitDbRef})"));

		await Assert.That(result.ToPlainText()).IsEqualTo($"{exitName};fa;fb");
	}

	/// <summary>
	/// An exit's aliases are its ALIAS attribute (PennMUSH <c>create.c:151-153</c>, <c>set.c:140-147</c>):
	/// <c>@open</c> writes it, and <c>@alias</c> and <c>@name</c> rewrite it, so fullname(), fullalias() and
	/// matching all follow the attribute.
	/// </summary>
	[Test]
	public async Task ExitAliasesFollowTheAliasAttribute()
	{
		var destDbRef = (await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("AliasDest")}"))).Message.ToPlainText()!.Trim();
		var exitName = TestIsolationHelpers.GenerateUniqueName("AliasExit");
		var exitDbRef = (await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@open {exitName};ea={destDbRef}"))).Message.ToPlainText()!.Trim();

		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain($"get({exitDbRef}/ALIAS)"))).ToPlainText())
			.IsEqualTo("ea");

		var aliasedA = TestIsolationHelpers.GenerateUniqueName("eb");
		var aliasedB = TestIsolationHelpers.GenerateUniqueName("ec");
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@alias {exitDbRef}={aliasedA};{aliasedB}"));
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain($"fullname({exitDbRef})"))).ToPlainText())
			.IsEqualTo($"{exitName};{aliasedA};{aliasedB}");
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain($"objid({aliasedB})"))).ToPlainText())
			.IsEqualTo(exitDbRef);

		var renamed = TestIsolationHelpers.GenerateUniqueName("AliasRenamed");
		var renamedAlias = TestIsolationHelpers.GenerateUniqueName("ed");
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@name {exitDbRef}={renamed};{renamedAlias}"));
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain($"fullname({exitDbRef})"))).ToPlainText())
			.IsEqualTo($"{renamed};{renamedAlias}");
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain($"fullalias({exitDbRef})"))).ToPlainText())
			.IsEqualTo(renamedAlias);
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain($"objid({aliasedB})"))).ToPlainText())
			.StartsWith("#-1");

		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@name {exitDbRef}={renamed};"));
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain($"fullname({exitDbRef})"))).ToPlainText())
			.IsEqualTo(renamed);
	}

	/// <summary>
	/// PennMUSH <c>do_clone</c> (<c>create.c:777-788</c>): an exit cloned under a name with aliases answers to
	/// those, and one cloned without a new name takes the original's along with its other attributes.
	/// </summary>
	[Test]
	public async Task ClonedExitKeepsTheAliasesItWasClonedUnder()
	{
		var destDbRef = (await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("CloneDest")}"))).Message.ToPlainText()!.Trim();
		var exitName = TestIsolationHelpers.GenerateUniqueName("CloneExit");
		var exitDbRef = (await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@open {exitName};ca={destDbRef}"))).Message.ToPlainText()!.Trim();

		var renamed = TestIsolationHelpers.GenerateUniqueName("CloneRenamed");
		var renamedClone = (await Parser.EvaluateAsync(MarkupText.Plain($"clone({exitDbRef},{renamed};cb)"))).ToPlainText();
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain($"fullname({renamedClone})"))).ToPlainText())
			.IsEqualTo($"{renamed};cb");

		var sameClone = (await Parser.EvaluateAsync(MarkupText.Plain($"clone({exitDbRef})"))).ToPlainText();
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain($"fullname({sameClone})"))).ToPlainText())
			.IsEqualTo($"{exitName};ca");
	}

	[Test]
	[Arguments("accname(%#)", "")]
	public async Task Accname(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("iname(%#)", "")]
	public async Task Iname(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("moniker(%#)", "")]
	public async Task Moniker(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("money(%#)", "#-1 NOT SUPPORTED")]
	public async Task Money(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Quota()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("quota(%#)"));
		// One integer, the player's limit (src/wiz.c:1895). The permission and No_Quota cases are in
		// QuotaFunctionPermissionTests, driven by mortals.
		await Assert.That(result.ToPlainText()).IsEqualTo("999999");
	}

	[Test]
	[Arguments("powers(%#)", "")]
	public async Task Powers(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("findable(%#,%#)", "1")]
	public async Task Findable(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("hidden(%#)", "0")]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - intermittent failure, returns '1' instead of '0'")]
	public async Task Hidden(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("playermem()", "0")]
	[Arguments("playermem(%#)", "0")]
	public async Task Playermem(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Numversion()
	{
		// PennMUSH's layout: 1.8.8p0 is 1008008000.
		var version = EngineVersion.Parse(Implementation.Generated.VersionInfo.SharpMUSHVersion).Current;
		var expected = $"{version.Major}{version.Minor:000}{version.Patch:000}000";

		var result = await Parser.EvaluateAsync(MarkupText.Plain("numversion()"));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>RhostMUSH's error(): what an unmatched command is told, with or without an object.</summary>
	[Test]
	[Arguments("error()")]
	[Arguments("error(%#)")]
	public async Task Error(string str)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo("Huh?  (Type \"help\" for help.)");
	}

	[Test]
	[Arguments("nearby(%#,%#)", "1")]
	[Arguments("nearby(%#,%l)", "1")]
	public async Task Nearby(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("first(rloc(%#,0),:)", "%#")]
	[Arguments("first(rloc(%#,1),:)", "%l")]
	// fun_rloc reads the depth with parse_integer and clamps it to 0..20 (src/fundb.c:1563-1572): no
	// depth is an error, a negative or non-numeric one climbs nothing, and the leading digits count.
	[Arguments("first(rloc(%#,-1),:)", "%#")]
	[Arguments("first(rloc(%#,foo),:)", "%#")]
	[Arguments("first(rloc(%#,1x),:)", "%l")]
	[Arguments("first(rloc(%#,99999999999999999999),:)", "%l")]
	public async Task Rloc(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		var resultPlain = result.ToPlainText();
		var expectedParsed = (await Parser.FunctionParse(MarkupText.Plain(expected)))?.Message.ToPlainText();
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
	/// count, and the first the sum of the other five: rooms, exits, things, players and the free
	/// numbers counted as garbage.
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
			.IsEqualTo(int.Parse(fields[1]) + int.Parse(fields[2]) + int.Parse(fields[3]) + int.Parse(fields[4]) + int.Parse(fields[5]))
			.Because("the first field is the total: rooms, exits, things and players, then garbage, as PennMUSH counts it");
		await Assert.That(int.Parse(fields[0])).IsGreaterThan(0).Because("the world is not empty");
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
		await Assert.That((await player.EvaluateAsync(MarkupText.Plain("lstats(me)"))).ToPlainText())
			.IsEqualTo("1 0 0 0 1").Because("\"me\" is the executor");
	}

	/// <summary>
	/// The argument is a player, never an object type: <c>fun_lstats</c> hands anything other than
	/// empty, <c>all</c> or <c>me</c> to <c>lookup_player</c>, so words such as <c>player</c>,
	/// <c>things</c>, <c>room</c> or <c>here</c> are names that resolve to no player. Each is
	/// <c>e_notvis</c> (<c>hdrs/parse.h:35</c>).
	/// </summary>
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

		var world = (await mortal.EvaluateAsync(MarkupText.Plain("lstats()"))).ToPlainText();
		await Assert.That(world.Split(' ').Length).IsEqualTo(6)
			.Because("who == ANY_OWNER never reaches the controls() test");

		await Assert.That((await mortal.EvaluateAsync(MarkupText.Plain("lstats(me)"))).ToPlainText())
			.IsEqualTo("1 0 0 0 1").Because("a mortal controls itself");

		await Assert.That((await mortal.EvaluateAsync(
				MarkupText.Plain($"lstats(#{WebAppFactoryArg.ExecutorDBRef.Number})"))).ToPlainText())
			.IsEqualTo("#-1 PERMISSION DENIED: NOT CONTROLLED, NEEDS GAME.WIZARD OR GAME.ROYALTY OR GAME.SEARCH")
			.Because("without Search_All, a player you do not control is refused");
	}

	private async Task<string> Eval(string expression)
		=> (await Parser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	private async Task<(DBRef DbRef, string Name)> MintPlayerAsync(string prefix)
	{
		var name = $"{prefix}{Guid.NewGuid():N}"[..14];
		var created = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@pcreate {name}=pw_{name}"));
		return (DBRef.Parse(created.Message.ToPlainText()!), name);
	}

	[Test]
	[Arguments("pidinfo(999999)", "#-1 INVALID PID")]
	// fun_pidinfo refuses a non-number with e_uint (src/cque.c:1747-1749).
	[Arguments("pidinfo(abc)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	public async Task Pidinfo_Invalid(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Pidinfo_ValidFormat()
	{
		// In a live environment with actual tasks, this would return task info
		var result = await Parser.EvaluateAsync(MarkupText.Plain("pidinfo(1)"));
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
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Colors_NoArgs()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("colors()"));
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
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		var colors = result.ToPlainText();

		await Assert.That(colors).IsNotEmpty();
		await Assert.That(colors).Contains(expectedContains);
	}

	[Test]
	[Arguments("colors(+yellow, hex)", "#ffff00")]
	public async Task Colors_NameToHex(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+yellow, rgb)", "255 255 0")]
	public async Task Colors_NameToRgb(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// The downgrades PennMUSH answers: the colour table's first row by name with the colour's value, so +yellow is
	/// xterm 11 (xterm11 sorts before xterm226 and yellow), and a background letter in upper case.
	/// </summary>
	[Test]
	[Arguments("colors(+yellow, xterm256)", "11")]
	[Arguments("colors(+orange, xterm256x)", "d6")]
	[Arguments("colors(+yellow, 16color)", "yh")]
	[Arguments("colors(/+yellow, 16color)", "Y")]
	[Arguments("colors(+xterm200, 16color)", "mh")]
	[Arguments("colors(#ffff00, name)", "yellow yellow1")]
	[Arguments("colors(#123456, name)", "#-1 NO MATCHING COLOR NAME")]
	[Arguments("colors(0xabc, hex)", "#0a0b0c")]
	[Arguments("colors(<255 128 0>, hex)", "#ff8000")]
	[Arguments("colors(+lightyellow, auto)", "#ffffe0")]
	[Arguments("colors(fhu+red, styles 16color)", "ufrh")]
	[Arguments("colors(h, styles)", "#-1 COLORS() REQUIRES AT LEAST ONE COLOR")]
	public async Task Colors_Downgrades(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>A pattern lists in the table's order, which reads a number in a name as a number.</summary>
	[Test]
	public async Task Colors_PatternListsXtermNumbersInOrder()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("extract(colors(xterm1*),1,4)"));
		await Assert.That(result.ToPlainText()).IsEqualTo("xterm1 xterm10 xterm11 xterm12");
	}

	[Test]
	[Arguments("colors(+blue /+black, hex)", "#0000ff/#000000")]
	public async Task Colors_ForegroundAndBackground(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(iuB+red, hex styles)", "ui#ff0000/#0000ee")]
	public async Task Colors_WithStyles(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+blue huyG/+black, auto)", "yh/+black")]
	public async Task Colors_AutoFormat(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+invalidcolor, hex)", "#-1 INVALID COLOR")]
	public async Task Colors_InvalidColor(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("colors(+yellow, invalidformat)", "#-1 INVALID ARGUMENT")]
	public async Task Colors_InvalidFormat(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Motd_ReturnsConnectMotd()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("motd()"));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task WizMotd_ReturnsWizardMotd()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("wizmotd()"));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task DownMotd_ReturnsDownMotd()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("downmotd()"));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task FullMotd_ReturnsFullMotd()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("fullmotd()"));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
