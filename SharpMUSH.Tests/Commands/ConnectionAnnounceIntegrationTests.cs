using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// End-to-end proof that <see cref="SharpMUSH.Library.Services.ConnectionAnnounceService"/> is
/// actually wired up: the real CONNECT/CD/CH login words and QUIT, run through the real command
/// parser and DI container (not a mock of the service itself), must produce the PennMUSH-worded
/// broadcasts documented in <c>ErrorMessages.Notifications</c> — on the room broadcast path
/// (<c>CommunicationService.SendToRoomAsync</c>) and on the channel-announce path
/// (<c>ChannelMessageRequestHandler</c>) alike.
///
/// <para>Every scenario uses freshly created, uniquely-named players and a fresh room/channel per
/// test, and reads back only that test's own witness's notification queue via
/// <see cref="ServerWebAppFactory.Notifications"/> (windowed with a before/after count) — never
/// NSubstitute's <c>ReceivedCalls()</c>/<c>Received()</c>, which is shared session-wide and, per
/// <see cref="TestHelpers.NotificationRecorder"/>, is not safe to enumerate while other
/// <c>[NotInParallel]</c>-exempt test classes may still be recording calls into the same
/// substitute. <c>[NotInParallel]</c> here only serializes this class's own tests against each
/// other; it does not pause the rest of the session.</para>
/// </summary>
[NotInParallel]
public class ConnectionAnnounceIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	// --- helpers ---------------------------------------------------------------------------------

	/// <summary>Digs a fresh, uniquely-named room as God and returns its DBRef.</summary>
	private async ValueTask<DBRef> DigRoomAsync(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {name}"));
		return DBRef.Parse(result.Message!.ToPlainText()!.Trim());
	}

	/// <summary>Teleports (as God, so locks are irrelevant) into a room dug by <see cref="DigRoomAsync"/>.</summary>
	private async ValueTask TeleportAsync(DBRef who, DBRef room)
		=> await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {who}={room}"));

	/// <summary>A registered but unbound handle — a client sitting on the connect screen.</summary>
	private async ValueTask<long> AnonymousHandleAsync()
	{
		var handle = Random.Shared.NextInt64(500_000, 599_999);
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	/// <summary>Resolves an object already known to exist (created earlier in the same test) to its <see cref="AnySharpObject"/>.</summary>
	private async ValueTask<AnySharpObject> KnownObjectAsync(DBRef dbRef)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbRef))).Known;

	/// <summary>Everything <paramref name="who"/> was notified of since <paramref name="before"/>, in order.</summary>
	private string[] MessagesTo(DBRef who, int before)
		=> [.. WebAppFactoryArg.Notifications.For(who).Skip(before)];

	/// <summary>
	/// Creates a fresh, uniquely-named channel with the given privileges, owned by God, and returns it.
	/// Mirrors <c>CommunicationCommandTests.SetupTestChannel</c>, but with a unique name per test so
	/// this file's channel-announce assertions can never collide with another test's channel traffic.
	/// </summary>
	private async ValueTask<(string Name, SharpChannel Channel)> CreateChannelAsync(string prefix, params string[] privs)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var ownerNode = await Database.GetObjectNodeAsync(WebAppFactoryArg.ExecutorDBRef);
		var owner = ownerNode.AsPlayer;

		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(name), privs, owner));
		var channel = await Mediator.Send(new GetChannelQuery(name))
			?? throw new InvalidOperationException($"Channel {name} was not created.");

		return (name, channel);
	}

	private async ValueTask JoinChannelAsync(SharpChannel channel, DBRef who)
		=> await Mediator.Send(new AddUserToChannelCommand(channel, await KnownObjectAsync(who)));

	// --- Test 1: connect a fresh player (single connection) --------------------------------------

	[Test]
	public async ValueTask Connect_FreshPlayer_BroadcastsHasConnectedToTheRoom()
	{
		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWitness1");
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AnnounceConn1");

		var room = await DigRoomAsync("AnnounceRoom1");
		await TeleportAsync(witness.DbRef, room);
		await TeleportAsync(playerRef, room);

		var playerName = (await KnownObjectAsync(playerRef)).Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		var handle = await AnonymousHandleAsync();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"CONNECT {playerName} TestPassword123"));

		var messages = MessagesTo(witness.DbRef, before);

		await Assert.That(messages).Contains($"{playerName} {ErrorMessages.Notifications.GameHasConnected}");
	}

	// --- Test 2: same player, second simultaneous handle --------------------------------------

	[Test]
	public async ValueTask Connect_SamePlayerSecondHandle_BroadcastsHasReconnectedToTheRoom()
	{
		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWitness2");
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AnnounceConn2");

		var room = await DigRoomAsync("AnnounceRoom2");
		await TeleportAsync(witness.DbRef, room);
		await TeleportAsync(playerRef, room);

		var playerName = (await KnownObjectAsync(playerRef)).Object().Name;

		// First connection: "has connected." — asserted already by Test 1's style, not re-checked here.
		var handle1 = await AnonymousHandleAsync();
		await Parser.CommandParse(handle1, ConnectionService, MarkupText.Plain($"CONNECT {playerName} TestPassword123"));

		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		// A second, simultaneous handle logging in as the SAME player while the first stays open.
		var handle2 = await AnonymousHandleAsync();
		await Parser.CommandParse(handle2, ConnectionService, MarkupText.Plain($"CONNECT {playerName} TestPassword123"));

		var messages = MessagesTo(witness.DbRef, before);

		await Assert.That(messages).Contains($"{playerName} {ErrorMessages.Notifications.GameHasReconnected}");
	}

	// --- Test 3: disconnect a player's only connection --------------------------------------------

	/// <summary>
	/// KNOWN FAILING as of Task 14 — production bug, not a test-harness issue, and intentionally left
	/// red rather than weakened (see this file's task report). <see cref="ConnectionService.Disconnect"/>
	/// publishes the <c>ConnectionStateChangeNotification</c> BEFORE removing the handle from its
	/// session-state dictionary (unlike <c>Unbind</c>, which does the opposite and says so in a
	/// comment). <c>ConnectionStateEventHandler.Handle</c> computes <c>remainingConnections</c> via
	/// <c>connectionService.Get(playerRef).CountAsync()</c> while handling that notification, so for a
	/// player's ONLY connection it counts the about-to-be-removed handle too and gets 1, not 0 — the
	/// broadcast reads "has partially disconnected." instead of "has disconnected.", and LASTLOGOUT
	/// (gated on <c>remainingConnections == 0</c>) never gets set. Test 4 (two connections, one
	/// disconnects) is unaffected: 1 vs. 2 remaining both land in the same "&gt;0" wording bucket.
	/// </summary>
	[Test]
	public async ValueTask Quit_OnlyConnection_BroadcastsHasDisconnectedAndSetsLastLogout()
	{
		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWitness3");
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceDisc3");

		var room = await DigRoomAsync("AnnounceRoom3");
		await TeleportAsync(witness.DbRef, room);
		await TeleportAsync(testPlayer.DbRef, room);

		var playerObj = await KnownObjectAsync(testPlayer.DbRef);
		var playerName = playerObj.Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("QUIT"));

		var messages = MessagesTo(witness.DbRef, before);
		await Assert.That(messages).Contains($"{playerName} {ErrorMessages.Notifications.GameHasDisconnected}");

		await TestHelpers.WaitForAttribute(AttributeService, playerObj, "LASTLOGOUT");
		var lastLogout = await AttributeService.GetAttributeAsync(
			playerObj, playerObj, "LASTLOGOUT", IAttributeService.AttributeMode.Read, false);
		await Assert.That(lastLogout.IsAttribute).IsTrue()
			.Because("AnnounceDisconnectAsync sets LASTLOGOUT once the player's last connection drops");
	}

	// --- Test 4: disconnect one of two connections -------------------------------------------------

	[Test]
	public async ValueTask Quit_OneOfTwoConnections_BroadcastsHasPartiallyDisconnected()
	{
		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWitness4");
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceDisc4");

		var room = await DigRoomAsync("AnnounceRoom4");
		await TeleportAsync(witness.DbRef, room);
		await TeleportAsync(testPlayer.DbRef, room);

		// A second, simultaneous connection for the same player (bound directly, mirroring
		// HideCommandTests/SocketCommandTests — the connect announcement wording for the SECOND
		// connection is already covered by Test 2, this test only needs it present).
		var secondHandle = await AnonymousHandleAsync();
		await ConnectionService.Bind(secondHandle, testPlayer.DbRef);

		var playerName = (await KnownObjectAsync(testPlayer.DbRef)).Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("QUIT"));

		var messages = MessagesTo(witness.DbRef, before);
		await Assert.That(messages).Contains($"{playerName} {ErrorMessages.Notifications.GameHasPartiallyDisconnected}");
	}

	// --- Test 5: "ch" hidden-connect wording + WHO visibility ---------------------------------------

	[Test]
	public async ValueTask ConnectHidden_ViaCh_BroadcastsHiddenConnectedAndHidesFromMortalWhoOnly()
	{
		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWitness5");
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AnnounceCh5");
		// ch only hides the connection when the connecting player has Hide permission.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {playerRef}=WIZARD"));

		var room = await DigRoomAsync("AnnounceRoom5");
		await TeleportAsync(witness.DbRef, room);
		await TeleportAsync(playerRef, room);

		var playerName = (await KnownObjectAsync(playerRef)).Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		var handle = await AnonymousHandleAsync();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"ch {playerName} TestPassword123"));

		var messages = MessagesTo(witness.DbRef, before);
		await Assert.That(messages).Contains($"{playerName} {ErrorMessages.Notifications.GameHasHiddenConnected}");

		// A mortal viewer's WHO must not list the hidden connection...
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceMortalWho5");
		var mortalBefore = WebAppFactoryArg.Notifications.CountForHandle(mortal.Handle);
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("WHO"));
		var mortalListing = WebAppFactoryArg.Notifications.ForHandle(mortal.Handle).Skip(mortalBefore).LastOrDefault();

		await Assert.That(mortalListing).IsNotNull();
		await Assert.That(mortalListing!).DoesNotContain(playerName);

		// ...but a wizard's WHO must.
		var wizardBefore = WebAppFactoryArg.Notifications.CountForHandle(1);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("WHO"));
		var wizardListing = WebAppFactoryArg.Notifications.ForHandle(1).Skip(wizardBefore).LastOrDefault();

		await Assert.That(wizardListing).IsNotNull();
		await Assert.That(wizardListing!).Contains(playerName);
	}

	// --- Test 6: "cd" wording + DARK flag ------------------------------------------------------------

	/// <summary>
	/// <c>cd</c> forces the connecting player's DARK flag on, which also turns off the room-broadcast
	/// half of the announcement (<c>ConnectionAnnounceService</c> only sends the visible-room message
	/// when the player is not Dark). The channel-announce path is unconditional on Dark, so it — not
	/// the room — is this test's witness for the wording; the DARK flag itself is asserted directly.
	/// </summary>
	[Test]
	public async ValueTask ConnectDark_ViaCd_SetsDarkFlagAndBroadcastsHiddenConnectedOnChannel()
	{
		var (chanName, channel) = await CreateChannelAsync("AnnounceChan6", "Open");

		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWitness6");
		await JoinChannelAsync(channel, witness.DbRef);

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AnnounceCd6");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {playerRef}=WIZARD"));
		await JoinChannelAsync(channel, playerRef);

		var playerName = (await KnownObjectAsync(playerRef)).Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		var handle = await AnonymousHandleAsync();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"cd {playerName} TestPassword123"));

		var messages = MessagesTo(witness.DbRef, before);
		await Assert.That(messages).Contains(
			$"<{chanName}> {playerName} {ErrorMessages.Notifications.GameHasHiddenConnected}");

		var reloaded = await KnownObjectAsync(playerRef);
		await Assert.That(await reloaded.HasFlag("DARK")).IsTrue();
	}

	// --- Test 7: @hide, then disconnect ---------------------------------------------------------------

	/// <summary>
	/// KNOWN FAILING as of Task 14 — same production bug as
	/// <see cref="Quit_OnlyConnection_BroadcastsHasDisconnectedAndSetsLastLogout"/>: this is again a
	/// player's only connection, so <c>remainingConnections</c> is overcounted to 1 and the broadcast
	/// reads "has partially HIDDEN-disconnected." instead of "has HIDDEN-disconnected."
	/// </summary>
	[Test]
	public async ValueTask Hide_ThenQuit_BroadcastsHiddenDisconnected()
	{
		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWitness7");
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceHide7");
		// @hide is permission-gated (wizard/royalty or the Hide power) - grant WIZARD.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		var room = await DigRoomAsync("AnnounceRoom7");
		await TeleportAsync(witness.DbRef, room);
		await TeleportAsync(testPlayer.DbRef, room);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));

		var playerName = (await KnownObjectAsync(testPlayer.DbRef)).Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("QUIT"));

		var messages = MessagesTo(witness.DbRef, before);
		await Assert.That(messages).Contains($"{playerName} {ErrorMessages.Notifications.GameHasHiddenDisconnected}");
	}

	// --- Test 8: channel announcement on a non-Quiet channel -------------------------------------------

	[Test]
	public async ValueTask Connect_OnNonQuietChannel_BroadcastsToChannelMembers()
	{
		var (chanName, channel) = await CreateChannelAsync("AnnounceChan8", "Open");

		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWitness8");
		await JoinChannelAsync(channel, witness.DbRef);

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AnnounceConn8");
		await JoinChannelAsync(channel, playerRef);

		var playerName = (await KnownObjectAsync(playerRef)).Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		var handle = await AnonymousHandleAsync();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"CONNECT {playerName} TestPassword123"));

		var messages = MessagesTo(witness.DbRef, before);
		await Assert.That(messages).Contains(
			$"<{chanName}> {playerName} {ErrorMessages.Notifications.GameHasConnected}");
	}

	// --- Test 9: a Quiet channel suppresses the channel announcement, room broadcast still fires ---------

	[Test]
	public async ValueTask Connect_OnQuietChannel_SuppressesChannelBroadcastButRoomBroadcastStillHappens()
	{
		var (_, channel) = await CreateChannelAsync("AnnounceQuietChan9", "Open", "Quiet");

		var channelWitness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceChanWitness9");
		await JoinChannelAsync(channel, channelWitness.DbRef);

		var roomWitness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceRoomWitness9");
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AnnounceConn9");
		await JoinChannelAsync(channel, playerRef);

		var room = await DigRoomAsync("AnnounceRoom9");
		await TeleportAsync(roomWitness.DbRef, room);
		await TeleportAsync(playerRef, room);

		var playerName = (await KnownObjectAsync(playerRef)).Object().Name;
		var chanBefore = WebAppFactoryArg.Notifications.CountFor(channelWitness.DbRef);
		var roomBefore = WebAppFactoryArg.Notifications.CountFor(roomWitness.DbRef);

		var handle = await AnonymousHandleAsync();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"CONNECT {playerName} TestPassword123"));

		var chanMessages = MessagesTo(channelWitness.DbRef, chanBefore);
		var roomMessages = MessagesTo(roomWitness.DbRef, roomBefore);

		await Assert.That(chanMessages).IsEmpty()
			.Because("channels with the Quiet privilege must not carry the connect announcement");
		await Assert.That(roomMessages).Contains($"{playerName} {ErrorMessages.Notifications.GameHasConnected}");
	}
}
