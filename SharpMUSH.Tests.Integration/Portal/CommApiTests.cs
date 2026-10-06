using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// <c>api/comm</c>: a channel's recall buffer, and the acting character's read markers, for the portal's
/// channel view and its unread counts.
///
/// <para>Driven through <see cref="CommController"/> directly, as <c>ObjectsControllerPermissionTests</c>
/// is: over HTTP the shared host authenticates every request as God, a wizard who passes every channel
/// lock, so a refusal could never be observed. Here each test picks the character it acts as.</para>
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class CommApiTests(ServerWebAppFactory factory)
{
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => factory.Services.GetRequiredService<IConnectionService>();

	private Task<CommController> As(DBRef actor) => PortalControllers.CommControllerAs(factory, actor);

	private Task God(string command) =>
		factory.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<DBRef> NewPlayerAsync(string prefix)
	{
		var number = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, prefix);
		return (await Mediator.Send(new GetObjectNodeQuery(number))).Expect<SharpPlayer>().Object.DBRef;
	}

	private static string UniqueChannel(string prefix) =>
		TestIsolationHelpers.GenerateUniqueName(prefix).Replace("_", string.Empty);

	private async Task<string> ObjidOf(DBRef who) =>
		(await factory.FunctionParser.FunctionParse(MarkupText.Plain($"objid(#{who.Number})")))!.Message!.ToPlainText();

	private async Task<string> GodObjid() =>
		(await factory.FunctionParser.FunctionParse(MarkupText.Plain("objid(#1)")))!.Message!.ToPlainText();

	/// <summary>An open player channel with God and <paramref name="members"/> on it.</summary>
	private async Task<string> ChannelAsync(string prefix, params DBRef[] members)
	{
		var name = UniqueChannel(prefix);
		await God($"@channel/add {name}=player open");
		await God($"@channel/on {name}=me");
		foreach (var member in members)
		{
			await God($"@channel/on {name}=#{member.Number}");
		}

		return name;
	}

	private static T Value<T>(ActionResult<T> result) =>
		result.Value ?? (result.Result is OkObjectResult { Value: T ok } ? ok : throw new InvalidOperationException(
			$"Expected a value, got {result.Result?.GetType().Name ?? "nothing"}."));

	private static int? Status<T>(ActionResult<T> result) => result.Result switch
	{
		ObjectResult objectResult => objectResult.StatusCode,
		StatusCodeResult statusCode => statusCode.StatusCode,
		_ => null
	};

	[Test]
	public async Task Recall_ReturnsAMembersLines_ShapedAsTheCommFeedShapesThem()
	{
		var reader = await NewPlayerAsync("CommRecallReader");
		var channel = await ChannelAsync("CommRecall", reader);
		var marker = TestIsolationHelpers.GenerateUniqueName("said");

		await God($"@chat {channel}=hello {marker}");
		await God($"@chat {channel}=:waves {marker}");

		var lines = Value(await (await As(reader)).Recall(channel, null, null, CancellationToken.None));

		var mine = lines.Where(line => line.Text.Contains(marker)).ToList();
		await Assert.That(mine.Count).IsEqualTo(2);
		var godObjid = await GodObjid();

		await Assert.That(mine[0].Channel).IsEqualTo(channel);
		await Assert.That(mine[0].Style).IsEqualTo("say");
		await Assert.That(mine[0].From).IsEqualTo("God");
		await Assert.That(mine[0].FromObjid).IsEqualTo(godObjid);
		await Assert.That(mine[0].Text).IsEqualTo($"hello {marker}");
		await Assert.That(mine[1].Style).IsEqualTo("pose");
		await Assert.That(mine[1].Text).IsEqualTo($"God waves {marker}").Because("a pose reads with its name, as comm.message's does");
		await Assert.That(mine[1].Id).IsGreaterThan(mine[0].Id).Because("ids rise with the buffer's order");
		await Assert.That(mine[0].Ts).IsGreaterThan(0);
	}

	/// <summary>
	/// <c>@channel/recall</c>'s own gate: not a member, and not able to join. The in-game command refuses
	/// the same player with "You must be able to join a channel to recall from it".
	/// </summary>
	[Test]
	public async Task Recall_OnAChannelTheActorMayNotJoin_IsForbidden()
	{
		var outsider = await NewPlayerAsync("CommRecallOutsider");
		var channel = UniqueChannel("CommRecallLocked");
		await God($"@channel/add {channel}=player");
		await God($"@clock/join {channel}=#1");

		var result = await (await As(outsider)).Recall(channel, null, null, CancellationToken.None);

		await Assert.That(Status(result)).IsEqualTo(StatusCodes.Status403Forbidden);
	}

	/// <summary>
	/// The member list is <c>@channel/who</c>'s: connected players and things, a member hiding on the channel
	/// only for a viewer who may see hidden members, and nobody who is not connected.
	/// </summary>
	[Test]
	public async Task Who_ListsTheConnectedMembers_AsChannelWhoDoes()
	{
		var viewer = await NewPlayerAsync("CommWhoViewer");
		var online = await NewPlayerAsync("CommWhoOnline");
		var offline = await NewPlayerAsync("CommWhoOffline");
		var hider = await NewPlayerAsync("CommWhoHidden");
		await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, online);
		var hiderHandle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, hider);
		var channel = UniqueChannel("CommWho");
		await God($"@channel/add {channel}=player open hide_ok");
		foreach (var member in new[] { viewer, online, offline, hider })
		{
			await God($"@channel/on {channel}=#{member.Number}");
		}
		await factory.CommandParser.CommandParse(hiderHandle, ConnectionService,
			MarkupText.Plain($"@channel/hide {channel}=yes"));

		var mortal = Value(await (await As(viewer)).Who(channel, CancellationToken.None));
		var staff = Value(await (await As(new DBRef(1))).Who(channel, CancellationToken.None));

		var (onlineObjid, hiderObjid, godObjid) = (await ObjidOf(online), await ObjidOf(hider), await GodObjid());

		// God made the channel, which put God on it, and is connected.
		await Assert.That(mortal.Channel).IsEqualTo(channel);
		await Assert.That(mortal.Members.Select(m => m.Objid)).IsEquivalentTo(new[] { godObjid, onlineObjid })
			.Because("the viewer is not connected, the offline member is not, and the hider is hidden from a mortal");
		await Assert.That(staff.Members.Select(m => m.Objid)).IsEquivalentTo(new[] { godObjid, onlineObjid, hiderObjid });
	}

	[Test]
	public async Task Who_OnAChannelTheActorCannotSee_IsNotFound()
	{
		var outsider = await NewPlayerAsync("CommWhoBlind");
		var hidden = UniqueChannel("CommWhoHiddenChan");
		await God($"@channel/add {hidden}=player wizard");
		await God($"@clock/join {hidden}=#1");

		var result = await (await As(outsider)).Who(hidden, CancellationToken.None);

		await Assert.That(Status(result)).IsEqualTo(StatusCodes.Status404NotFound);
	}

	/// <summary>A channel the actor may not see is answered exactly as one that does not exist.</summary>
	[Test]
	public async Task Recall_OnAChannelTheActorCannotSee_IsNotFound_AsAMissingOneIs()
	{
		var outsider = await NewPlayerAsync("CommRecallBlind");
		var hidden = UniqueChannel("CommRecallHidden");
		await God($"@channel/add {hidden}=player wizard");
		await God($"@clock/join {hidden}=#1");

		var controller = await As(outsider);
		var hiddenResult = await controller.Recall(hidden, null, null, CancellationToken.None);
		var missingResult = await controller.Recall(UniqueChannel("CommRecallMissing"), null, null, CancellationToken.None);

		await Assert.That(Status(hiddenResult)).IsEqualTo(StatusCodes.Status404NotFound);
		await Assert.That(Status(missingResult)).IsEqualTo(StatusCodes.Status404NotFound);
	}

	/// <summary>A non-member who could join may read the history, which is how recall helps decide.</summary>
	[Test]
	public async Task Recall_ByANonMemberWhoCouldJoin_IsAllowed()
	{
		var passerby = await NewPlayerAsync("CommRecallPasserby");
		var channel = await ChannelAsync("CommRecallOpen");
		var marker = TestIsolationHelpers.GenerateUniqueName("open");
		await God($"@chat {channel}={marker}");

		var lines = Value(await (await As(passerby)).Recall(channel, null, null, CancellationToken.None));

		await Assert.That(lines.Any(line => line.Text == marker)).IsTrue();
	}

	[Test]
	public async Task Recall_TakesTheLastLinesAskedFor()
	{
		var reader = await NewPlayerAsync("CommRecallTail");
		var channel = await ChannelAsync("CommRecallTail", reader);
		foreach (var n in Enumerable.Range(1, 5))
		{
			await God($"@chat {channel}=line {n}");
		}

		var lines = Value(await (await As(reader)).Recall(channel, 2, null, CancellationToken.None));

		await Assert.That(lines.Select(line => line.Text)).IsEquivalentTo(new[] { "line 4", "line 5" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// With a read marker's id, recall reaches back past the lines asked for to the first line after it, so a
	/// reader coming back gets all they missed; a marker inside the tail changes nothing.
	/// </summary>
	[Test]
	public async Task Recall_ReachesBackToTheLineAfterTheMarker()
	{
		var reader = await NewPlayerAsync("CommRecallAfter");
		var channel = await ChannelAsync("CommRecallAfter", reader);
		foreach (var n in Enumerable.Range(1, 6))
		{
			await God($"@chat {channel}=line {n}");
		}

		var controller = await As(reader);
		var all = Value(await controller.Recall(channel, null, null, CancellationToken.None));
		var seen = all.Single(line => line.Text == "line 2").Id;

		var missed = Value(await controller.Recall(channel, 2, seen, CancellationToken.None));
		var tail = Value(await controller.Recall(channel, 2, all.Single(line => line.Text == "line 5").Id,
			CancellationToken.None));

		await Assert.That(missed.Select(line => line.Text)).IsEquivalentTo(new[] { "line 3", "line 4", "line 5", "line 6" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(tail.Select(line => line.Text)).IsEquivalentTo(new[] { "line 5", "line 6" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// PennMUSH keeps the buffer on the channel, so <c>@channel/rename</c> keeps its history; the line is
	/// recalled under the new name with the id it had.
	/// </summary>
	[Test]
	public async Task Recall_KeepsTheHistoryThroughARename()
	{
		var reader = await NewPlayerAsync("CommRecallRename");
		var channel = await ChannelAsync("CommRecallOld", reader);
		var renamed = UniqueChannel("CommRecallNew");
		var marker = TestIsolationHelpers.GenerateUniqueName("before");
		await God($"@chat {channel}={marker}");
		var controller = await As(reader);
		var before = Value(await controller.Recall(channel, null, null, CancellationToken.None)).Single(line => line.Text == marker);

		await God($"@channel/rename {channel}={renamed}");

		var after = Value(await controller.Recall(renamed, null, null, CancellationToken.None)).Single(line => line.Text == marker);
		await Assert.That(after.Id).IsEqualTo(before.Id);
		await Assert.That(after.Channel).IsEqualTo(renamed);
	}

	[Test]
	public async Task AChannelMarker_IsTheActorsOwn_AndOnlyMovesForward()
	{
		var reader = await NewPlayerAsync("CommMarkReader");
		var other = await NewPlayerAsync("CommMarkOther");
		var channel = await ChannelAsync("CommMark", reader, other);
		var at = DateTimeOffset.UtcNow;

		var controller = await As(reader);
		Value(await controller.MarkChannel(channel, new ReadMarkerUpdate(200, at), CancellationToken.None));
		var behind = Value(await controller.MarkChannel(channel, new ReadMarkerUpdate(100, at.AddMinutes(1)), CancellationToken.None));

		await Assert.That(behind.LastReadId).IsEqualTo(200).Because("a device that is behind does not un-read the channel");

		var markers = Value(await controller.Markers(CancellationToken.None));
		await Assert.That(markers.Character).IsEqualTo(reader.ToString());
		var mark = markers.Channels.Single(m => m.Channel == channel);
		await Assert.That(mark.LastReadId).IsEqualTo(200);

		var theirs = Value(await (await As(other)).Markers(CancellationToken.None));
		await Assert.That(theirs.Channels.Any(m => m.Channel == channel)).IsFalse();
	}

	/// <summary>Line ids issued while the clock ran behind them (stopped, or set back).</summary>
	private sealed class AheadOfTheClock(long latest) : IChannelMessageIdSource
	{
		public long Latest => latest;
		public ValueTask<long> NextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(latest);
	}

	/// <summary>
	/// A marker id is bounded by the largest id issued, not by the clock: ids run ahead of the clock when
	/// it stops or is set back, and a marker cut down to the time would bring read lines back as unread.
	/// </summary>
	[Test]
	public async Task AChannelMarker_KeepsAnIdIssuedAheadOfTheClock()
	{
		var reader = await NewPlayerAsync("CommMarkAhead");
		var channel = await ChannelAsync("CommMarkAhead", reader);
		var now = (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond;
		var issued = now + 3_600_000_000;

		var controller = await PortalControllers.CommControllerAs(factory, reader, new AheadOfTheClock(issued + 10));
		var mark = Value(await controller.MarkChannel(channel, new ReadMarkerUpdate(issued, DateTimeOffset.UtcNow),
			CancellationToken.None));
		var beyond = Value(await controller.MarkChannel(channel, new ReadMarkerUpdate(issued + 1_000_000, DateTimeOffset.UtcNow),
			CancellationToken.None));

		await Assert.That(mark.LastReadId).IsEqualTo(issued);
		await Assert.That(beyond.LastReadId).IsEqualTo(issued + 10).Because("no line has an id past the last one issued");
	}

	/// <summary>
	/// A channel the character is not on but may read (a game listing joinable channels with
	/// <c>"joined": false</c>) keeps its marker too; one they can no longer see or join does not come back.
	/// </summary>
	[Test]
	public async Task Markers_IncludeAJoinableChannelTheCharacterIsNotOn_AndNotOneTheyCannotRead()
	{
		var passerby = await NewPlayerAsync("CommMarkPasserby");
		var open = await ChannelAsync("CommMarkOpen");
		var closing = await ChannelAsync("CommMarkClosing");

		var controller = await As(passerby);
		Value(await controller.MarkChannel(open, new ReadMarkerUpdate(11, DateTimeOffset.UtcNow), CancellationToken.None));
		Value(await controller.MarkChannel(closing, new ReadMarkerUpdate(12, DateTimeOffset.UtcNow), CancellationToken.None));
		await God($"@clock/join {closing}=#1");

		var markers = Value(await controller.Markers(CancellationToken.None));

		await Assert.That(markers.Channels.Single(m => m.Channel == open).LastReadId).IsEqualTo(11);
		await Assert.That(markers.Channels.Any(m => m.Channel == closing)).IsFalse()
			.Because("@channel/recall would refuse it now, so its marker says nothing the portal may show");
	}

	/// <summary>A marker follows the channel, not its name.</summary>
	[Test]
	public async Task AChannelMarker_SurvivesARename()
	{
		var reader = await NewPlayerAsync("CommMarkRename");
		var channel = await ChannelAsync("CommMarkOld", reader);
		var renamed = UniqueChannel("CommMarkNew");

		var controller = await As(reader);
		Value(await controller.MarkChannel(channel, new ReadMarkerUpdate(7, DateTimeOffset.UtcNow), CancellationToken.None));
		await God($"@channel/rename {channel}={renamed}");

		var markers = Value(await controller.Markers(CancellationToken.None));
		await Assert.That(markers.Channels.Single(m => m.Channel == renamed).LastReadId).IsEqualTo(7);
	}

	[Test]
	public async Task AMarker_OnAChannelTheActorCannotSee_IsNotFound()
	{
		var outsider = await NewPlayerAsync("CommMarkBlind");
		var hidden = UniqueChannel("CommMarkHidden");
		await God($"@channel/add {hidden}=player wizard");
		await God($"@clock/join {hidden}=#1");

		var result = await (await As(outsider)).MarkChannel(hidden, new ReadMarkerUpdate(1, DateTimeOffset.UtcNow),
			CancellationToken.None);

		await Assert.That(Status(result)).IsEqualTo(StatusCodes.Status404NotFound);
	}

	/// <summary>
	/// A page conversation is keyed by the other people in it, in any order, by objid: the viewer's own
	/// objid is dropped, so either spelling names the same conversation.
	/// </summary>
	[Test]
	public async Task AConversationMarker_IsKeyedByTheOthersInIt()
	{
		var reader = await NewPlayerAsync("CommPageReader");
		var tomas = await NewPlayerAsync("CommPageTomas");
		var dace = await NewPlayerAsync("CommPageDace");
		// Markers keep the millisecond, as comm.message's ts does.
		var at = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

		var controller = await As(reader);
		Value(await controller.MarkConversation(
			new ConversationReadMarkerUpdate([dace.ToString(), tomas.ToString()], null, at), CancellationToken.None));
		var same = Value(await controller.MarkConversation(
			new ConversationReadMarkerUpdate([tomas.ToString(), reader.ToString(), dace.ToString()], null, at.AddMinutes(-1)),
			CancellationToken.None));

		await Assert.That(same.LastReadAt).IsEqualTo(at).Because("the same conversation, and an earlier time does not move it back");

		var markers = Value(await controller.Markers(CancellationToken.None));
		var conversation = markers.Conversations.Single();
		await Assert.That(conversation.With).IsEquivalentTo(new[] { tomas.ToString(), dace.ToString() });
		await Assert.That(conversation.LastReadId).IsNull();
	}

	[Test]
	[Arguments("Tomas")]
	[Arguments("#7")]
	public async Task AConversationMarker_NamingSomeoneOtherThanByObjid_IsABadRequest(string who)
	{
		var reader = await NewPlayerAsync("CommPageBad");

		var result = await (await As(reader)).MarkConversation(
			new ConversationReadMarkerUpdate([who], null, DateTimeOffset.UtcNow), CancellationToken.None);

		await Assert.That(Status(result)).IsEqualTo(StatusCodes.Status400BadRequest);
	}

	[Test]
	public async Task WithoutACharacter_EveryEndpointIsUnauthorized()
	{
		var controller = PortalControllers.CommControllerFor(factory, new System.Security.Claims.ClaimsIdentity());

		await Assert.That(Status(await controller.Recall("Public", null, null, CancellationToken.None)))
			.IsEqualTo(StatusCodes.Status401Unauthorized);
		await Assert.That(Status(await controller.Markers(CancellationToken.None)))
			.IsEqualTo(StatusCodes.Status401Unauthorized);
	}
}
