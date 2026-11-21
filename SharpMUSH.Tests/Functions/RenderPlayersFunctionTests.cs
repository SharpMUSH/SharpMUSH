using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// renderplayers(): player names in a text replaced by what an attribute makes of them, with %0 the
/// dbref, %1 the name as written and %2 the whole match. Every player here has a unique name, so the
/// shared world's other players never match.
/// </summary>
public class RenderPlayersFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task God(string command)
		=> await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<MString> Eval(string expression)
		=> await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain(expression));

	private async Task<string> Plain(string expression) => (await Eval(expression)).ToPlainText();

	private Task<DBRef> Player(string name)
		=> TestIsolationHelpers.CreateNamedTestPlayerAsync(WebAppFactoryArg.Services, Mediator, name);

	/// <summary>A thing whose LINK attribute writes <c>&lt;%0|%1|%2&gt;</c>, so each match shows what it was given.</summary>
	private async Task<string> Linker()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "RpLinker");
		await God($"&LINK {thing}=<%0|%1|%2>");
		return thing.ToString();
	}

	/// <summary>A name unique to this run that fits the player name length limit: letters and digits, 14 long.</summary>
	private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

	private static string Ref(DBRef dbref) => $"#{dbref.Number}";

	[Test]
	public async ValueTask FirstOccurrenceIsLinkedByDefault()
	{
		var name = Unique("RpJim");
		var jim = await Player(name);
		var linker = await Linker();

		var result = await Plain($"[renderplayers({linker}/LINK,A pose for {name.ToLowerInvariant()} and {name} again.)]");

		await Assert.That(result)
			.IsEqualTo($"A pose for <{Ref(jim)}|{name.ToLowerInvariant()}|{name.ToLowerInvariant()}> and {name} again.");
	}

	[Test]
	public async ValueTask CountLinksThatManyTimesZeroLinksAllNegativeTurnsItOff()
	{
		var name = Unique("RpCount");
		var who = await Player(name);
		var linker = await Linker();
		var text = $"{name} {name} {name}";
		var link = $"<{Ref(who)}|{name}|{name}>";

		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{text},2)]")).IsEqualTo($"{link} {link} {name}");
		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{text},0)]")).IsEqualTo($"{link} {link} {link}");
		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{text},-1)]")).IsEqualTo(text);
		await Assert.That(await Plain($"[renderplayers(#-1/NOWHERE,{text},-1)]")).IsEqualTo(text)
			.Because("a count of -1 returns the text without looking the attribute up");
	}

	[Test]
	public async ValueTask AliasesMatchUnlessTurnedOff()
	{
		var name = Unique("RpAliased");
		var alias = Unique("RpNick");
		var who = await Player(name);
		await God($"&ALIAS {Ref(who)}={alias}");
		var linker = await Linker();

		await Assert.That(await Plain($"[renderplayers({linker}/LINK,Hi {alias}!)]"))
			.IsEqualTo($"Hi <{Ref(who)}|{alias}|{alias}>!");
		await Assert.That(await Plain($"[renderplayers({linker}/LINK,Hi {alias}!,1,,noalias)]"))
			.IsEqualTo($"Hi {alias}!");
	}

	[Test]
	public async ValueTask EachUniqueMatchHasItsOwnCount()
	{
		var name = Unique("RpUnique");
		var alias = Unique("RpTag");
		var who = await Player(name);
		await God($"&ALIAS {Ref(who)}={alias}");
		var linker = await Linker();
		var lower = name.ToLowerInvariant();

		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{name} {alias} {lower} {alias})]"))
			.IsEqualTo($"<{Ref(who)}|{name}|{name}> <{Ref(who)}|{alias}|{alias}> {lower} {alias}")
			.Because("a name and an alias are different matches, and a name in another case is the same match");
	}

	[Test]
	public async ValueTask OnlyWholeWordsMatch()
	{
		var name = Unique("RpWord");
		await Player(name);
		var linker = await Linker();
		var text = $"x{name} {name}x {name}9";

		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{text},0)]")).IsEqualTo(text);
	}

	[Test]
	public async ValueTask ThePlayerListLimitsWhoIsLinked()
	{
		var inName = Unique("RpIn");
		var outName = Unique("RpOut");
		var inside = await Player(inName);
		var outside = await Player(outName);
		var linker = await Linker();

		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{inName} and {outName},1,{Ref(inside)} {linker} junk)]"))
			.IsEqualTo($"<{Ref(inside)}|{inName}|{inName}> and {outName}");
		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{inName} and {outName},1,)]"))
			.IsEqualTo($"<{Ref(inside)}|{inName}|{inName}> and <{Ref(outside)}|{outName}|{outName}>")
			.Because("an empty list means every player");
	}

	[Test]
	public async ValueTask FirstNamesMatchOnlyWithTheOptionAndFullNamesWin()
	{
		var first = Unique("RpFirst");
		var full = $"{first} Bob";
		var who = await Player(full);
		var linker = await Linker();
		var text = $"{first} met {full}.";

		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{text},0)]"))
			.IsEqualTo($"{first} met <{Ref(who)}|{full}|{full}>.");
		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{text},0,,first)]"))
			.IsEqualTo($"<{Ref(who)}|{first}|{first}> met <{Ref(who)}|{full}|{full}>.");
	}

	[Test]
	public async ValueTask AFirstNameTwoPlayersShareLinksNeither()
	{
		var first = Unique("RpShared");
		await Player($"{first} Ann");
		await Player($"{first} Sue");
		var linker = await Linker();

		await Assert.That(await Plain($"[renderplayers({linker}/LINK,Hello {first}.,0,,first)]"))
			.IsEqualTo($"Hello {first}.");
	}

	[Test]
	public async ValueTask APrefixIsRequiredAndPassedInTheWholeMatch()
	{
		var name = Unique("RpAt");
		var who = await Player(name);
		var linker = await Linker();

		await Assert.That(await Plain($"[renderplayers({linker}/LINK,@{name}\\, hi! {name} mail@{name},0,,prefix:@)]"))
			.IsEqualTo($"<{Ref(who)}|{name}|@{name}>, hi! {name} mail@{name}");
	}

	[Test]
	public async ValueTask MarkupAroundTheNamesIsKept()
	{
		var name = Unique("RpAnsi");
		await Player(name);
		var linker = await Linker();

		var result = await Eval($"[renderplayers({linker}/LINK,[ansi(r,Red text)] and {name})]");

		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[")
			.Because("the colour of the text around a name survives");
		await Assert.That(result.ToPlainText()).StartsWith("Red text and <");
	}

	/// <summary>A sound right after a name is outside the match, so it survives an attribute that drops %1.</summary>
	[Test]
	public async ValueTask APointAfterANameStaysInTheText()
	{
		var name = Unique("RpSound");
		await Player(name);
		var linker = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "RpSoundLinker");
		await God($"&LINK {linker}=X");

		var result = await Eval($"[renderplayers({linker}/LINK,[sound(ding.wav)]{name}[sound(dong.wav)] waves)]");

		await Assert.That(result.ToPlainText()).IsEqualTo("X waves");
		var html = result.Render(MarkupFormat.Html);
		await Assert.That(html).Contains("ding.wav");
		await Assert.That(html).Contains("dong.wav");
	}

	[Test]
	public async ValueTask AnAliasChangeIsSeenAtOnce()
	{
		var name = Unique("RpAlias");
		var first = Unique("RpNickA");
		var second = Unique("RpNickB");
		var who = await Player(name);
		var linker = await Linker();
		await God($"&ALIAS {Ref(who)}={first}");
		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{first} {second},0)]"))
			.IsEqualTo($"<{Ref(who)}|{first}|{first}> {second}");

		await God($"&ALIAS {Ref(who)}={second}");
		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{first} {second},0)]"))
			.IsEqualTo($"{first} <{Ref(who)}|{second}|{second}>");

		await God($"&ALIAS {Ref(who)}");
		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{first} {second},0)]"))
			.IsEqualTo($"{first} {second}");
	}

	[Test]
	public async ValueTask ARenameIsSeenAtOnce()
	{
		var before = Unique("RpOld");
		var after = Unique("RpNew");
		var who = await Player(before);
		var linker = await Linker();
		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{before})]")).StartsWith("<");

		await God($"@name {Ref(who)}={after}");

		await Assert.That(await Plain($"[renderplayers({linker}/LINK,{before} {after})]"))
			.IsEqualTo($"{before} <{Ref(who)}|{after}|{after}>");
	}

	[Test]
	[Arguments("x", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("1.5", "#-1 ARGUMENT MUST BE INTEGER")]
	public async ValueTask ACountThatIsNotAnIntegerIsRefused(string count, string expected)
		=> await Assert.That(await Plain($"[renderplayers(me/LINK,text,{count})]")).IsEqualTo(expected);

	[Test]
	public async ValueTask AnUnknownOptionIsRefused()
		=> await Assert.That(await Plain("[renderplayers(me/LINK,text,1,,first loud)]")).IsEqualTo("#-1 INVALID OPTION LOUD");

	[Test]
	public async ValueTask ALambdaWorksAsTheAttribute()
	{
		var name = Unique("RpLambda");
		await Player(name);

		await Assert.That(await Plain($"[renderplayers(#lambda/<\\%1>,Hey {name})]")).IsEqualTo($"Hey <{name}>");
	}

	/// <summary>
	/// Three hundred players and a text of over 150,000 characters naming each of them many times: every
	/// occurrence is linked, and the scan stays well under the time a pose can wait.
	/// </summary>
	[Test]
	public async ValueTask ManyPlayersInALargeText()
	{
		var names = new List<string>();
		for (var i = 0; i < 300; i++)
		{
			var name = Unique("RpBulk");
			await Player(name);
			names.Add(name);
		}

		var linker = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "RpBulkLinker");
		await God($"&LINK {linker}=*");
		// Five passes over every name fit in one attribute; the text is four copies of it.
		var passes = string.Concat(Enumerable.Repeat(names, 5).SelectMany(n => n).Select(n => $"{n} filler words, "));
		await God($"&TEXT {linker}={passes}");
		var text = $"[repeat(get({linker}/TEXT),4)]";
		await Assert.That(int.Parse(await Plain($"[strlen({text})]"))).IsGreaterThan(150_000);

		// Warm the name index, then time a read that uses it.
		await Plain($"[renderplayers({linker}/LINK,warm)]");
		var watch = Stopwatch.StartNew();
		var result = await Plain($"[renderplayers({linker}/LINK,{text},0)]");
		watch.Stop();

		await Assert.That(result.Count(c => c == '*')).IsEqualTo(300 * 5 * 4);
		await Assert.That(names.Any(result.Contains)).IsFalse();
		await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(30));
	}
}
