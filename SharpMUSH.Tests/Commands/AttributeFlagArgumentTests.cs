using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Covers <c>@set obj/attr=&lt;flaglist&gt;</c>'s argument parsing, which PennMUSH performs in
/// <c>string_to_atrflagsets</c> (<c>src/attrib.c:241-254</c>) BEFORE <c>af_helper</c> ever runs
/// its write gate. The two gates test different things and neither substitutes for the other:
/// the write gate asks what flags the attribute already carries, while this one asks whether the
/// player is even allowed to name the flag they typed.
/// </summary>
public class AttributeFlagArgumentTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private readonly List<TestIsolationHelpers.TestPlayer> _players = [];
	private DBRef _startingRoom;

	private async Task<TestIsolationHelpers.TestPlayer> CreateOwner(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		_players.Add(player);
		var actor = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).AsPlayer;
		var origin = await actor.Location.WithCancellation(CancellationToken.None);
		_startingRoom = origin.Object().DBRef;
		var roomId = await Mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("FlagArgRoom"), actor));
		var room = (await Mediator.Send(new GetObjectNodeQuery(roomId))).AsRoom;
		await Mediator.Send(new MoveObjectCommand(actor, room, _startingRoom, IsSilent: true));
		return player;
	}

	[After(Test)]
	public async Task DisconnectPlayers()
	{
		foreach (var player in _players)
			await ConnectionService.Disconnect(player.Handle);
	}

	private IMUSHCodeParser ParserFor(TestIsolationHelpers.TestPlayer player) =>
		WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

	/// <summary>
	/// Reads <paramref name="attribute"/> as God, so that a <c>mortal_dark</c> attribute is still
	/// visible to the assertion: reading it as its own owner would report "no such attribute" and
	/// make a set-succeeded check indistinguishable from a set-failed one.
	/// </summary>
	private async Task<bool> HasFlag(DBRef who, string attribute, string flag)
	{
		var obj = await Mediator.Send(new GetObjectNodeQuery(who));
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Known;
		var attr = await AttributeService.GetAttributeAsync(god, obj.Known, attribute,
			IAttributeService.AttributeMode.Read, false);

		return attr.IsAttribute && attr.AsAttribute.Last().Flags
			.Any(f => f.Name.Equals(flag, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Everything <paramref name="who"/> was notified of while <paramref name="action"/> ran, read
	/// from the recipient-keyed recorder rather than the session-shared NSubstitute call list.
	/// </summary>
	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	/// <summary>
	/// <c>!See_All(player) &amp;&amp; ((*setbits | *clrbits) &amp; AF_WIZARD)</c> fails the whole
	/// argument. Without it a mortal can set <c>wizard</c> on their own unflagged attribute -
	/// <c>CanSet</c> only ever inspects the flags already on the attribute, and there are none yet.
	/// </summary>
	[Test]
	public async ValueTask MortalNamingWizard_LeavesTheFlagUnset()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await CreateOwner("FlagArgWiz");

		await ParserFor(mortal).CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"&FW{uid} me=value"));
		await ParserFor(mortal).CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@set me/FW{uid}=wizard"));

		await Assert.That(await HasFlag(mortal.DbRef, $"FW{uid}", "wizard")).IsFalse()
			.Because("a player without See_All may not name the wizard flag in either direction");

		// Control: the same command from God does set it, so the assertion above is about the
		// privilege check and not about @set silently failing for everyone.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {mortal.DbRef}/FW{uid}=wizard"));

		await Assert.That(await HasFlag(mortal.DbRef, $"FW{uid}", "wizard")).IsTrue()
			.Because("the flag itself is settable - only the mortal's naming of it was refused");
	}

	/// <summary>
	/// <c>!Hasprivs(player) &amp;&amp; ((*setbits | *clrbits) &amp; AF_MDARK)</c>, the same rule one
	/// privilege level lower: wizard or royalty, not See_All.
	/// </summary>
	[Test]
	public async ValueTask MortalNamingMortalDark_LeavesTheFlagUnset()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await CreateOwner("FlagArgMDark");

		await ParserFor(mortal).CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"&FM{uid} me=value"));
		await ParserFor(mortal).CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@set me/FM{uid}=mortal_dark"));

		await Assert.That(await HasFlag(mortal.DbRef, $"FM{uid}", "mortal_dark")).IsFalse()
			.Because("a player without Hasprivs may not name mortal_dark in either direction");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {mortal.DbRef}/FM{uid}=mortal_dark"));

		await Assert.That(await HasFlag(mortal.DbRef, $"FM{uid}", "mortal_dark")).IsTrue()
			.Because("the flag itself is settable - only the mortal's naming of it was refused");
	}

	/// <summary>
	/// A bare <c>!</c> survives <c>MushText.SplitList</c> (which only drops empty items) with an
	/// empty flag name. Both fallbacks then match something: the symbol comparison matches any
	/// flag whose symbol is the empty string (<c>prefixmatch</c>, where it is seeded), and failing
	/// that <c>StartsWith("")</c> matches the shortest flag name in the list. Either way
	/// <c>@set obj/attr=!</c> acted on a flag the player never named, so this asserts the refusal
	/// itself rather than any one provider's flag table.
	/// </summary>
	[Test]
	public async ValueTask BareBangToken_IsRefusedAsAnUnrecognizedFlag()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();

		// Run as a freshly-created player rather than God: this is the one test here that asserts
		// on a "#-1 ..." notification, and several unrelated suites assert that #1 never receives
		// one anywhere in the session. `case` carries no privilege requirement, so a mortal can
		// name it.
		var owner = await CreateOwner("FlagArgBang");

		await ParserFor(owner).CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"&FB{uid} me=value"));
		await ParserFor(owner).CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@set me/FB{uid}=case"));

		await Assert.That(await HasFlag(owner.DbRef, $"FB{uid}", "case")).IsTrue()
			.Because("precondition: `case` is the shortest flag name, so it is what StartsWith(\"\") selects");

		var messages = await MessagesWhile(owner.DbRef, () =>
			ParserFor(owner).CommandParse(owner.Handle, ConnectionService,
				MarkupText.Plain($"@set me/FB{uid}=!")).AsTask());

		await Assert.That(messages).Contains(ErrorMessages.Returns.UnrecognizedAttributeFlag)
			.Because("a bare ! names no flag, so the whole argument is refused before anything is applied");

		await Assert.That(await HasFlag(owner.DbRef, $"FB{uid}", "case")).IsTrue()
			.Because("the refusal must leave every flag on the attribute untouched");
	}

	/// <summary>
	/// <c>af_helper</c> reports each half of a batch as ONE line naming the whole list
	/// (<c>src/set.c:522-535</c>), built from the REQUESTED bitmask rather than from what actually
	/// changed - so a flag that was not set still appears in the "reset." line, and there is no
	/// per-flag "already set" / "is not set" wording anywhere in Penn.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async ValueTask FlagBatch_IsReportedAsOneLinePerHalf(bool broadcastInStartingRoom)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).AsPlayer;
		var initialRoom = await Mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("FlagInitialRoom"), god));
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Database = options.Database with { DefaultHome = checked((uint)initialRoom.Number) }
		});
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await CreateOwner("FlagArgReport");
		var ownerName = (await Mediator.Send(new GetObjectNodeQuery(owner.DbRef))).Known.Object().Name;

		await ParserFor(owner).CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"&FR{uid} me=value"));
		await ParserFor(owner).CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@set me/FR{uid}=case"));

		var noise = TestIsolationHelpers.GenerateUniqueName("UnrelatedBroadcast");
		TestIsolationHelpers.TestPlayer? witness = null;
		if (broadcastInStartingRoom)
		{
			witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
				WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagWitness");
			_players.Add(witness);
		}

		// Owner and witness start in a test-owned room; the owner must leave it before assertions.
		// A real broadcast there must not pollute this exact-count check.
		// Keep the full recipient window; filtering expected text would hide extra flag reports.
		var messages = await MessagesWhile(owner.DbRef, async () =>
		{
			if (broadcastInStartingRoom)
				await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@remit {_startingRoom}={noise}"));
			// `nospace` is not set, `case` is: report both in one reset line regardless.
			await ParserFor(owner).CommandParse(owner.Handle, ConnectionService,
				MarkupText.Plain($"@set me/FR{uid}=!case !nospace regexp"));
		});
		if (witness is not null)
			await Assert.That(WebAppFactoryArg.Notifications.For(witness.DbRef)).Contains(noise);

		await Assert.That(messages).Contains($"{ownerName}/FR{uid} - case nospace reset.")
			.Because("one line per half, naming the whole requested list in flag-table order");
		await Assert.That(messages).Contains($"{ownerName}/FR{uid} - regexp set.")
			.Because("the set half is reported the same way, as its own single line");
		await Assert.That(messages.Count).IsEqualTo(2)
			.Because("three flag tokens must produce exactly two lines, not one per flag");

		await Assert.That(await HasFlag(owner.DbRef, $"FR{uid}", "case")).IsFalse();
		await Assert.That(await HasFlag(owner.DbRef, $"FR{uid}", "regexp")).IsTrue();
	}

	/// <summary>
	/// <c>AreQuiet(x, y)</c> is <c>Quiet(x) || (Quiet(y) &amp;&amp; Owner(y) == x)</c>
	/// (<c>hdrs/dbdefs.h:198</c>); <c>af_helper</c> suppresses both report lines when it holds, or
	/// when the attribute itself carries <c>AF_Quiet</c>.
	/// </summary>
	[Test]
	public async ValueTask QuietPlayer_GetsNoFlagReport()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await CreateOwner("FlagArgQuiet");

		await ParserFor(owner).CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"&FQ{uid} me=value"));

		// Control: without QUIET the same command does report, so the silence below is the flag's
		// doing and not the command failing.
		var loud = await MessagesWhile(owner.DbRef, () =>
			ParserFor(owner).CommandParse(owner.Handle, ConnectionService,
				MarkupText.Plain($"@set me/FQ{uid}=regexp")).AsTask());

		await Assert.That(loud).IsNotEmpty()
			.Because("precondition: a non-quiet player is told what the batch did");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {owner.DbRef}=QUIET"));

		var quiet = await MessagesWhile(owner.DbRef, () =>
			ParserFor(owner).CommandParse(owner.Handle, ConnectionService,
				MarkupText.Plain($"@set me/FQ{uid}=!regexp")).AsTask());

		await Assert.That(quiet).IsEmpty()
			.Because("AreQuiet(player, thing) suppresses both halves of the report");
		await Assert.That(await HasFlag(owner.DbRef, $"FQ{uid}", "regexp")).IsFalse()
			.Because("the batch must still be APPLIED - only the report is suppressed");
	}

	/// <summary>
	/// <c>af_helper</c> applies <c>AL_FLAGS(atr) &amp;= ~clrf</c> and then
	/// <c>AL_FLAGS(atr) |= setf</c> to the same live bitmask, so a flag named in both directions
	/// ends SET regardless of the order it was typed.
	/// </summary>
	[Test]
	public async ValueTask FlagNamedInBothDirections_EndsSet()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "FlagArgBoth");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&FD{uid} {obj}=value"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {obj}/FD{uid}=wizard"));

		await Assert.That(await HasFlag(obj, $"FD{uid}", "wizard")).IsTrue()
			.Because("precondition: the clear half of the batch needs something to clear");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {obj}/FD{uid}=!wizard wizard"));

		await Assert.That(await HasFlag(obj, $"FD{uid}", "wizard")).IsTrue()
			.Because("clrf is applied before setf, so a flag in both halves survives the batch");
	}
}
