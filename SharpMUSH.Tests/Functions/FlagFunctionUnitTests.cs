using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class FlagFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[Arguments("orflags(%#,PW)", "1")]
	public async Task Orflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// With no argument these list every flag the game defines — as symbols and as names, not as the
	// type name of an unenumerated stream.
	[Test]
	public async Task FlagsAndLflags_WithNoArgument_ListEveryFlag()
	{
		var symbols = (await Parser.FunctionParse(MarkupText.Plain("flags()")))?.Message!.ToPlainText();
		var names = (await Parser.FunctionParse(MarkupText.Plain("lflags()")))?.Message!.ToPlainText();

		await Assert.That(symbols).DoesNotContain("System.");
		await Assert.That(symbols).Contains("W");
		await Assert.That(names).Contains("WIZARD");
	}

	[Test]
	[Arguments("orlflags(%#,PLAYER WIZARD)", "1")]
	public async Task Orlflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// orlpowers()/andlpowers() are orlflags()/andlflags() over the POWER namespace: one object, a
	// space-separated list of power names (pennmush src/function.c ORLPOWERS → fun_orlflags,
	// src/fundb.c fun_orlflags/fun_andlflags, src/flags.c flaglist_check_long). Expected values
	// from a live PennMUSH 80a1d5b run against a thing holding Boot and Login.
	[Test]
	[Arguments("orlpowers({0}, boot)", "1")]
	[Arguments("orlpowers({0}, poll)", "0")]
	[Arguments("andlpowers({0}, boot login)", "1")]
	[Arguments("andlpowers({0}, boot poll)", "0")]
	[Arguments("andlpowers({0}, BOOT Login)", "1")]
	[Arguments("orlpowers({0}, !poll)", "1")]
	[Arguments("orlpowers({0}, boot ! poll)", "1")]
	[Arguments("andlpowers({0}, boot !)", "#-1 INVALID POWER")]
	[Arguments("orlpowers({0}, nosuchpower)", "0")]
	[Arguments("andlpowers({0}, nosuchpower)", "0")]
	[Arguments("orlpowers({0}, !nosuchpower)", "1")]
	[Arguments("andlpowers({0},)", "#-1 INVALID POWER")]
	[Arguments("orlpowers({0},)", "#-1 INVALID POWER")]
	[Arguments("andlpowers({0}, wizard)", "0")]
	[Arguments("orlpowers({0}, player)", "0")]
	public async Task ListPowersCheckOneObject(string template, string expected)
	{
		var thing = await PoweredThing();
		var result = (await Parser.FunctionParse(MarkupText.Plain(string.Format(template, thing))))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// The first argument is one object, not a list of them: PennMUSH match_thing()s "#1 <thing>"
	// as a single name, which matches nothing.
	[Test]
	public async Task ListPowersDoNotTakeAnObjectList()
	{
		var thing = await PoweredThing();
		var result = (await Parser.FunctionParse(MarkupText.Plain($"orlpowers(#1 {thing}, boot)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// Live PennMUSH 80a1d5b: a bad flag list is #-1 INVALID FLAG in all four flag functions, and an
	// empty list is invalid for the name-list forms but vacuous for the letter forms.
	[Test]
	[Arguments("andflags(%#, W!)", "#-1 INVALID FLAG")]
	[Arguments("orflags(%#, v!)", "#-1 INVALID FLAG")]
	[Arguments("andlflags(%#, !)", "#-1 INVALID FLAG")]
	[Arguments("orlflags(%#, noaccents ! myopic)", "#-1 INVALID FLAG")]
	[Arguments("andlflags(%#,)", "#-1 INVALID FLAG")]
	[Arguments("orlflags(%#,)", "#-1 INVALID FLAG")]
	[Arguments("andflags(%#,)", "1")]
	[Arguments("orflags(%#,)", "0")]
	[Arguments("andlflags(%#, boot)", "0")]
	public async Task FlagListErrorsAndEmptyLists(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	private async Task<DBRef> PoweredThing()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PowerList");
		var obj = (await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>();
		foreach (var name in new[] { "Boot", "Login" })
		{
			var power = await Mediator.Send(new GetPowerQuery(name));
			await Mediator.Send(new SetObjectPowerCommand(obj, power!));
		}
		return thing;
	}

	// Penn testflags.t: hasflag tests
	[Test]
	[Arguments("hasflag(%#, wizard)", "1")]
	[Arguments("hasflag(%#, flunky)", "0")]
	[Arguments("hasflag(%#, puppet)", "0")]
	public async Task Hasflag(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn andlflags.9: invalid syntax (space before !) → #-1 error
	[Test]
	public async Task AndlflagsInvalidSyntax()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("andlflags(%#, connected ! myopic)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// Penn andflags.5: W! → invalid flag string → #-1
	[Test]
	public async Task AndflagsInvalidSyntax()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("andflags(%#, W!)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// Penn orlflags.8: invalid syntax → #-1
	[Test]
	public async Task OrlflagsInvalidSyntax()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("orlflags(%#, noaccents ! myopic)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// Penn testflags.t orflags.3; orflags.1, .2, .5 and .6 are in DbrefFunctionUnitTests.Orflags.
	[Test]
	[Arguments("orflags(%#, v!~)", "1")]
	public async Task OrflagsPenn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn orflags.4: v! → invalid → #-1
	[Test]
	public async Task OrflagsInvalidSyntax()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("orflags(%#, v!)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	// Penn testhastype.t: hastype tests
	[Test]
	[Arguments("hastype(#0, room)", "1")]
	[Arguments("hastype(#1, player)", "1")]
	public async Task HastypePenn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn-oracle verified (tools/oracle, 2026-06-12): unheld, unknown, and empty power names
	// all return 0 — an unknown power is NOT an error in Penn.
	[Test]
	[Arguments("haspower(%#, guest)", "0")]
	[Arguments("haspower(%#, notapower)", "0")]
	[Arguments("haspower(%#,)", "0")]
	public async Task Haspower(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn-oracle verified: a held power reports 1, matched case-insensitively (GuEsT).
	[Test]
	[NotInParallel(SharpMUSH.Tests.Commands.GuestLoginTests.GuestCharacters)]
	public async Task HaspowerGranted()
	{
		var home = new DBRef(0, null);
		var subject = await Mediator.Send(new CreatePlayerCommand("HaspowerSubject", "testpass", home, home, 1));
		var player = await Mediator.CreateStream(new GetPlayerQuery("HaspowerSubject")).FirstAsync();
		var guestPower = await Mediator.Send(new GetPowerQuery("Guest"));
		await Assert.That(guestPower).IsNotNull();
		await Mediator.Send(new SetObjectPowerCommand(new AnySharpObject(player), guestPower!));
		try
		{
			var granted = (await Parser.FunctionParse(MarkupText.Plain($"haspower(#{subject.Number}, GuEsT)")))?.Message!;
			await Assert.That(granted.ToPlainText()).IsEqualTo("1");

			var other = (await Parser.FunctionParse(MarkupText.Plain($"haspower(#{subject.Number}, builder)")))?.Message!;
			await Assert.That(other.ToPlainText()).IsEqualTo("0");
		}
		finally
		{
			// A leftover guest would count as a guest character in GuestLoginTests.
			await Mediator.Send(new UnsetObjectPowerCommand(new AnySharpObject(player), guestPower!));
		}
	}

	/// <summary>
	/// A power answers to PennMUSH's aliases for it, so softcode written against a PennMUSH game
	/// (<c>haspower(%#, tel_anywhere)</c>) reads the same power here (#1508).
	/// </summary>
	[Test]
	[Arguments("tel_anywhere")]
	[Arguments("Tport_Anywhere")]
	[Arguments("TEL_ANYWHERE")]
	public async Task HaspowerAnswersToPennMUSHsAlias(string asked)
	{
		var name = TestIsolationHelpers.GenerateUniqueName("HpAlias");
		var home = new DBRef(0, null);
		var subject = await Mediator.Send(new CreatePlayerCommand(name, "testpass", home, home, 1));
		var player = await Mediator.CreateStream(new GetPlayerQuery(name)).FirstAsync();
		var power = await Mediator.Send(new GetPowerQuery("tel_anywhere"));
		await Assert.That(power?.Name).IsEqualTo("Tport_Anywhere").Because("the alias resolves to the power");
		await Mediator.Send(new SetObjectPowerCommand(new AnySharpObject(player), power!));

		var held = (await Parser.FunctionParse(MarkupText.Plain($"haspower(#{subject.Number}, {asked})")))?.Message!;
		await Assert.That(held.ToPlainText()).IsEqualTo("1");
	}

	// Penn-oracle verified: an invalid object is an error (#-1 NO SUCH OBJECT VISIBLE).
	[Test]
	public async Task HaspowerInvalidObject()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("haspower(#99999, guest)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1");
	}

	[Test]
	public async Task HastypeThing()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HasTypeTest");
		var result = (await Parser.FunctionParse(MarkupText.Plain($"hastype({objDbRef}, thing)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}
}
