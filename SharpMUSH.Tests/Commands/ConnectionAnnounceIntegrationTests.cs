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
	/// Exercises the single-connection count edge case: <see cref="ConnectionService.Disconnect"/>
	/// publishes the <c>ConnectionStateChangeNotification</c> before it removes the handle from its
	/// session-state dictionary, so <c>ConnectionStateEventHandler.Handle</c> must exclude the
	/// disconnecting handle itself (by handle, not by relying on removal order) when it counts the
	/// player's remaining connections — otherwise a player's ONLY connection is counted as 1
	/// remaining instead of 0, and the broadcast reads "has partially disconnected." instead of
	/// "has disconnected.", with LASTLOGOUT (gated on <c>remainingConnections == 0</c>) never set.
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
	/// Same single-connection count edge case as
	/// <see cref="Quit_OnlyConnection_BroadcastsHasDisconnectedAndSetsLastLogout"/>, on the HIDDEN
	/// wording branch: without excluding the disconnecting handle from the remaining-connections
	/// count, this player's only connection would be overcounted to 1 and the broadcast would read
	/// "has partially HIDDEN-disconnected." instead of "has HIDDEN-disconnected."
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

	// --- Test 10: ACONNECT hook actually executes, and reaches a zoned room's contents ------------

	/// <summary>
	/// Every other test in this file only proves the room/channel BROADCAST paths. None of them ever
	/// caused an ACONNECT/ADISCONNECT hook attribute to actually run, because none of them set one -
	/// so <c>QueueHookAsync</c>'s attribute-execution branch (the 15-argument <c>ParserState</c> push,
	/// %1 binding, <c>CommandListParse</c> on the attribute body) went untested end-to-end. This is
	/// also the regression test for the zone-dispatch bug fixed in
	/// <c>ConnectionAnnounceService.DispatchZoneAndMasterRoomHooksAsync</c>: that method used to read
	/// the connecting PLAYER's own Zone (almost always unset - zones are attached to rooms in
	/// practice) instead of the zone of the player's LOCATION (PennMUSH bsd.c:5992,
	/// <c>loc = Location(player)</c>), so a zoned room's contents never got their hook queued at all.
	///
	/// <para>Setup: a "zone room" holds a Thing with <c>&amp;ACONNECT thing=@emit ...%1...</c>; a
	/// separate "player room" (chzoned to the zone room, and NOT itself the zone room) is where the
	/// connecting player actually lands. A witness sits in the zone room. If the hook never executes,
	/// or the zone dispatch still reads the player's own Zone instead of their location's, the witness
	/// sees nothing.</para>
	/// </summary>
	[Test]
	public async ValueTask Connect_PlayerInZonedRoom_ExecutesZonedRoomContentsAconnectHook()
	{
		var zoneRoom = await DigRoomAsync("AnnounceZoneRoom10");
		var playerRoom = await DigRoomAsync("AnnounceRoom10");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {playerRoom}={zoneRoom}"));

		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceZoneWitness10");
		await TeleportAsync(witness.DbRef, zoneRoom);

		var hookThingResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create AnnounceHookThing10"));
		var hookThing = DBRef.Parse(hookThingResult.Message!.ToPlainText());
		await TeleportAsync(hookThing, zoneRoom);
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&ACONNECT {hookThing}=@emit Zone hook fired for connection %1"));

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AnnounceZoneConn10");
		await TeleportAsync(playerRef, playerRoom);

		var playerName = (await KnownObjectAsync(playerRef)).Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		var handle = await AnonymousHandleAsync();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"CONNECT {playerName} TestPassword123"));

		var messages = MessagesTo(witness.DbRef, before);

		await Assert.That(messages).Contains("Zone hook fired for connection 1")
			.Because("the zone room's contents' ACONNECT must fire, proving both that QueueHookAsync " +
				"actually executes hook attributes and that DispatchZoneAndMasterRoomHooksAsync reads " +
				"the player's LOCATION's zone rather than the player's own (almost always unset) zone");
	}

	// --- Test 11: a WIZARD-flagged hook object's ACONNECT still fires for a mortal connect (Finding 1) ---

	/// <summary>
	/// Regression test for the Codex review's Finding 1 on PR #902: <c>QueueHookAsync</c> used to check
	/// the ACONNECT/ADISCONNECT read/execute permission as the CONNECTING PLAYER evaluating the hook
	/// OWNER's attribute (<c>GetAttributeAsync(player, owner, ...)</c>), rather than as the owner
	/// evaluating its own attribute. ACONNECT/ADISCONNECT attributes are seeded without the "public"
	/// flag, so for a WIZARD- or ROYALTY-flagged hook object - not a contrived case; <c>#8</c> "HTTP
	/// Handler" and <c>#9</c> "Event Handler" in <c>InitialObjectSeed</c> both ship WIZARD-flagged by
	/// default - a mortal connecting player would fail <c>PermissionService.CanEvalAttr</c> and the
	/// hook would silently never fire.
	///
	/// <para>Reuses the zoned-room end-to-end fixture from Test 10, but the hook Thing itself is set
	/// WIZARD (the connecting player is left an ordinary mortal - no <c>@set ... =WIZARD</c>). Before
	/// the fix this test fails (the witness sees nothing); after the fix it passes, because the
	/// permission check now runs as the hook owner reading its own attribute, which always satisfies
	/// <c>PermissionService.CanEval</c>'s self-evaluation case regardless of the owner's own privilege
	/// level.</para>
	/// </summary>
	[Test]
	public async ValueTask Connect_MortalPlayer_WizardOwnedZoneHookStillFires()
	{
		var zoneRoom = await DigRoomAsync("AnnounceWizZoneRoom11");
		var playerRoom = await DigRoomAsync("AnnounceWizRoom11");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {playerRoom}={zoneRoom}"));

		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWizWitness11");
		await TeleportAsync(witness.DbRef, zoneRoom);

		var hookThingResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create AnnounceWizHookThing11"));
		var hookThing = DBRef.Parse(hookThingResult.Message!.ToPlainText());
		await TeleportAsync(hookThing, zoneRoom);
		// The HOOK OBJECT itself carries WIZARD, not the connecting player. Setting the flag requires
		// a trusted/wizard executor, so this runs as God (handle 1).
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {hookThing}=WIZARD"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&ACONNECT {hookThing}=@emit Wizard zone hook fired for connection %1"));

		// An ordinary mortal - no WIZARD, no ROYALTY, no Hide/See_All power.
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AnnounceWizConn11");
		await TeleportAsync(playerRef, playerRoom);

		var playerName = (await KnownObjectAsync(playerRef)).Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		var handle = await AnonymousHandleAsync();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"CONNECT {playerName} TestPassword123"));

		var messages = MessagesTo(witness.DbRef, before);

		await Assert.That(messages).Contains("Wizard zone hook fired for connection 1")
			.Because("QueueHookAsync must check the hook OWNER's own permission to read/execute its " +
				"ACONNECT attribute, not the connecting mortal player's - a WIZARD-flagged hook object's " +
				"ACONNECT must still fire for an ordinary mortal connect");
	}

	// --- Test 12: @hide, then LOGOUT (not QUIT) - Finding 2 ----------------------------------------

	/// <summary>
	/// Regression test for the Codex review's Finding 2 on PR #902: <c>ConnectionService.Unbind</c>
	/// used to clear the "Hidden" metadata key INSIDE the same state mutation that nulls <c>Ref</c>,
	/// before publishing <c>ConnectionStateChangeNotification</c> - so
	/// <c>ConnectionStateEventHandler</c>'s disconnect branch, which re-fetches connection data via
	/// <c>IConnectionService.Get</c> to read <c>IsHidden</c> for the disconnect wording, always saw it
	/// already cleared. A hidden player who LOGOUTs (as opposed to QUITs -
	/// <c>ConnectionService.Disconnect</c> removes its state AFTER publishing, so QUIT never had this
	/// bug, per <see cref="Hide_ThenQuit_BroadcastsHiddenDisconnected"/>) got ordinary "has
	/// disconnected." wording instead of "has HIDDEN-disconnected."
	/// </summary>
	[Test]
	public async ValueTask Hide_ThenLogout_BroadcastsHiddenDisconnected()
	{
		var witness = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceWitness12");
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceHideLogout12");
		// @hide is permission-gated (wizard/royalty or the Hide power) - grant WIZARD.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		var room = await DigRoomAsync("AnnounceRoom12");
		await TeleportAsync(witness.DbRef, room);
		await TeleportAsync(testPlayer.DbRef, room);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));

		var playerName = (await KnownObjectAsync(testPlayer.DbRef)).Object().Name;
		var before = WebAppFactoryArg.Notifications.CountFor(witness.DbRef);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("LOGOUT"));

		var messages = MessagesTo(witness.DbRef, before);
		await Assert.That(messages).Contains($"{playerName} {ErrorMessages.Notifications.GameHasHiddenDisconnected}")
			.Because("Unbind must not clear the Hidden metadata key before the disconnect notification is " +
				"published, or ConnectionStateEventHandler's re-fetch of IsHidden for the disconnect " +
				"wording always sees it already cleared, producing the ordinary (non-hidden) wording " +
				"instead");
	}

	// --- Test 13: LASTLOGOUT keeps updating past the player's first-ever disconnect (Codex round 2, Finding 1) ---

	/// <summary>
	/// Regression test for the Codex review's Finding 1 on PR #902 (second round): LASTLOGOUT is
	/// seeded wizard-flagged (<c>AttributeEntrySeed.cs</c>: <c>("LASTLOGOUT", ["no_clone","wizard",
	/// "locked","prefixmatch"])</c>). Before the fix, <c>AnnounceDisconnectAsync</c> wrote it via
	/// <c>IAttributeService.SetAttributeAsync(player, player, "LASTLOGOUT", ...)</c> - the PLAYER's
	/// own authority. A player's first-ever disconnect creates the attribute (the pre-set permission
	/// check is a no-op against a non-existent attribute) with the seeded wizard flag baked into its
	/// instance flags. Every disconnect after that hit <c>PermissionService.CanSetInternal</c>'s
	/// <c>attribute.Any(a =&gt; a.IsWizard()) return false</c> guard, silently freezing LASTLOGOUT at
	/// its first-ever value forever for a non-wizard player. This test proves the value is genuinely
	/// updated on a SECOND disconnect, not merely that the call didn't throw - by (1) independently
	/// confirming a mortal's own <c>SetAttributeAsync</c> attempt against the now-existing attribute is
	/// denied (establishing the bug's exact mechanism would otherwise apply here), and (2) polling for
	/// the stored value to actually change after a second QUIT.
	/// </summary>
	[Test]
	public async ValueTask Quit_Twice_UpdatesLastLogoutOnTheSecondDisconnectToo()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AnnounceTwice13");

		var playerObj = await KnownObjectAsync(testPlayer.DbRef);
		var playerName = playerObj.Object().Name;

		// First-ever disconnect: LASTLOGOUT does not exist yet, so it gets created here - along with
		// the seeded wizard flag PennMUSH's own attribute-entry table stamps onto it.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("QUIT"));
		await TestHelpers.WaitForAttribute(AttributeService, playerObj, "LASTLOGOUT");

		var firstLogout = await AttributeService.GetAttributeAsync(
			playerObj, playerObj, "LASTLOGOUT", IAttributeService.AttributeMode.Read, false);
		await Assert.That(firstLogout.IsAttribute).IsTrue();
		var firstValue = firstLogout.AsAttribute.Last().Value.ToPlainText();

		// Confirms the exact mechanism of the bug: now that LASTLOGOUT exists and carries the seeded
		// wizard flag, a mortal player's OWN authority is denied from overwriting it. This is exactly
		// the permission gate AnnounceDisconnectAsync must bypass (via a direct SetAttributeCommand,
		// not IAttributeService.SetAttributeAsync) for the real disconnect flow to keep working.
		var mortalAttempt = await AttributeService.SetAttributeAsync(
			playerObj, playerObj, "LASTLOGOUT", MarkupText.Plain("mortal write should be denied"));
		await Assert.That(mortalAttempt.IsT1).IsTrue()
			.Because("LASTLOGOUT is seeded wizard-flagged, so once it exists a mortal player's own " +
				"authority must be denied - proving AnnounceDisconnectAsync would silently freeze " +
				"LASTLOGOUT after one update if it wrote through this same path");

		// Sleep past the one-second granularity of LASTLOGOUT's timestamp format so a real update is
		// distinguishable from a frozen value by more than coincidence.
		await Task.Delay(TimeSpan.FromSeconds(1.1));

		var handle = await AnonymousHandleAsync();
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"CONNECT {playerName} TestPassword123"));
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain("QUIT"));

		var deadline = DateTime.UtcNow.AddSeconds(10);
		var secondValue = firstValue;
		while (DateTime.UtcNow < deadline)
		{
			var attr = await AttributeService.GetAttributeAsync(
				playerObj, playerObj, "LASTLOGOUT", IAttributeService.AttributeMode.Read, false);
			if (attr.IsAttribute)
			{
				secondValue = attr.AsAttribute.Last().Value.ToPlainText();
				if (secondValue != firstValue) break;
			}
			await Task.Delay(100);
		}

		await Assert.That(secondValue).IsNotEqualTo(firstValue)
			.Because("the SECOND disconnect must actually update LASTLOGOUT - before the fix, writing " +
				"through IAttributeService.SetAttributeAsync as the player's own authority silently " +
				"failed once the attribute existed, freezing it at its first-ever value forever");
	}
}
