using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Channel history and read markers for the portal's channel view, as the session's acting character.
///
/// Routes:
///   GET /api/comm/channels/{channel}/recall?lines=N — the channel's recall buffer (the last N lines, or all)
///   GET /api/comm/markers                            — the character's read markers
///   PUT /api/comm/markers/channels/{channel}         — move a channel's marker on
///   PUT /api/comm/markers/conversations              — move a page conversation's marker on
///
/// <para>The recall endpoint is <c>@channel/recall</c>'s buffer behind <c>@channel/recall</c>'s gates, both
/// taken from the command rather than restated: the channel must be one the character may be told exists
/// (<see cref="ChannelHelper.GetVisibleChannelOrError"/>; a hidden channel answers 404 as a missing one
/// does), the character must be on it or able to join it (<see cref="ChannelRecall.MayRecallAsync"/>, 403),
/// and a line only See_All members were sent stays hidden (<see cref="ChannelHelper.FilterRecallableAsync"/>).
/// Gagging a channel does not stop <c>@channel/recall</c>, and does not stop this.</para>
///
/// <para>Each line is shaped as the <c>comm-feed</c> package shapes its <c>comm.message</c> push, and
/// carries the same id, so the portal keeps one copy of a line it both pulled and was pushed. Its
/// <c>text</c> goes through the game's installed <c>FN`COMM`TEXT</c> as the push's does
/// (<see cref="CommTextComposer"/>), so a game that redefined it gets the same text both ways.</para>
/// </summary>
[ApiController]
[Route("api/comm")]
[Authorize]
public class CommController(
	IMediator mediator,
	IPermissionService permissionService,
	INotifyService notifyService,
	IVisibleWorldProjection projection,
	CommTextComposer textComposer,
	IChannelMessageIdSource messageIds) : ControllerBase
{
	/// <summary>The most people one conversation marker may name; a page to more than this is not a conversation.</summary>
	public const int ConversationLimit = 32;

	[HttpGet("channels/{channel}/recall")]
	public async Task<ActionResult<IReadOnlyList<ChannelRecallLine>>> Recall(string channel, [FromQuery] int? lines,
		CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } executor) return Unauthorized();
		if (lines is < 0) return BadRequest(new { error = "lines must be zero or more." });

		return await ReadableChannelAsync(executor, channel) switch
		{
			SharpChannel found => await RecallAsync(executor, found, lines is null or 0 ? int.MaxValue : lines.Value, ct),
			ActionResult refusal => refusal
		};
	}

	private async Task<ActionResult<IReadOnlyList<ChannelRecallLine>>> RecallAsync(AnySharpObject executor,
		SharpChannel channel, int lines, CancellationToken ct)
	{
		var buffered = await mediator
			.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, lines), ct)
			.ToListAsync(ct);
		var name = channel.Name.ToPlainText();
		var handler = await textComposer.HandlerAsync(ct);

		var recalled = new List<ChannelRecallLine>();
		foreach (var line in await ChannelHelper.FilterRecallableAsync(buffered, executor))
		{
			recalled.Add(await ToRecallLineAsync(name, line, handler, ct));
		}

		return recalled;
	}

	[HttpGet("markers")]
	public async Task<ActionResult<CommReadMarkers>> Markers(CancellationToken ct)
	{
		if (await User.ResolvePlayerAsync(projection, ct) is not { } player) return Unauthorized();

		var character = player.Object.DBRef;
		var markers = await mediator.Send(new GetReadMarkersQuery(character), ct);
		var byScope = markers.ToDictionary(marker => marker.Scope, StringComparer.Ordinal);

		// Every channel with a marker that the character may still read — the gate a marker is set behind,
		// so a joinable channel they are not on (a game listing those with "joined": false) is included —
		// under its name now, since a marker follows its channel through a rename. One they can no longer
		// see or join says nothing the portal may show.
		var channels = new List<ChannelReadMarker>();
		await foreach (var channel in mediator.CreateStream(new GetChannelListQuery(), ct))
		{
			if (channel.Id is { } id
				&& byScope.TryGetValue(ReadMarkerScope.Channel(id), out var marker)
				&& await ChannelHelper.CanSeeChannel(permissionService, player, channel)
				&& await ChannelRecall.MayRecallAsync(permissionService, player, channel))
			{
				channels.Add(new ChannelReadMarker(channel.Name.ToPlainText(), marker.LastReadId, marker.LastReadAt));
			}
		}

		var conversations = markers
			.Select(marker => ReadMarkerScope.IsConversation(marker.Scope, out var others)
				? new ConversationReadMarker(others, marker.LastReadId, marker.LastReadAt)
				: null)
			.OfType<ConversationReadMarker>()
			.ToList();

		return new CommReadMarkers(character.ToString(), channels, conversations);
	}

	/// <summary>A channel's marker, behind the recall gates: one may mark read only what one may read.</summary>
	[HttpPut("markers/channels/{channel}")]
	public async Task<ActionResult<ChannelReadMarker>> MarkChannel(string channel, [FromBody] ReadMarkerUpdate update,
		CancellationToken ct)
	{
		if (await User.ResolvePlayerAsync(projection, ct) is not { } player) return Unauthorized();

		return await ReadableChannelAsync(player, channel) switch
		{
			SharpChannel found => await MarkChannelAsync(player, found, update, ct),
			ActionResult refusal => refusal
		};
	}

	private async Task<ActionResult<ChannelReadMarker>> MarkChannelAsync(SharpPlayer player, SharpChannel channel,
		ReadMarkerUpdate update, CancellationToken ct)
	{
		var stored = await AdvanceAsync(player,
			ReadMarkerScope.Channel(channel.Id ?? string.Empty), update.LastReadId, update.LastReadAt, ct);
		return new ChannelReadMarker(channel.Name.ToPlainText(), stored.LastReadId, stored.LastReadAt);
	}

	/// <summary>
	/// A conversation's marker, keyed by the other people in it by objid. Nobody is looked up: a
	/// conversation outlives its players, and a marker names only who the character itself paged with.
	/// </summary>
	[HttpPut("markers/conversations")]
	public async Task<ActionResult<ConversationReadMarker>> MarkConversation(
		[FromBody] ConversationReadMarkerUpdate update, CancellationToken ct)
	{
		if (await User.ResolvePlayerAsync(projection, ct) is not { } player) return Unauthorized();

		var self = player.Object.DBRef;
		var others = new List<DBRef>();
		foreach (var objid in update.With ?? [])
		{
			if (!DBRef.TryParse(objid, out var other) || other is not { IsObjid: true } parsed)
			{
				return BadRequest(new { error = $"'{objid}' is not an objid." });
			}

			if (parsed.Number != self.Number || parsed.CreationMilliseconds != self.CreationMilliseconds)
			{
				others.Add(parsed);
			}
		}

		if (others.Count is 0 or > ConversationLimit)
		{
			return BadRequest(new { error = $"A conversation names between 1 and {ConversationLimit} other people." });
		}

		var stored = await AdvanceAsync(player, ReadMarkerScope.Conversation(others), update.LastReadId,
			update.LastReadAt, ct);
		ReadMarkerScope.IsConversation(stored.Scope, out var with);
		return new ConversationReadMarker(with, stored.LastReadId, stored.LastReadAt);
	}

	/// <summary>
	/// Moves the marker on. Neither part may be later than any real line: the time no later than now, and
	/// the id no larger than the largest id issued — which can run ahead of the clock when it stops or is
	/// set back, so the clock alone would cut a real marker down and bring read lines back as unread. A
	/// marker set past every line would call every line until then read.
	/// </summary>
	private async Task<SharpReadMarker> AdvanceAsync(SharpPlayer player, string scope, long? lastReadId,
		DateTimeOffset lastReadAt, CancellationToken ct)
	{
		var now = DateTimeOffset.UtcNow;
		var latestId = Math.Max((now - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond, messageIds.Latest);
		var marker = new SharpReadMarker(scope,
			lastReadId is { } id ? Math.Clamp(id, 0, latestId) : null,
			lastReadAt > now ? now : lastReadAt);
		return await mediator.Send(new AdvanceReadMarkerCommand(player.Object.DBRef, marker), ct);
	}

	/// <summary>
	/// <paramref name="name"/> resolved as <c>@channel/recall</c> resolves it, and passed through its gate,
	/// or the response that refuses it.
	/// </summary>
	private async Task<ValueOrResponse<SharpChannel>> ReadableChannelAsync(AnySharpObject viewer, string name)
	{
		if (await ChannelHelper.GetVisibleChannelOrError(permissionService, mediator, notifyService, viewer,
				MarkupText.Plain(name)) is not SharpChannel channel)
		{
			return NotFound();
		}

		return await ChannelRecall.MayRecallAsync(permissionService, viewer, channel)
			? channel
			: StatusCode(StatusCodes.Status403Forbidden, new { error = "You must be able to join a channel to read it." });
	}

	/// <summary>
	/// A buffered line shaped as its <c>comm.message</c>, <c>text</c> composed by
	/// <see cref="CommTextComposer"/>. A line written straight into the buffer (<c>cbufferadd()</c>) was
	/// never pushed and has no parts, and reads as an unattributed emit of the whole line.
	/// </summary>
	private async ValueTask<ChannelRecallLine> ToRecallLineAsync(string channel, SharpChannelMessage line,
		AnySharpObject? handler, CancellationToken ct)
	{
		var ts = line.Timestamp.ToUnixTimeMilliseconds();
		if (line.Style.Length == 0)
		{
			return new ChannelRecallLine(line.Id, channel, string.Empty, null, line.Message.ToPlainText(), "emit", ts);
		}

		var named = line.SpeakerName.Length > 0;
		return new ChannelRecallLine(line.Id, channel, line.SpeakerName, named ? line.Sender.ToString() : null,
			await textComposer.ComposeAsync(handler, line, ct), line.Style, ts);
	}
}
