using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

public class CommunicationCommandTests
{
	private const string TestChannelName = "Public";
	private const string TestChannelPrivilege = "Open";
	private const int TestPlayerDbRef = 1;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Creates the channel once for the session; tests starting together would otherwise race to
	/// create it.
	/// </summary>
	private static readonly RunOnce ChannelSetup = new();

	[Before(Test)]
	public Task SetupTestChannel() => ChannelSetup.RunAsync(CreateTestChannelAsync);

	private async Task CreateTestChannelAsync()
	{
		var player = (await Database.GetObjectNodeAsync(new DBRef(TestPlayerDbRef)))
			.Expect<SharpPlayer>($"test player #{TestPlayerDbRef} exists");

		await Mediator.Send(new CreateChannelCommand(
			MarkupText.Plain(TestChannelName),
			[TestChannelPrivilege],
			player
		));

		var channel = await Mediator.Send(new GetChannelQuery(TestChannelName));

		if (channel != null)
		{
			await Mediator.Send(new AddUserToChannelCommand(channel, player));
		}
	}

	/// <summary>
	/// Runs <paramref name="command"/> as God and returns only what #1 was notified of while it ran.
	/// </summary>
	/// <remarks>
	/// The three <c>@pemit</c> cases below all address #1, which is not a recipient this class owns —
	/// every suite in the session notifies God. <c>Received(1)</c> counts against the notify
	/// substitute, which <see cref="ServerWebAppFactory"/> shares for the whole session, so a broad
	/// matcher ("a message to #1 reading 3", "one starting #-1 PARSER FAILURE") also counts a matching
	/// call some other suite made: these passed alone and failed in a full run. Clearing the substitute
	/// is not the fix — it deletes calls a concurrently running class is about to assert on, which
	/// <c>[NotInParallel]</c> does not prevent, since it serialises this class only against other
	/// <c>[NotInParallel]</c> ones. The windowed recorder is what the rest of the suite uses
	/// (<c>ConnectionAnnounceIntegrationTests</c>) and it never touches NSubstitute state.
	/// </remarks>
	private async Task<IReadOnlyList<string>> NotifiedGodWhile(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var before = WebAppFactoryArg.Notifications.CountFor(executor);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(executor).Skip(before)];
	}

	/// <summary>Runs <paramref name="command"/> as <paramref name="player"/> and returns what they were told meanwhile.</summary>
	private async Task<IReadOnlyList<string>> NotifiedWhile(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before)];
	}

	private static string UniqueAlias(string prefix) => $"{prefix}_{Guid.NewGuid().ToString("N")[..12]}";

	/// <summary>
	/// Runs <paramref name="body"/> with a fresh player who owns, and so is a member of, a channel of
	/// their own. The alias tests used God and the session's shared channel, and lost whenever another
	/// test's work left God off it.
	/// </summary>
	private async Task WithOwnChannelAsync(string prefix, Func<TestIsolationHelpers.TestPlayer, string, Task> body)
	{
		var player = await CreatePlayerAsync(prefix);
		var owner = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();
		var channelName = TestIsolationHelpers.GenerateUniqueName("CC");
		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(channelName), ["Open", "Player"], owner));
		try
		{
			await body(player, channelName);
		}
		finally
		{
			if (await Mediator.Send(new GetChannelQuery(channelName)) is { } channel)
			{
				await Mediator.Send(new DeleteChannelCommand(channel));
			}
		}
	}

	private Task<TestIsolationHelpers.TestPlayer> CreatePlayerAsync(string prefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	/// <summary>How many times God said exactly <paramref name="message"/> to himself as <paramref name="type"/>.</summary>
	private int GodHeardFromGod(string message, INotifyService.NotificationType type)
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		return WebAppFactoryArg.Notifications.DeliveriesFor(god)
			.Count(delivery => delivery.Sender == god && delivery.Type == type && delivery.Message == message);
	}

	[Test]
	[Arguments("@pemit #1=Test message", "Test message")]
	[Arguments("@pemit #1=Another test", "Another test")]
	public async ValueTask PemitBasic(string command, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var unique = TestIsolationHelpers.GenerateUniqueName(expected);

		await Assert.That(await NotifiedGodWhile(command.Replace(expected, unique))).Contains(unique);
	}

	/// <summary>
	/// Regression test for the command-argument subtree-reuse optimization (ArgumentSplit /
	/// SharpMUSHParserVisitor.EvaluateArgumentSubtree): a function call with nested brackets inside
	/// an EqSplit command's RHS argument must still evaluate correctly when the argument's parse
	/// subtree is re-visited directly instead of being re-lexed from scratch a third time.
	/// </summary>
	[Test]
	[Arguments("@pemit #1=[add(1,2)]", "3")]
	[Arguments("@pemit #1=[add(1,[mul(2,3)])]", "7")]
	public async ValueTask PemitWithFunctionCallInArgument(string command, string expected)
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("Sum");

		await Assert.That(await NotifiedGodWhile(command.Replace("#1=", $"#1={marker} "))).Contains($"{marker} {expected}");
	}

	/// <summary>
	/// Regression test for the command-argument subtree-reuse optimization (ArgumentSplit /
	/// SharpMUSHParserVisitor.EvaluateArgumentSubtree, CallState.HadErrors). @PEMIT is EqSplit
	/// without NoParse/RSNoParse, so its RHS is eagerly evaluated. The NoParse split pass that
	/// locates the '=' boundary always runs lenient (CommandEqSplitParse's
	/// lenient: !StrictParse, and StrictParse is only set by the single-token command handler),
	/// so a malformed argument like an unclosed function call doesn't fail that pass outright —
	/// ANTLR recovers and produces a best-effort tree. Before this optimization, each argument's
	/// extracted text still got an independent STRICT re-parse via FunctionParse afterwards,
	/// which is what actually turns this into "#-1 PARSER FAILURE ...". EvaluateArgumentSubtree
	/// must fall back to that strict re-parse whenever the split had errors, instead of trusting
	/// the retained (error-recovered) subtree and silently returning a best-effort value.
	/// </summary>
	[Test]
	public async ValueTask PemitWithUnclosedParenInArgument_ReportsParseFailure()
	{
		// Missing the closing ')' on add(1,2 — matches the exact malformed input already proven
		// to produce "#-1 PARSER FAILURE: Expected ) or , at end of expression" via FunctionParse
		// (see SharpMUSH.Tests/Parser/ParserFailureTests.cs and FunctionUnitTests.cs).
		var player = await CreatePlayerAsync("PemitUnclosed");
		var said = await NotifiedWhile(player, "@pemit me=add(1,2");

		await Assert.That(said.Any(message => message.StartsWith("#-1 PARSER FAILURE", StringComparison.Ordinal)))
			.IsTrue()
			.Because("a malformed argument must report the parse failure rather than a best-effort value");
	}

	[Test]
	[Arguments("@emit Test broadcast", "Test broadcast")]
	[Arguments("@emit Another broadcast message", "Another broadcast message")]
	public async ValueTask EmitBasic(string command, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var unique = TestIsolationHelpers.GenerateUniqueName(expected);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command.Replace(expected, unique)));

		// @emit broadcasts to room via CommunicationService.SendToRoomAsync which calls
		// Notify(AnySharpObject, ..., NotificationType.Emit)
		await Assert.That(GodHeardFromGod(unique, INotifyService.NotificationType.Emit)).IsEqualTo(1);
	}

	[Test]
	[Arguments("@lemit Test local emit", "Test local emit")]
	public async ValueTask LemitBasic(string command, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var unique = TestIsolationHelpers.GenerateUniqueName(expected);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command.Replace(expected, unique)));

		await Assert.That(GodHeardFromGod(unique, INotifyService.NotificationType.Emit)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask RemitBasic()
	{
		var (room, listener) = await ListenerInARoomOfItsOwnAsync("Remit");
		var unique = TestIsolationHelpers.GenerateUniqueName("Test remote emit");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@remit {room}={unique}"));

		await Assert.That(HeardFromGod(listener, unique, INotifyService.NotificationType.Emit)).IsEqualTo(1);
	}

	/// <summary>
	/// A fresh connected player alone in a fresh room, so a remote emit can be addressed somewhere no
	/// other test is — God's own location moves while other tests run.
	/// </summary>
	private async Task<(string Room, DBRef Listener)> ListenerInARoomOfItsOwnAsync(string prefix)
	{
		var dig = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix + "Room")}"));
		var room = dig.Message.ToPlainText()!.Trim();
		var listener = await CreatePlayerAsync(prefix + "Listener");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {listener.DbRef}={room}"));
		return (room, listener.DbRef);
	}

	/// <summary>How many times <paramref name="who"/> heard exactly <paramref name="message"/> from God as <paramref name="type"/>.</summary>
	private int HeardFromGod(DBRef who, string message, INotifyService.NotificationType type)
		=> WebAppFactoryArg.Notifications.DeliveriesFor(who)
			.Count(delivery => delivery.Sender == WebAppFactoryArg.ExecutorDBRef && delivery.Type == type && delivery.Message == message);

	[Test]
	public async ValueTask OemitBasic()
	{
		TestDiagnostics.WriteLine("Testing: @oemit");

		// Create a unique thing to omit so that the executor (player #1) still receives the emit.
		var excludeName = TestIsolationHelpers.GenerateUniqueName("OemitExclude");
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {excludeName}"));
		var excludeDbRef = DBRef.Parse(createResult.Message.ToPlainText()!);

		var expectedMsg = TestIsolationHelpers.GenerateUniqueName("Test omit emit");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@oemit {excludeDbRef}={expectedMsg}"));

		await Assert.That(GodHeardFromGod(expectedMsg, INotifyService.NotificationType.Emit)).IsEqualTo(1);
	}

	/// <summary>
	/// The excluded object must be resolved before the room is notified. The exclusion list was
	/// built from un-awaited locate calls, so an object resolved asynchronously could be added to
	/// it only after the emit had already gone out.
	/// </summary>
	[Test]
	public async ValueTask OemitDoesNotNotifyTheExcludedObject()
	{
		var excludeName = TestIsolationHelpers.GenerateUniqueName("OemitExcluded");
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {excludeName}"));
		var excludeDbRef = DBRef.Parse(createResult.Message.ToPlainText()!);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"drop {excludeDbRef}"));

		var expectedMsg = TestIsolationHelpers.GenerateUniqueName("Test omit exclusion");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@oemit {excludeDbRef}={expectedMsg}"));

		await Assert.That(GodHeardFromGod(expectedMsg, INotifyService.NotificationType.Emit)).IsEqualTo(1);
		await Assert.That(WebAppFactoryArg.Notifications.For(excludeDbRef).Count(message => message == expectedMsg)).IsEqualTo(0);
	}

	[Test, Skip("Failing")]
	public async ValueTask ZemitBasic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		TestDiagnostics.WriteLine("Testing: @zemit");

		var expectedMsg = "Test zone emit";

		// Create a unique zone master object (ZMO).
		var zmoName = TestIsolationHelpers.GenerateUniqueName("ZemitZMO");
		var zmoResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zmoName}"));
		var zmoDbRef = DBRef.Parse(zmoResult.Message.ToPlainText()!);

		// Zone room #0 to the ZMO so that it participates in the zone.  Player #1 is in room #0.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone #0={zmoDbRef}"));

		// Emit to the zone — player #1 (in room #0, which is now in the zone) should receive it.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@zemit {zmoDbRef}={expectedMsg}"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextEquals(s, expectedMsg)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Emit);

		// Clean up: remove the temporary zone from room #0.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@chzone #0=none"));
	}

	[Test]
	[Arguments("@nsemit Test nospoof emit")]
	public async ValueTask NsemitBasic(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var unique = TestIsolationHelpers.GenerateUniqueName("Test nospoof emit");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command.Replace("Test nospoof emit", unique)));

		await Assert.That(GodHeardFromGod(unique, INotifyService.NotificationType.NSEmit)).IsEqualTo(1);
	}

	[Test]
	[Arguments("@nslemit Test nospoof local")]
	public async ValueTask NslemitBasic(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var unique = TestIsolationHelpers.GenerateUniqueName("Test nospoof local");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command.Replace("Test nospoof local", unique)));

		await Assert.That(GodHeardFromGod(unique, INotifyService.NotificationType.NSEmit)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask NsremitBasic()
	{
		var (room, listener) = await ListenerInARoomOfItsOwnAsync("Nsremit");
		var unique = TestIsolationHelpers.GenerateUniqueName("Test nospoof remote");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nsremit {room}={unique}"));

		await Assert.That(HeardFromGod(listener, unique, INotifyService.NotificationType.NSEmit)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask NsoemitBasic()
	{
		TestDiagnostics.WriteLine("Testing: @nsoemit");

		// The first argument is the exclusion list, so omit a freshly-created thing to leave the
		// executor (player #1) among the recipients.
		var excludeName = TestIsolationHelpers.GenerateUniqueName("NsoemitExclude");
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {excludeName}"));
		var excludeDbRef = DBRef.Parse(createResult.Message.ToPlainText()!);

		var expectedMsg = TestIsolationHelpers.GenerateUniqueName("Test nospoof omit");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nsoemit {excludeDbRef}={expectedMsg}"));

		await Assert.That(GodHeardFromGod(expectedMsg, INotifyService.NotificationType.NSEmit)).IsEqualTo(1);
	}

	/// <summary>
	/// PennMUSH <c>do_oemit_list</c> (<c>speech.c</c>): the first argument of <c>@oemit</c> and
	/// <c>@nsoemit</c> is the list of objects to LEAVE OUT. Everyone else in the room — the sender
	/// included — hears the message, and the listed object hears nothing. <c>@nsoemit</c> was a copy
	/// of <c>@nsemit</c> that read argument 1 as the message and never looked at argument 0, so the
	/// exclusion list was silently discarded and the emit reached the object it had to skip.
	/// </summary>
	[Test]
	public async ValueTask NsoemitExcludesTheListedObject()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("NsoemitRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();

		var speaker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NsoemitSpeaker");
		var excluded = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NsoemitExcluded");
		var listener = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NsoemitListener");

		foreach (var player in new[] { speaker, excluded, listener })
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));
		}

		var message = TestIsolationHelpers.GenerateUniqueName("NsoemitSaid");
		await Parser.CommandParse(speaker.Handle, ConnectionService,
			MarkupText.Plain($"@nsoemit {excluded.DbRef}={message}"));

		var notifications = WebAppFactoryArg.Notifications;

		await Assert.That(notifications.For(listener.DbRef).Any(m => m.Contains(message))).IsTrue()
			.Because("everyone in the room who is not on the exclusion list hears an @nsoemit");
		await Assert.That(notifications.For(speaker.DbRef).Any(m => m.Contains(message))).IsTrue()
			.Because("@oemit omits the listed objects, not the sender");
		await Assert.That(notifications.For(excluded.DbRef).Any(m => m.Contains(message))).IsFalse()
			.Because("the object named in argument 0 is the one @nsoemit has to leave out");

		// The no-spoof delivery is a privilege, not a property of the command name:
		// CanNoSpoof is IsWizard || HasPower("Can_Spoof"), and @NSEMIT, @NSREMIT, @NSPEMIT and
		// @NSZEMIT all fall back to Emit without it. This speaker is a plain player, so it gets
		// Emit; NsoemitDeliversAsNoSpoof covers the privileged case with a wizard executor.
		await Assert.That(notifications.DeliveriesFor(listener.DbRef)
				.Any(d => d.Message.Contains(message) && d.Type == INotifyService.NotificationType.Emit)).IsTrue()
			.Because("an executor without Can_Spoof gets the ordinary emit type, as it does from every other @ns* command");
	}

	/// <summary>
	/// The exclusion list is a list: <c>@nsoemit &lt;obj&gt; &lt;obj&gt;=&lt;message&gt;</c> leaves
	/// out every object named in it.
	/// </summary>
	[Test]
	public async ValueTask NsoemitExcludesEveryObjectInTheList()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("NsoemitListRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();

		var speaker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NsoemitListSpeaker");
		var firstExcluded = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NsoemitListFirst");
		var secondExcluded = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NsoemitListSecond");
		var listener = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NsoemitListListener");

		foreach (var player in new[] { speaker, firstExcluded, secondExcluded, listener })
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));
		}

		var message = TestIsolationHelpers.GenerateUniqueName("NsoemitListSaid");
		await Parser.CommandParse(speaker.Handle, ConnectionService,
			MarkupText.Plain($"@nsoemit {firstExcluded.DbRef} {secondExcluded.DbRef}={message}"));

		var notifications = WebAppFactoryArg.Notifications;

		await Assert.That(notifications.For(listener.DbRef).Any(m => m.Contains(message))).IsTrue();
		await Assert.That(notifications.For(firstExcluded.DbRef).Any(m => m.Contains(message))).IsFalse()
			.Because("every object in the exclusion list is left out, not just the first");
		await Assert.That(notifications.For(secondExcluded.DbRef).Any(m => m.Contains(message))).IsFalse()
			.Because("every object in the exclusion list is left out, not just the first");
	}

	/// <summary>
	/// PennMUSH <c>do_oemit_list</c> accepts <c>&lt;room&gt;/&lt;object list&gt;</c>, emitting into
	/// that room instead of the sender's own — the same syntax <c>@oemit</c> already supports here.
	/// </summary>
	[Test]
	public async ValueTask NsoemitRoomSlashObjectEmitsIntoThatRoom()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("NsoemitRemoteRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();

		var excluded = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NsoemitRemoteExcluded");
		var listener = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NsoemitRemoteListener");

		foreach (var player in new[] { excluded, listener })
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));
		}

		var message = TestIsolationHelpers.GenerateUniqueName("NsoemitRemoteSaid");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@nsoemit {roomDbRef}/{excluded.DbRef}={message}"));

		var notifications = WebAppFactoryArg.Notifications;

		await Assert.That(notifications.For(listener.DbRef).Any(m => m.Contains(message))).IsTrue()
			.Because("<room>/<object> emits into the named room");
		await Assert.That(notifications.For(excluded.DbRef).Any(m => m.Contains(message))).IsFalse()
			.Because("the objects after the slash are still the exclusion list");
	}

	[Test]
	[Arguments("@nspemit #1=Test nospoof pemit")]
	public async ValueTask NspemitBasic(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var unique = TestIsolationHelpers.GenerateUniqueName("Test nospoof pemit");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command.Replace("Test nospoof pemit", unique)));

		await Assert.That(GodHeardFromGod(unique, INotifyService.NotificationType.NSPrivateEmit)).IsEqualTo(1);
	}

	[Test, Skip("Failing")]
	public async ValueTask NszemitBasic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		TestDiagnostics.WriteLine("Testing: @nszemit");

		var expectedMsg = "Test nospoof zone";

		// Create a unique zone master object (ZMO).
		var zmoName = TestIsolationHelpers.GenerateUniqueName("NsZemitZMO");
		var zmoResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zmoName}"));
		var zmoDbRef = DBRef.Parse(zmoResult.Message.ToPlainText()!);

		// Zone room #0 to the ZMO so that it participates in the zone.  Player #1 is in room #0.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone #0={zmoDbRef}"));

		// Emit to the zone — player #1 (in room #0, which is now in the zone) should receive it.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nszemit {zmoDbRef}={expectedMsg}"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextEquals(s, expectedMsg)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.NSEmit);

		// Clean up: remove the temporary zone from room #0.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@chzone #0=none"));
	}

	[Test]
	[Arguments("addcom test_alias_ADDCOM1=Public")]
	[Arguments("addcom test_alias_ADDCOM2=Public")]
	public async ValueTask AddComBasic(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var alias = UniqueAlias(command.Split('=')[0].Split(' ')[1]);

		await WithOwnChannelAsync("AddComBasic", async (player, channel) =>
		{
			var said = await NotifiedWhile(player, $"addcom {alias}={channel}");

			await Assert.That(said.Any(message => message.StartsWith($"Alias '{alias}' added for channel ", StringComparison.OrdinalIgnoreCase)))
				.IsTrue().Because($"the player was told: {string.Join(" | ", said)}");
		});
	}

	[Test]
	public async ValueTask AddComEmptyAlias()
	{
		var player = await CreatePlayerAsync("AddComEmpty");

		await Assert.That(await NotifiedWhile(player, "addcom=Public")).Contains("Alias name cannot be empty.");
	}

	/// <summary>
	/// A channel that does not exist gets the SAME refusal as one the caller may not see —
	/// "CHAT: I don't recognize that channel." A distinguishable "Channel not found." would let a caller
	/// tell the two apart and enumerate the channel list, which is why <c>GetVisibleChannelOrError</c>
	/// gives both one answer. PennMUSH words both cases identically too (<c>hdrs/extchat.h:161</c> for
	/// missing, every <c>Chan_Can_See</c> refusal in <c>src/extchat.c</c> for invisible).
	/// </summary>
	[Test]
	public async ValueTask AddComChannelNotFound()
	{
		var testPlayer = await CreatePlayerAsync("AddComChannelNotFound");

		await NotifiedWhile(testPlayer, "addcom test_alias_ADDCOM3=NonExistentChannel");

		await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(testPlayer.DbRef).Count(delivery =>
				delivery.Message == ErrorMessages.Notifications.DontRecognizeThatChannel
				&& delivery.Sender == testPlayer.DbRef
				&& delivery.Type == INotifyService.NotificationType.Announce))
			.IsEqualTo(1);
	}

	[Test]
	[Arguments("delcom test_alias_DELCOM1")]
	public async ValueTask DelComBasic(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var alias = UniqueAlias(command.Split(' ')[1]);

		await WithOwnChannelAsync("DelComBasic", async (player, channel) =>
		{
			await NotifiedWhile(player, $"addcom {alias}={channel}");

			var said = await NotifiedWhile(player, $"delcom {alias}");

			await Assert.That(said.Any(message => message.Equals($"Alias '{alias}' deleted.", StringComparison.OrdinalIgnoreCase)))
				.IsTrue().Because($"the player was told: {string.Join(" | ", said)}");
		});
	}

	/// <summary>
	/// Deleting a player's last alias for a channel takes them off it; deleting one of two does not. This
	/// is what made #1493: the alias tests once ran as God on one shared channel, and one test's delcom
	/// took God off the channel the others were still adding aliases to.
	/// </summary>
	[Test]
	public async ValueTask DelCom_OfTheLastAlias_LeavesTheChannel()
	{
		var first = UniqueAlias("LastAliasA");
		var second = UniqueAlias("LastAliasB");

		await WithOwnChannelAsync("DelComLeaves", async (player, channel) =>
		{
			var asPlayer = WebAppFactoryArg.FunctionParserFor(player.DbRef);
			// cwho() lists bare dbrefs, as PennMUSH's does.
			async Task<bool> OnChannel() => (await asPlayer.FunctionParse(MarkupText.Plain($"cwho({channel})")))!
				.Message.ToPlainText().Split(' ').Contains($"#{player.DbRef.Number}");

			await NotifiedWhile(player, $"addcom {first}={channel}");
			await NotifiedWhile(player, $"addcom {second}={channel}");

			await Assert.That(await OnChannel()).IsTrue().Because("the owner is a member from the start");
			await NotifiedWhile(player, $"delcom {first}");
			await Assert.That(await OnChannel()).IsTrue().Because("another alias still names the channel");

			await NotifiedWhile(player, $"delcom {second}");
			await Assert.That(await OnChannel()).IsFalse().Because("that was the last alias for it");
		});
	}

	[Test]
	[Arguments("delcom nonexistent_alias_DELCOM")]
	public async ValueTask DelComNotFound(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var player = await CreatePlayerAsync("DelComNotFound");
		var alias = command.Split(' ')[1];

		var said = await NotifiedWhile(player, command);

		await Assert.That(said.Any(message => message.Equals($"Alias '{alias}' not found.", StringComparison.OrdinalIgnoreCase)))
			.IsTrue();
	}

	[Test]
	[Arguments("@clist")]
	[Arguments("@clist/full")]
	public async ValueTask CListBasic(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var player = await CreatePlayerAsync("CListBasic");

		// @clist is @channel/list, which prints PennMUSH's columns (src/extchat.c:2622) as a table: each
		// channel's name under headings naming the columns.
		var said = await NotifiedWhile(player, command);

		await Assert.That(said.Any(message => message.Contains("Public") && message.Contains("Locks"))).IsTrue();
	}

	[Test]
	[Arguments("comtitle test_alias_COMTITLE=test_title_COMTITLE")]
	public async ValueTask ComTitleBasic(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var parts = command.Split('=');
		var alias = UniqueAlias(parts[0].Split(' ')[1]);
		var title = parts[1];

		await WithOwnChannelAsync("ComTitleBasic", async (player, channel) =>
		{
			await NotifiedWhile(player, $"addcom {alias}={channel}");

			// This command sends TWO notifications - one from ChannelTitle.Handle and one naming the alias.
			var said = await NotifiedWhile(player, $"comtitle {alias}={title}");

			await Assert.That(said.Any(message =>
					message.StartsWith($"Title set to '{title}' for alias '{alias}' (channel ", StringComparison.OrdinalIgnoreCase)))
				.IsTrue().Because($"the player was told: {string.Join(" | ", said)}");
		});
	}

	[Test]
	[Arguments("comtitle nonexistent_alias_COMTITLE=title")]
	public async ValueTask ComTitleNotFound(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var player = await CreatePlayerAsync("ComTitleNotFound");
		var alias = command.Split('=')[0].Split(' ')[1];

		var said = await NotifiedWhile(player, command);

		await Assert.That(said.Any(message => message.Equals($"Alias '{alias}' not found.", StringComparison.OrdinalIgnoreCase)))
			.IsTrue();
	}

	[Test]
	[Arguments("comlist")]
	public async ValueTask ComListBasic(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var first = UniqueAlias("test_alias_COMLIST1");
		var second = UniqueAlias("test_alias_COMLIST2");
		await WithOwnChannelAsync("ComListBasic", async (player, channel) =>
		{
			await NotifiedWhile(player, $"addcom {first}={channel}");
			await NotifiedWhile(player, $"addcom {second}={channel}");

			var said = await NotifiedWhile(player, command);

			// The output is sent as a multi-line MString containing all aliases (in lowercase)
			// Note: Aliases are stored in uppercase but displayed in lowercase
			await Assert.That(said.Count(message =>
					message.Contains(first.ToLowerInvariant()) && message.Contains(second.ToLowerInvariant())))
				.IsEqualTo(1).Because($"the player was told: {string.Join(" | ", said)}");
		});
	}

	[Test]
	[Arguments("comlist")]
	public async ValueTask ComListEmpty(string command)
	{
		TestDiagnostics.WriteLine("Testing: {0}", command);
		var player = await CreatePlayerAsync("ComListEmpty");

		await Assert.That(await NotifiedWhile(player, command)).Contains("You have no channel aliases.");
	}

	/// <summary>
	/// PennMUSH <c>do_pemit</c> (<c>speech.c:595</c>) echoes the message back to the sender unless
	/// <c>/silent</c> is given and the target is not the sender itself.
	/// </summary>
	[Test]
	public async ValueTask PemitEchoesTheMessageBackToTheSender()
	{
		var targetName = TestIsolationHelpers.GenerateUniqueName("PemitEchoTarget");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {targetName}"));

		var message = TestIsolationHelpers.GenerateUniqueName("Hello there");
		var said = await NotifiedGodWhile($"@pemit {targetName}={message}");

		await Assert.That(said.Any(m => m.StartsWith($"You pemit \"{message}\" to "))).IsTrue();
	}

	[Test]
	public async ValueTask PemitSilentSuppressesTheSenderEcho()
	{
		var targetName = TestIsolationHelpers.GenerateUniqueName("PemitSilentTarget");
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {targetName}"));
		var targetDbRef = DBRef.Parse(createResult.Message.ToPlainText()!);

		var message = TestIsolationHelpers.GenerateUniqueName("Quietly");
		var notified = await NotifiedGodWhile($"@pemit/silent {targetName}={message}");

		// /silent suppresses the sender's echo, not the delivery — without this a no-op would pass.
		await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(targetDbRef)
			.Count(d => d.Message == message && d.Type == INotifyService.NotificationType.PrivateEmit)).IsEqualTo(1);
		await Assert.That(notified.Any(m => m.Contains(message))).IsFalse();
	}

	/// <summary>
	/// PennMUSH <c>speech.c:599</c> skips the echo when the only recipient was the sender, who has
	/// already seen the message.
	/// </summary>
	[Test]
	public async ValueTask PemitToYourselfDoesNotEchoBack()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		var message = TestIsolationHelpers.GenerateUniqueName("TalkingToMyself");
		var notified = await NotifiedGodWhile($"@pemit #{executor.Number}={message}");

		// Heard once, as the message itself, and never as a "You pemit" echo.
		await Assert.That(notified.Where(m => m.Contains(message))).IsEquivalentTo([message]);
	}

	/// <summary>
	/// PennMUSH <c>speech.c:596</c>: more than one recipient collapses the echo into a count.
	/// </summary>
	[Test]
	public async ValueTask PemitListEchoesTheRecipientCount()
	{
		var firstName = TestIsolationHelpers.GenerateUniqueName("PemitListA");
		var secondName = TestIsolationHelpers.GenerateUniqueName("PemitListB");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {firstName}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {secondName}"));

		var message = TestIsolationHelpers.GenerateUniqueName("Group message");
		var said = await NotifiedGodWhile($"@pemit/list/noisy {firstName} {secondName}={message}");

		// The whole rendering, not just the key: that would pass even if the recipients were counted wrong.
		await Assert.That(said).Contains($"You pemit \"{message}\" to 2 objects.");
	}

	/// <summary>
	/// PennMUSH <c>do_one_remit</c> (<c>speech.c:1263</c>): an exit cannot hold anything.
	/// </summary>
	[Test]
	public async ValueTask RemitToAnExitReportsThatNothingCanBeInIt()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("RemitExitRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();

		var exitName = TestIsolationHelpers.GenerateUniqueName("RemitExit");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@open {exitName}={roomDbRef}"));

		var said = await NotifiedGodWhile($"@remit {exitName}=Nobody hears this");

		await Assert.That(said).Contains("There can't be anything in that!");
	}

	/// <summary>
	/// PennMUSH <c>speech.c:1273</c>: the sender is told what they remitted, but only when they are not
	/// standing in the target room themselves.
	/// </summary>
	[Test]
	public async ValueTask RemitEchoesToTheSenderWhenTheyAreElsewhere()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("RemitEchoRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();

		var message = TestIsolationHelpers.GenerateUniqueName("Anyone there?");
		var said = await NotifiedGodWhile($"@remit {roomDbRef}={message}");

		await Assert.That(said.Any(m => m.StartsWith($"You remit, \"{message}\" in "))).IsTrue();
	}

	[Test]
	public async ValueTask RemitSilentSuppressesTheSenderEcho()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("RemitSilentRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();

		var message = TestIsolationHelpers.GenerateUniqueName("Hush");
		var notified = await NotifiedGodWhile($"@remit/silent {roomDbRef}={message}");

		await Assert.That(notified.Any(m => m.Contains(message))).IsFalse();
	}

	/// <summary>
	/// Digs a fresh room and moves a fresh player into it, so a remit test can assert who heard the
	/// message and who did not without depending on any shared object.
	/// </summary>
	private async Task<(string RoomDbRef, DBRef Listener)> DigRoomWithListenerAsync(string prefix)
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName(prefix);
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();

		var listener = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Listener");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {listener.DbRef}={roomDbRef}"));

		return (roomDbRef, listener.DbRef);
	}

	private bool Heard(DBRef listener, string message)
		=> WebAppFactoryArg.Notifications.For(listener).Any(said => said.Contains(message, StringComparison.Ordinal));

	/// <summary>
	/// PennMUSH <c>do_remit</c> (<c>speech.c:1303-1307</c>): <c>/list</c> splits the target argument on
	/// spaces and remits into every room named, not only the first.
	/// </summary>
	[Test]
	public async ValueTask NsremitListReachesEveryRoomInTheList()
	{
		var (firstRoom, firstListener) = await DigRoomWithListenerAsync("NsremitListA");
		var (secondRoom, secondListener) = await DigRoomWithListenerAsync("NsremitListB");
		var message = TestIsolationHelpers.GenerateUniqueName("NsremitListSaid");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@nsremit/list {firstRoom} {secondRoom}={message}"));

		await Assert.That(Heard(firstListener, message)).IsTrue()
			.Because("the first room in the list must hear the emit");
		await Assert.That(Heard(secondListener, message)).IsTrue()
			.Because("every later room in the list must hear it too");
	}

	[Test]
	public async ValueTask NsremitToASingleRoomStillDelivers()
	{
		var (room, listener) = await DigRoomWithListenerAsync("NsremitSingle");
		var message = TestIsolationHelpers.GenerateUniqueName("NsremitSingleSaid");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nsremit {room}={message}"));

		await Assert.That(Heard(listener, message)).IsTrue();
	}

	[Test]
	public async ValueTask RemitListReachesEveryRoomInTheList()
	{
		var (firstRoom, firstListener) = await DigRoomWithListenerAsync("RemitListA");
		var (secondRoom, secondListener) = await DigRoomWithListenerAsync("RemitListB");
		var message = TestIsolationHelpers.GenerateUniqueName("RemitListSaid");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@remit/list {firstRoom} {secondRoom}={message}"));

		await Assert.That(Heard(firstListener, message)).IsTrue();
		await Assert.That(Heard(secondListener, message)).IsTrue();
	}

	/// <summary>
	/// PennMUSH <c>do_remit</c> (<c>speech.c:1308-1309</c>): without <c>/list</c> the whole argument is a
	/// single room name, so a space-separated pair of dbrefs names nothing and nobody hears it.
	/// </summary>
	[Test]
	public async ValueTask RemitWithoutListTreatsTheWholeArgumentAsOneRoomName()
	{
		var (firstRoom, firstListener) = await DigRoomWithListenerAsync("RemitNoListA");
		var (secondRoom, secondListener) = await DigRoomWithListenerAsync("RemitNoListB");
		var message = TestIsolationHelpers.GenerateUniqueName("RemitNoListSaid");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@remit {firstRoom} {secondRoom}={message}"));

		await Assert.That(Heard(firstListener, message)).IsFalse();
		await Assert.That(Heard(secondListener, message)).IsFalse();
	}

	/// <summary>
	/// PennMUSH <c>do_lemit</c> (<c>speech.c:1343</c>): echoed only when the sender is not directly in the
	/// outermost room, i.e. when they are inside a container.
	/// </summary>
	[Test]
	public async ValueTask LemitEchoesToTheSenderFromInsideAContainer()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LemitEcho");

		var boxName = TestIsolationHelpers.GenerateUniqueName("LemitBox");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {boxName}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {boxName}=ENTER_OK"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={boxName}"));

		var said = await NotifiedWhile(player, "@lemit Anyone outside?");

		await Assert.That(said).Contains("You lemit: \"Anyone outside?\"");
	}

	/// <summary>
	/// PennMUSH <c>do_zemit</c> (<c>speech.c:1405</c>) requires control of the zone object.
	/// </summary>
	[Test]
	public async ValueTask ZemitRequiresControlOfTheZone()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ZemitNoControl");

		// God is the one object a mortal provably cannot control, so this isolates the new permission
		// branch from the Control-lock default that governs ordinary objects.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}=#0"));

		var said = await NotifiedWhile(player, "@zemit #1=Not mine");

		await Assert.That(said).Contains("Permission denied.");
	}

	/// <summary>
	/// PennMUSH <c>speech.c:1419</c>: the sender is told what they zemitted.
	/// </summary>
	[Test]
	public async ValueTask ZemitEchoesToTheSender()
	{
		var zmoName = TestIsolationHelpers.GenerateUniqueName("ZemitEchoZMO");
		var zmoResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zmoName}"));
		var zmoDbRef = zmoResult.Message.ToPlainText()!.Trim();

		var message = TestIsolationHelpers.GenerateUniqueName("Zone wide");
		var said = await NotifiedGodWhile($"@zemit {zmoDbRef}={message}");

		await Assert.That(said.Any(m => m.StartsWith($"You zemit, \"{message}\" in zone "))).IsTrue();
	}
}
