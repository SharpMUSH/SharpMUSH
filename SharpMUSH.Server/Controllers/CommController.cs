using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
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
/// Channel history, page history and read markers for the portal's channel view, as the session's acting
/// character.
///
/// Routes:
///   GET /api/comm/channels/{channel}/recall?lines=N&amp;after=ID — the channel's recall buffer (the last N lines, or
///                                                    all; with after, also every line after that id)
///   GET /api/comm/channels/{channel}/who             — who is on the channel now, as @channel/who lists them
///   GET /api/comm/markers                            — the character's read markers
///   PUT /api/comm/markers/channels/{channel}         — move a channel's marker on
///   PUT /api/comm/markers/conversations              — move a page conversation's marker on
///   GET /api/comm/conversations                      — the character's logged page conversations
///   GET /api/comm/conversations/{key}/recall?lines=N&amp;after=ID — one of them: its last N logged pages (with
///                                                    after, also every page after that id, within the cap)
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
///
/// <para>The conversation endpoints read the page log (<c>page_log</c>, a SharpMUSH extension, on by
/// default): the character's own copies only. Each route reads the session's character's log and takes no
/// one else's name, so nobody, staff included, can read another character's pages here. With the option
/// off they answer with nothing and <c>logging: false</c>, so the portal can say why there is no
/// history. A page's <c>text</c> is composed through <c>FN`COMM`TEXT</c> as a channel line's is.</para>
/// </summary>
[ApiController]
[Route("api/comm")]
[Authorize]
public class CommController(
	IMediator mediator,
	IChannelPermissionService channelPermissions,
	INotifyService notifyService,
	IVisibleWorldProjection projection,
	CommTextComposer textComposer,
	IChannelMessageIdSource messageIds,
	IConnectionService connectionService,
	IOptionsWrapper<SharpMUSHOptions> options) : ControllerBase
{
	/// <summary>The most people one conversation marker may name; a page to more than this is not a conversation.</summary>
	public const int ConversationLimit = PageConversation.MaxOthers;

	/// <summary>The most logged pages one conversation recall returns, and what it returns when not asked for fewer.</summary>
	public const int PageRecallLimit = CommLimits.PageRecallMaxLines;

	/// <summary>
	/// The most conversations the listing returns, the latest: as many as the portal's feed keeps
	/// (<c>OobCommFeed.ConversationLimit</c>), read without reading the rest.
	/// </summary>
	public const int PageConversationListLimit = CommLimits.PageConversationListMax;

	private bool PageLogOn => options.CurrentValue.Chat.PageLog;

	/// <summary>
	/// The character's own logged page conversations, the latest <see cref="PageConversationListLimit"/>, latest
	/// first. Nobody else's: there is no way to
	/// name another character's log here, and no staff variant of it.
	/// </summary>
	[HttpGet("conversations")]
	public async Task<ActionResult<PageConversations>> Conversations(CancellationToken ct)
	{
		if (await User.ResolvePlayerAsync(projection, ct) is not { } player) return Unauthorized();

		var character = player.Object.DBRef;
		if (!PageLogOn) return new PageConversations(character.ToString(), false, []);

		var conversations = await mediator.Send(new GetPageConversationsQuery(character, PageConversationListLimit), ct);
		return new PageConversations(character.ToString(), true, conversations
			.Select(conversation => new PageConversationSummary(
				conversation.With.Select(other => other.ToString()).ToArray(),
				conversation.Names,
				conversation.LastId,
				conversation.LastAt))
			.ToList());
	}

	/// <summary>
	/// The last <paramref name="lines"/> pages (at most <see cref="PageRecallLimit"/>) of the character's own
	/// conversation with the people <paramref name="key"/> names: their objids, separated by spaces or
	/// commas, in any order, the character's own ignored (or alone, for pages to themselves). With
	/// <paramref name="after"/>, a read marker's id, it reaches further back when it must, to the first page
	/// after that id, never past the last <see cref="PageRecallLimit"/>. With <c>page_log</c> off it answers with
	/// no lines and says so.
	/// </summary>
	[HttpGet("conversations/{key}/recall")]
	public async Task<ActionResult<PageRecall>> ConversationRecall(string key, [FromQuery] int? lines,
		[FromQuery] long? after, CancellationToken ct)
	{
		if (await User.ResolvePlayerAsync(projection, ct) is not { } player) return Unauthorized();
		if (lines is < 0) return BadRequest(new { error = "lines must be zero or more." });

		return ConversationWith(player, key.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries), selfAlone: true) switch
		{
			IReadOnlyList<DBRef> with => await ConversationRecallAsync(player, with,
				lines is null or 0 ? PageRecallLimit : Math.Min(lines.Value, PageRecallLimit), after, ct),
			ActionResult refusal => refusal
		};
	}

	private async Task<ActionResult<PageRecall>> ConversationRecallAsync(SharpPlayer player, IReadOnlyList<DBRef> with,
		int lines, long? after, CancellationToken ct)
	{
		if (!PageLogOn) return new PageRecall(false, []);

		var pages = await mediator.Send(
			new GetPageLogQuery(player.Object.DBRef, with, after is null ? lines : PageRecallLimit), ct);
		if (after is { } seen)
		{
			var log = pages.ToList();
			var unseen = log.FindIndex(page => page.Id > seen);
			var tail = Math.Max(0, log.Count - lines);
			var start = unseen < 0 ? tail : Math.Min(unseen, tail);
			pages = log.GetRange(start, log.Count - start);
		}

		var handler = await textComposer.HandlerAsync(ct);
		var recalled = new List<PageRecallLine>(pages.Count);
		foreach (var page in pages)
		{
			recalled.Add(await ToRecallLineAsync(page, handler, ct));
		}

		return new PageRecall(true, recalled);
	}

	/// <summary>
	/// The people a conversation names, by objid, without the character themselves; or the 400 refusing
	/// it. With <paramref name="selfAlone"/>, naming only oneself is the conversation of pages to oneself.
	/// </summary>
	private ValueOrResponse<IReadOnlyList<DBRef>> ConversationWith(SharpPlayer player, IEnumerable<string> objids,
		bool selfAlone)
	{
		var self = player.Object.DBRef;
		var others = new List<DBRef>();
		var namedSelf = false;
		foreach (var objid in objids)
		{
			if (!DBRef.TryParse(objid, out var other) || other is not { IsObjid: true } parsed)
			{
				return BadRequest(new { error = $"'{objid}' is not an objid." });
			}

			if (parsed.Number == self.Number && parsed.CreationMilliseconds == self.CreationMilliseconds)
			{
				namedSelf = true;
			}
			else
			{
				others.Add(parsed);
			}
		}

		if (others.Count is 0 && namedSelf && selfAlone)
		{
			others.Add(self);
		}

		if (others.Count is 0 or > ConversationLimit)
		{
			return BadRequest(new { error = $"A conversation names between 1 and {ConversationLimit} other people." });
		}

		return PageConversation.Normalize(others).ToArray();
	}

	/// <summary>A logged page shaped as its <c>comm.message</c>, <c>text</c> composed by <see cref="CommTextComposer"/>.</summary>
	private async ValueTask<PageRecallLine> ToRecallLineAsync(SharpPage page, AnySharpObject? handler,
		CancellationToken ct) => new(
		page.Id,
		page.RecipientNames,
		page.Recipients.Select(recipient => recipient.ToString()).ToArray(),
		page.SenderName,
		page.Sender.ToString(),
		await textComposer.ComposeAsync(handler, page, ct),
		page.Style,
		page.Timestamp.ToUnixTimeMilliseconds());

	/// <summary>
	/// The last <paramref name="lines"/> lines of the channel's recall buffer (every line when not given, or 0).
	/// With <paramref name="after"/>, a read marker's id, it reaches further back when it must, to the first
	/// line after that id, so a reader coming back gets everything they have not seen that the buffer still holds.
	/// </summary>
	[HttpGet("channels/{channel}/recall")]
	public async Task<ActionResult<IReadOnlyList<ChannelRecallLine>>> Recall(string channel, [FromQuery] int? lines,
		[FromQuery] long? after, CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } executor) return Unauthorized();
		if (lines is < 0) return BadRequest(new { error = "lines must be zero or more." });

		return await ReadableChannelAsync(executor, channel) switch
		{
			SharpChannel found => await RecallAsync(executor, found, lines is null or 0 ? int.MaxValue : lines.Value, after,
				ct),
			ActionResult refusal => refusal
		};
	}

	private async Task<ActionResult<IReadOnlyList<ChannelRecallLine>>> RecallAsync(AnySharpObject executor,
		SharpChannel channel, int lines, long? after, CancellationToken ct)
	{
		var buffered = await mediator
			.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, after is null ? lines : int.MaxValue), ct)
			.ToListAsync(ct);
		if (after is { } seen)
		{
			var unseen = buffered.FindIndex(line => line.Id > seen);
			var from = Math.Max(0, buffered.Count - lines);
			var start = unseen < 0 ? from : Math.Min(unseen, from);
			buffered = buffered.GetRange(start, buffered.Count - start);
		}
		var name = channel.Name.ToPlainText();
		var handler = await textComposer.HandlerAsync(ct);

		var recalled = new List<ChannelRecallLine>();
		foreach (var line in await ChannelHelper.FilterRecallableAsync(buffered, executor))
		{
			recalled.Add(await ToRecallLineAsync(name, line, handler, ct));
		}

		return recalled;
	}

	/// <summary>
	/// The channel's members as <c>@channel/who</c> lists them for the acting character, behind its gate: a
	/// channel the character may not be told exists answers 404, as a missing one does.
	/// </summary>
	[HttpGet("channels/{channel}/who")]
	public async Task<ActionResult<ChannelWhoList>> Who(string channel, CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } executor) return Unauthorized();

		if (await ChannelHelper.GetVisibleChannelOrError(channelPermissions, mediator, notifyService, executor,
				MarkupText.Plain(channel)) is not SharpChannel found)
		{
			return NotFound();
		}

		var privilegedWho = await ChannelHelper.PrivilegedWho(executor);
		var members = (await ChannelHelper.ChannelMembers(connectionService, found))
			.Where(member => member.ListedAsOn(privilegedWho))
			.Select(member => new ChannelWhoMember(member.Object.Object().Name, member.Object.Object().DBRef.ToString()))
			.ToList();

		return new ChannelWhoList(found.Name.ToPlainText(), members);
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
				&& await ChannelHelper.CanSeeChannel(channelPermissions, mediator, player, channel)
				&& await ChannelRecall.MayRecallAsync(channelPermissions, mediator, player, channel))
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

		return ConversationWith(player, update.With ?? [], selfAlone: true) switch
		{
			IReadOnlyList<DBRef> with => await MarkConversationAsync(player, with, update, ct),
			ActionResult refusal => refusal
		};
	}

	private async Task<ActionResult<ConversationReadMarker>> MarkConversationAsync(SharpPlayer player,
		IReadOnlyList<DBRef> others, ConversationReadMarkerUpdate update, CancellationToken ct)
	{
		var stored = await AdvanceAsync(player, ReadMarkerScope.Conversation(others), update.LastReadId,
			update.LastReadAt, ct);
		ReadMarkerScope.IsConversation(stored.Scope, out var with);
		return new ConversationReadMarker(with, stored.LastReadId, stored.LastReadAt);
	}

	/// <summary>
	/// Moves the marker on. Neither part may be later than any real line: the time no later than now, and
	/// the id no larger than the largest id issued — which can run ahead of the clock when it stops or is
	/// set back, so the clock alone would cut a real marker down and bring read lines back as unread. After
	/// a restart that is the previous process's ids too, which <see cref="IChannelMessageIdSource.CeilingAsync"/>
	/// loads before this process has handed out any. A marker set past every line would call every line
	/// until then read.
	/// </summary>
	private async Task<SharpReadMarker> AdvanceAsync(SharpPlayer player, string scope, long? lastReadId,
		DateTimeOffset lastReadAt, CancellationToken ct)
	{
		var now = DateTimeOffset.UtcNow;
		var latestId = Math.Max((now - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond,
			await messageIds.CeilingAsync(ct));
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
		if (await ChannelHelper.GetVisibleChannelOrError(channelPermissions, mediator, notifyService, viewer,
				MarkupText.Plain(name)) is not SharpChannel channel)
		{
			return NotFound();
		}

		return await ChannelRecall.MayRecallAsync(channelPermissions, mediator, viewer, channel)
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
