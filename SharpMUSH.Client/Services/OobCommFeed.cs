using SharpMUSH.Client.Models;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>
/// <see cref="ICommFeed"/> over the OOB pushes of the <c>comm-feed</c> package: <c>comm.channels</c> (the
/// viewer's channels, sent whole) and <c>comm.message</c> (one channel line or page, sent only to who
/// received it), read from an <see cref="IOobChannelStore"/> as they arrive.
/// </summary>
/// <remarks>
/// <para><b>Unread.</b> The game has no notion of what a player has read, so the feed counts: a line from
/// someone else arriving for a key that is not <see cref="Viewing"/> is unread until
/// <see cref="MarkRead"/>. The viewer is who the latest <c>comm.channels</c> says it was built for, or else
/// the <c>room.contents</c> row marked <c>you</c>. A list that does carry a count for a channel (a game
/// that tracks it), 0 included, replaces the feed's own. A channel a new list no longer carries is
/// forgotten, history and count.</para>
/// <para><b>Conversations.</b> A page's key is its participants — the pager and every recipient, the
/// viewer included — by objid where the payload has one and by name otherwise, sorted, so every page
/// among the same people is one conversation whoever sent it. It starts <c>page </c>, and a channel name
/// cannot hold a space, so the two kinds of key never meet. The <see cref="ConversationLimit"/> most
/// recent are kept.</para>
/// <para><b>History and read markers.</b> Given an <see cref="ICommHistory"/>, the feed is also the
/// server's: once a <c>comm.channels</c> says whose feed it is, it reads that character's read markers
/// and pulls each channel's recall buffer (and a channel's again on <see cref="LoadHistoryAsync"/>): back to
/// the channel's marker where it has one, so the viewer sees all they missed that the buffer still holds, and
/// the whole buffer where it has none. A
/// line with an id is kept once however it arrived, pulled, pushed, or replayed on a resumed connection.
/// A key with a marker counts as unread only what came after it from someone else — so the count survives
/// a reload and a change of device — and a key without one counts lines as they arrive, as before.
/// <see cref="MarkRead"/>, and a line arriving for <see cref="Viewing"/>, move the server's marker to the
/// key's last line. The markers are the session's acting character's: a feed whose viewer is someone else
/// (the server says whose they are) uses none and writes none. A conversation's marker is keyed by the
/// others in it by objid and holds its last page's id and time; one with someone known only by name is not
/// marked.</para>
/// <para><b>Page log.</b> When the game keeps one (<c>page_log</c>), the feed lists the viewer's conversations
/// from it once the markers are read, so a reload keeps them, and pulls those whose last page is past their
/// marker, back to the marker, so their unread counts survive too, and those with no marker, as far back as
/// the server gives; a conversation is pulled again on <see cref="LoadHistoryAsync"/>.
/// <see cref="PageLogging"/> says whether the game keeps one, for the view to say so.</para>
/// <para><b>Clearing.</b> The store raises <see cref="IOobChannelStore.ChannelUpdated"/> for each package
/// it drops, with nothing left to read (a new connection, or a character switch through
/// <see cref="OobChannelStoreProxy"/>), and the feed drops everything with it, <see cref="Viewing"/>
/// included, and raises <see cref="Changed"/> once.</para>
/// <para>It assumes, as the store does, a single-threaded dispatcher (Blazor WASM).</para>
/// </remarks>
public sealed class OobCommFeed : ICommFeed, IDisposable
{
	/// <summary>
	/// How many lines are kept per channel or conversation; older ones are dropped. A pull can bring back more — a
	/// channel's backfill reaches to its read marker, or takes the whole recall buffer — and a key keeps as many
	/// as its pulls brought back.
	/// </summary>
	public const int HistoryLimit = 200;

	/// <summary>How many page conversations are kept; the least recent are dropped, history and all.</summary>
	public const int ConversationLimit = 100;

	private const string ConversationKeyPrefix = "page ";

	private readonly IOobChannelStore _store;
	private readonly TimeProvider _time;

	private readonly ICommHistory? _server;

	private readonly Dictionary<string, List<CommMessage>> _history = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, int> _unread = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>By key, how many lines it keeps when a pull brought back more than <see cref="HistoryLimit"/>.</summary>
	private readonly Dictionary<string, int> _kept = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);
	private IReadOnlyList<CommChannel> _channels = [];
	private CommParticipant? _viewer;
	private string? _viewing;

	/// <summary>Where the viewer has read up to, by key, as the server has it plus what this feed has since moved.</summary>
	private readonly Dictionary<string, Marker> _markers = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The channels pulled since the markers were read.</summary>
	private readonly HashSet<string> _pulled = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The channels whose count the latest list carried: the game's own, which a marker does not recount.</summary>
	private readonly HashSet<string> _serverCounted = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The objid the markers were read for, or null while there are none to use.</summary>
	private string? _syncedFor;

	/// <summary>The objid a read of the markers was started for.</summary>
	private string? _syncingFor;

	/// <summary>Moves on with every clear, so an answer to a request made before it is dropped.</summary>
	private int _generation;

	private Task _sync = Task.CompletedTask;

	/// <summary>The play connection, whose drops call for a pull of everything once the character is back.</summary>
	private readonly ITerminalService? _connection;

	/// <summary>How many times the connection has dropped, and how many of those a full pull has since covered.</summary>
	private int _drops;
	private int _resyncedDrops;

	/// <summary>By conversation, the ids of lines that came from the page log and have not been pushed.</summary>
	private readonly Dictionary<string, HashSet<long>> _pulledOnly = new(StringComparer.Ordinal);

	/// <summary>Conversations known only from the page log's listing: no page has been pushed for them.</summary>
	private readonly HashSet<string> _listedOnly = new(StringComparer.Ordinal);

	/// <summary>Whether the conversations have been listed from the page log since the markers were read.</summary>
	private bool _conversationsListed;

	/// <summary>Whether the game keeps a page log, as the server last said; null until it has.</summary>
	private bool? _pageLogging;

	/// <param name="connection">
	/// The connection the store is fed from. A reconnect logs in again without clearing the store, and nothing
	/// sent while it was down is replayed, so after a drop the next <c>comm.channels</c> (sent on connect) pulls
	/// every channel and conversation again, back to its marker.
	/// </param>
	public OobCommFeed(IOobChannelStore store, TimeProvider? time = null, ICommHistory? history = null,
		ITerminalService? connection = null)
	{
		_store = store;
		_time = time ?? TimeProvider.System;
		_server = history;
		_connection = connection;
		_store.ChannelUpdated += OnChannelUpdated;
		if (_connection is not null) _connection.ConnectionStateChanged += OnConnectionStateChanged;
	}

	private void OnConnectionStateChanged(bool connected)
	{
		if (!connected) _drops++;
	}

	public event Action? Changed;

	public IReadOnlyList<CommChannel> Channels =>
		_channels.Select(channel => channel with { Unread = UnreadFor(channel.Name) }).ToArray();

	/// <remarks>
	/// Who a conversation is with is worked out as it is read, against the viewer as now known, so one
	/// filed before the viewer was known stops listing them once they are. Its key never changes: it names
	/// every participant, the viewer included, whether or not the viewer was known when it was built.
	/// </remarks>
	public IReadOnlyList<CommConversation> Conversations
	{
		get
		{
			var viewer = Viewer();
			return _conversations
				.Select(pair =>
				{
					var others = pair.Value.Participants
						.Where(participant => viewer is null || !IsSame(participant, viewer))
						.ToArray();
					return (pair.Value.Recency, Conversation: new CommConversation(pair.Key,
						others.Select(other => other.Name).ToArray(), others.Select(other => other.ObjId).ToArray(),
						UnreadFor(pair.Key), pair.Value.LastAt));
				})
				.OrderByDescending(entry => entry.Recency)
				.Select(entry => entry.Conversation)
				.ToArray();
		}
	}

	/// <inheritdoc/>
	public string? Viewing
	{
		get => _viewing;
		set
		{
			_viewing = value;
			if (value is not null) MarkRead(value);
		}
	}

	public IReadOnlyList<CommMessage> Messages(string key) =>
		_history.TryGetValue(key, out var lines) ? lines.ToArray() : [];

	public void MarkRead(string key)
	{
		if (_unread.Remove(key)) Changed?.Invoke();
		AdvanceMarker(key);
	}

	/// <inheritdoc/>
	/// <remarks>Pulls nothing until the markers have been read for the viewer: until then the feed does not
	/// know that the session's character is the one this feed is for.</remarks>
	public async Task LoadHistoryAsync(string key)
	{
		if (_server is null || _syncedFor is null) return;

		if (IsConversationKey(key))
		{
			await LoadConversationAsync(_server, key);
			return;
		}

		var generation = _generation;
		// Back to where the viewer last read, and at least as many as a channel keeps; with no marker to go back
		// to, everything the recall buffer holds.
		var pulled = _markers.TryGetValue(key, out var marker) && marker.Id is { } seen
			? await _server.RecallAsync(key, HistoryLimit, seen)
			: await _server.RecallAsync(key, 0);
		if (generation != _generation || pulled is not IReadOnlyList<ChannelRecallLine> lines) return;

		_pulled.Add(key);
		if (Merge(key, lines.Select(line => new CommMessage(CommPayloadParser.ChannelKind, key, [], line.From,
				line.FromObjid, line.Text, DateTimeOffset.FromUnixTimeMilliseconds(line.Ts), line.Id)).ToList()))
			Changed?.Invoke();
	}

	/// <summary>
	/// Pulls a conversation's logged pages, and takes from the answer whether the game keeps a page log.
	/// Nothing is asked for a conversation with someone known only by name: the log names people by objid.
	/// </summary>
	private async Task LoadConversationAsync(ICommHistory server, string key)
	{
		// Asked even after an earlier "off": page_log is a live option, and a game may have turned it on since.
		if (_syncedFor is not { } viewer
			|| !_conversations.TryGetValue(key, out var conversation) || OthersIn(conversation, viewer) is not { } others)
			return;

		var generation = _generation;
		// Back to where the viewer last read, and at least as many as a conversation keeps; with no marker to go
		// back to, as many as the server gives.
		var pulled = _markers.TryGetValue(key, out var marker) && marker.Id is { } seen
			? await server.ConversationRecallAsync(others, HistoryLimit, seen)
			: await server.ConversationRecallAsync(others, 0);
		if (generation != _generation) return;
		if (pulled is not PageRecall recall)
		{
			// The next list lists the conversations again and pulls those still behind, this one among them.
			_conversationsListed = false;
			return;
		}

		if (!recall.Logging)
		{
			var turnedOff = _pageLogging is not false;
			_pageLogging = false;
			if (ForgetLoggedHistory() || turnedOff) Changed?.Invoke();
			return;
		}

		// Turning back on is news even when the pull adds no line: the view's logging-off note depends on it.
		var turnedOn = _pageLogging is not true;
		_pageLogging = true;
		var lines = recall.Lines.Select(line => new CommMessage(CommPayloadParser.PageKind, null, line.To, line.From,
			line.FromObjid, line.Text, DateTimeOffset.FromUnixTimeMilliseconds(line.Ts), line.Id)).ToList();
		var held = _history.TryGetValue(key, out var kept) ? kept.Select(line => line.Id).OfType<long>().ToHashSet() : [];
		var fromTheLog = PulledOnly(key);
		fromTheLog.UnionWith(lines.Select(line => line.Id).OfType<long>().Where(id => !held.Contains(id)));
		var merged = Merge(key, lines);
		if (merged || turnedOn) Changed?.Invoke();
	}

	/// <summary>The read of the markers and the pulls that follow it, once started; completed otherwise.</summary>
	public Task Synced => _sync;

	/// <inheritdoc/>
	public bool? PageLogging => _pageLogging;

	public void Dispose()
	{
		_store.ChannelUpdated -= OnChannelUpdated;
		if (_connection is not null) _connection.ConnectionStateChanged -= OnConnectionStateChanged;
	}

	private int UnreadFor(string key) => _unread.GetValueOrDefault(key);

	/// <summary>How many lines a key keeps: <see cref="HistoryLimit"/>, or more when a pull brought back more.</summary>
	private int Kept(string key) => _kept.GetValueOrDefault(key, HistoryLimit);

	private void OnChannelUpdated(string package)
	{
		if (package is not (CommPayloadParser.ChannelsPackage or CommPayloadParser.MessagePackage)) return;

		if (_store.Get(package) is not { } json)
		{
			Clear();
			return;
		}

		var changed = package == CommPayloadParser.ChannelsPackage
			? ReplaceChannels(json)
			: Add(json);
		if (changed) Changed?.Invoke();
	}

	/// <summary>The channels the viewer is on: the ones whose history the feed pulls and counts.</summary>
	private IEnumerable<string> JoinedNames => _channels.Where(channel => channel.Joined).Select(channel => channel.Name);

	private bool ReplaceChannels(string json)
	{
		if (CommPayloadParser.ParseChannels(json) is not { } list) return false;

		// Only a channel the viewer is on has lines to keep; the others are listed for the channel browser.
		var listed = list.Channels.Where(channel => channel.Joined).Select(channel => channel.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		// A channel left is forgotten whole — pulled and marker too — so joining it again pulls it again.
		foreach (var key in _history.Keys.Concat(_unread.Keys).Concat(_pulled).Concat(_markers.Keys)
			.Where(key => !IsConversationKey(key) && !listed.Contains(key))
			.ToArray())
		{
			_history.Remove(key);
			_unread.Remove(key);
			_kept.Remove(key);
			_pulled.Remove(key);
			_markers.Remove(key);
		}

		_serverCounted.Clear();
		_serverCounted.UnionWith(list.ServerUnread.Keys);

		_channels = list.Channels;
		if (list.Viewer is { } viewer && (_viewer is null || !IsSame(_viewer, viewer)))
		{
			_viewer = viewer;
			UncountViewersOwnLines(viewer);
		}
		foreach (var (name, unread) in list.ServerUnread)
		{
			if (unread > 0) _unread[name] = unread;
			else _unread.Remove(name);
		}

		SyncWithServer();
		return true;
	}

	/// <summary>
	/// Reads the viewer's markers once the feed knows who the viewer is, then pulls every channel's history.
	/// With the markers read, a list naming a channel not pulled yet — one just joined, or one renamed, whose
	/// marker the server has moved to the new name — reads the markers again and pulls only those channels.
	/// The first list after the connection dropped pulls every channel, and lists the conversations again,
	/// since what was sent while it was down is not replayed.
	/// </summary>
	private void SyncWithServer()
	{
		if (_server is null || _viewer?.ObjId is not { } viewer) return;

		if (string.Equals(_syncedFor, viewer, StringComparison.Ordinal))
		{
			if (_drops != _resyncedDrops)
			{
				// Every channel counts as unpulled again, and is marked pulled only by a pull that answers, so a
				// pull that fails now is tried again on the next list.
				_pulled.Clear();
				_conversationsListed = false;
				_sync = RefreshAsync(_server, viewer, _generation, JoinedNames.ToArray(),
					_drops);
				return;
			}

			var unpulled = JoinedNames.Where(name => !_pulled.Contains(name)).ToArray();
			if (unpulled.Length > 0 || !_conversationsListed) _sync = RefreshAsync(_server, viewer, _generation, unpulled);
			return;
		}

		if (string.Equals(_syncingFor, viewer, StringComparison.Ordinal)) return;

		// A first read pulls everything, so it covers any drop before it.
		_resyncedDrops = _drops;
		_syncingFor = viewer;
		_syncedFor = null;
		_markers.Clear();
		_pulled.Clear();
		_conversationsListed = false;
		_sync = ReadMarkersAsync(_server, viewer, _generation);
	}

	private async Task ReadMarkersAsync(ICommHistory server, string viewer, int generation)
	{
		var answer = await server.MarkersAsync();
		if (generation != _generation || !string.Equals(_syncingFor, viewer, StringComparison.Ordinal)) return;

		_syncingFor = null;
		if (answer is not CommReadMarkers markers || !string.Equals(markers.Character, viewer, StringComparison.Ordinal)) return;

		_syncedFor = viewer;
		ApplyMarkers(markers, viewer);
		await PullAsync(JoinedNames.ToArray());
		await RebuildConversationsAsync(server, viewer, generation);
		Changed?.Invoke();
	}

	/// <summary>
	/// Reads the markers again for a feed already synced, then pulls <paramref name="channels"/>, and lists the
	/// conversations if an earlier listing failed. A failed read of the markers does neither: the next list
	/// tries again. A refresh after a drop names the <paramref name="drops"/> it covers, which then count as
	/// covered.
	/// </summary>
	private async Task RefreshAsync(ICommHistory server, string viewer, int generation, IReadOnlyList<string> channels,
		int? drops = null)
	{
		var answer = await server.MarkersAsync();
		if (generation != _generation || !string.Equals(_syncedFor, viewer, StringComparison.Ordinal)) return;

		// Without the markers a pull would file the history uncounted and mark the channel done; leave it
		// unpulled, so the next list tries again.
		if (answer is not CommReadMarkers markers || !string.Equals(markers.Character, viewer, StringComparison.Ordinal)) return;

		if (drops is { } covered && covered > _resyncedDrops) _resyncedDrops = covered;

		ApplyMarkers(markers, viewer);
		await PullAsync(channels);
		if (!_conversationsListed) await RebuildConversationsAsync(server, viewer, generation);
		Changed?.Invoke();
	}

	/// <summary>
	/// Takes the server's markers, keeping a marker of this feed's that is further on (a write the server
	/// has not answered yet). Lines pushed while the markers were on their way were counted one by one, so
	/// each key is counted again from its marker — here, for a key the pull after adds nothing to.
	/// </summary>
	private void ApplyMarkers(CommReadMarkers markers, string viewer)
	{
		foreach (var channel in markers.Channels)
		{
			Take(channel.Channel, new Marker(channel.LastReadId, channel.LastReadAt));
			Recount(channel.Channel);
		}

		foreach (var conversation in markers.Conversations)
		{
			var key = ConversationKeyPrefix + string.Join(' ', conversation.With.Append(viewer).Distinct().Order(StringComparer.Ordinal));
			Take(key, new Marker(conversation.LastReadId, conversation.LastReadAt));
			Recount(key);
		}
	}

	/// <summary>Records <paramref name="marker"/> for a key unless the one held is already as far on.</summary>
	private void Take(string key, Marker marker)
	{
		if (!_markers.TryGetValue(key, out var held) || held.IsBefore(marker)) _markers[key] = marker;
	}

	/// <summary>
	/// Lists the viewer's page conversations from the server's page log, so a reload keeps them, and pulls
	/// those whose last page is past the viewer's marker, so their unread counts survive too, and those never
	/// marked, so pages read on another machine are here. One already read to its end is pulled when it is
	/// opened.
	/// </summary>
	private async Task RebuildConversationsAsync(ICommHistory server, string viewer, int generation)
	{
		var answer = await server.ConversationsAsync();
		if (generation != _generation || answer is not PageConversations list
			|| !string.Equals(list.Character, viewer, StringComparison.Ordinal) || _viewer is not { } self)
			return;

		// A listing while page_log is off lists nothing and is not done: the next list asks again, so turning
		// logging on brings the kept conversations back without a reconnect.
		_conversationsListed = list.Logging;
		_pageLogging = list.Logging;
		if (!list.Logging)
		{
			ForgetLoggedHistory();
			return;
		}

		var behind = new List<string>();
		foreach (var summary in list.Conversations)
		{
			var participants = summary.With
				.Select((objid, i) => new CommParticipant(i < summary.Names.Count ? summary.Names[i] : objid, objid))
				.Where(other => other.ObjId != viewer)
				.Prepend(self)
				.ToList();
			var key = ConversationKeyPrefix + string.Join(' ', participants.Select(Identity).Distinct().Order(StringComparer.Ordinal));
			if (_conversations.GetValueOrDefault(key) is { } known)
			{
				_conversations[key] = known.Recency >= summary.LastId
					? known
					: known with { LastAt = summary.LastAt, Recency = summary.LastId };
			}
			else
			{
				_conversations[key] = new Conversation(participants, summary.LastAt, summary.LastId);
				_listedOnly.Add(key);
			}

			if (!_markers.TryGetValue(key, out var marker) || marker.IsBefore(new Marker(summary.LastId, summary.LastAt)))
				behind.Add(key);
		}

		DropLeastRecentConversations();
		foreach (var key in behind.Where(_conversations.ContainsKey))
		{
			await LoadConversationAsync(server, key);
		}
	}

	/// <summary>The ids of a conversation's lines that came from the page log and were never pushed.</summary>
	private HashSet<long> PulledOnly(string key)
	{
		if (!_pulledOnly.TryGetValue(key, out var ids)) _pulledOnly[key] = ids = [];
		return ids;
	}

	/// <summary>
	/// What the page log gave the feed, taken back out now the server says the log is off (it hides the log
	/// then): each pulled line never pushed, and each conversation known only from the listing that has no
	/// line left, unless it is the one being viewed. Pages pushed live stay. The next list asks for the
	/// conversations again, so turning logging
	/// back on brings them back. Answers whether anything went.
	/// </summary>
	private bool ForgetLoggedHistory()
	{
		var changed = false;
		foreach (var (key, ids) in _pulledOnly)
		{
			if (ids.Count > 0 && _history.TryGetValue(key, out var lines)
				&& lines.RemoveAll(line => line.Id is { } id && ids.Contains(id)) > 0)
			{
				changed = true;
				Recount(key);
			}
		}

		_pulledOnly.Clear();
		// The conversation being viewed stays: the view needs it to remain a conversation, to say why it has no
		// history and to page its people.
		foreach (var key in _listedOnly
			.Where(key => !string.Equals(key, _viewing, StringComparison.OrdinalIgnoreCase))
			.Where(key => !_history.TryGetValue(key, out var lines) || lines.Count == 0).ToArray())
		{
			_conversations.Remove(key);
			_history.Remove(key);
			_unread.Remove(key);
			_kept.Remove(key);
			changed = true;
		}

		_listedOnly.Clear();
		_conversationsListed = false;
		return changed;
	}

	private async Task PullAsync(IReadOnlyList<string> channels)
	{
		foreach (var channel in channels)
		{
			await LoadHistoryAsync(channel);
		}
	}

	/// <summary>
	/// Files pulled lines with the key's history: a line whose id is already there is skipped, and the
	/// whole is put back in the order the lines were sent. The key keeps every line from the earliest pulled
	/// on, however many that is. Then the key's unread count is taken again from its marker, if it has one,
	/// and a key being viewed has its marker moved to the new last line.
	/// </summary>
	private bool Merge(string key, IReadOnlyList<CommMessage> pulled)
	{
		if (!_history.TryGetValue(key, out var lines)) _history[key] = lines = [];

		var held = lines.Select(line => line.Id).OfType<long>().ToHashSet();
		var added = pulled.Where(line => line.Id is not { } id || held.Add(id)).ToList();
		if (added.Count == 0) return false;

		lines.AddRange(added);
		// Stable: lines with the same key keep the order they arrived in.
		var ordered = lines.OrderBy(OrderKey).ToList();
		var earliest = pulled.Min(OrderKey);
		var kept = Math.Max(Kept(key), ordered.Count(line => OrderKey(line) >= earliest));
		if (kept > HistoryLimit) _kept[key] = kept;
		lines.Clear();
		lines.AddRange(ordered.Skip(Math.Max(0, ordered.Count - kept)));

		Recount(key);
		if (string.Equals(key, _viewing, StringComparison.OrdinalIgnoreCase)) AdvanceMarker(key);
		return true;
	}

	/// <summary>
	/// Where a line sorts: its id where it has one, since ids keep rising when the clock steps back; else the
	/// microsecond it was sent, the scale ids are taken on, so a line without one falls among them by time.
	/// </summary>
	private static long OrderKey(CommMessage line) =>
		line.Id ?? (line.Timestamp - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond;

	/// <summary>
	/// A key's unread count from its marker: the lines after it from someone else. A key without one keeps
	/// its count, and so does a channel whose count the list carried — the game's count stands, and the
	/// bounded history here could not reach a larger one.
	/// </summary>
	private void Recount(string key)
	{
		if (_serverCounted.Contains(key)
			|| !_markers.TryGetValue(key, out var marker) || !_history.TryGetValue(key, out var lines)) return;

		var viewer = Viewer();
		var unread = string.Equals(key, _viewing, StringComparison.OrdinalIgnoreCase)
			? 0
			: lines.Count(line => marker.IsBefore(line) && !IsFrom(line, viewer));
		if (unread > 0) _unread[key] = unread;
		else _unread.Remove(key);
	}

	/// <summary>
	/// Moves the viewer's marker for a key to its last line on the server, unless it is already there or
	/// further on. Nothing is written for a feed whose markers were not read for its viewer.
	/// </summary>
	/// <remarks>
	/// The feed records the marker only when the server has taken it, and records what the server answers
	/// (which can be further on, moved from another device). A write that failed leaves the marker where
	/// it was, so the next read sends it again rather than finding nothing new to send.
	/// </remarks>
	private void AdvanceMarker(string key)
	{
		if (_server is null || _syncedFor is not { } viewer
			|| !_history.TryGetValue(key, out var lines) || lines.Count == 0)
			return;

		var last = lines[^1];
		var moved = new Marker(last.Id, last.Timestamp);
		if (_markers.TryGetValue(key, out var marker) && !marker.IsBefore(moved)) return;

		if (!IsConversationKey(key))
		{
			_ = WriteChannelMarkerAsync(_server, key, new ReadMarkerUpdate(last.Id, last.Timestamp), _generation);
			return;
		}

		if (!_conversations.TryGetValue(key, out var conversation) || OthersIn(conversation, viewer) is not { } others) return;

		_ = WriteConversationMarkerAsync(_server, key, new ConversationReadMarkerUpdate(others, last.Id, last.Timestamp),
			_generation);
	}

	/// <summary>
	/// The others in a conversation by objid, as the server names a conversation: the viewer alone for pages
	/// to themselves; null when someone in it is known only by name.
	/// </summary>
	private static IReadOnlyList<string>? OthersIn(Conversation conversation, string viewer)
	{
		var others = conversation.Participants.Where(participant => participant.ObjId != viewer).ToArray();
		if (others.Length > CommLimits.ConversationMaxOthers || others.Any(participant => participant.ObjId is null)) return null;

		return others.Length == 0 ? [viewer] : others.Select(participant => participant.ObjId!).ToArray();
	}

	private async Task WriteChannelMarkerAsync(ICommHistory server, string key, ReadMarkerUpdate update, int generation)
	{
		if (await server.MarkChannelAsync(key, update) is ChannelReadMarker stored && generation == _generation)
		{
			Take(key, new Marker(stored.LastReadId, stored.LastReadAt));
		}
	}

	private async Task WriteConversationMarkerAsync(ICommHistory server, string key, ConversationReadMarkerUpdate update,
		int generation)
	{
		if (await server.MarkConversationAsync(update) is ConversationReadMarker stored && generation == _generation)
		{
			Take(key, new Marker(stored.LastReadId, stored.LastReadAt));
		}
	}

	private static bool IsFrom(CommMessage line, CommParticipant? viewer) =>
		viewer is not null && IsSame(new CommParticipant(line.From, line.FromObjId), viewer);

	private bool Add(string json)
	{
		if (CommPayloadParser.ParseMessage(json, _time.GetUtcNow()) is not { } entry) return false;

		var message = entry.Message;
		var viewer = Viewer();

		var key = message.Channel ?? ConversationFor(entry);
		if (message.Channel is null)
		{
			_listedOnly.Remove(key);
			if (message.Id is { } pushed && _pulledOnly.TryGetValue(key, out var fromTheLog)) fromTheLog.Remove(pushed);
		}

		// Already held — pulled from the server, or pushed before and replayed on a resumed connection.
		if (message.Id is { } id && _history.TryGetValue(key, out var held) && held.Any(line => line.Id == id))
			return false;

		if (!_history.TryGetValue(key, out var lines)) _history[key] = lines = [];
		lines.Add(message);
		if (lines.Count > Kept(key)) lines.RemoveRange(0, lines.Count - Kept(key));

		var viewing = string.Equals(key, _viewing, StringComparison.OrdinalIgnoreCase);
		var alreadyRead = _markers.TryGetValue(key, out var marker) && !marker.IsBefore(message);
		if (!IsFrom(message, viewer) && !viewing && !alreadyRead)
			_unread[key] = UnreadFor(key) + 1;
		if (viewing) AdvanceMarker(key);

		if (message.Channel is null) DropLeastRecentConversations();
		return true;
	}

	/// <summary>Files a page under its conversation, updating who it is with and when it last spoke.</summary>
	private string ConversationFor(CommEntry entry)
	{
		var participants = new List<CommParticipant> { new(entry.Message.From, entry.Message.FromObjId) };
		// Where is lazy, so each recipient is checked against the list as it stands, repeats included.
		foreach (var recipient in entry.Recipients.Where(recipient => !participants.Any(known => IsSame(known, recipient))))
		{
			participants.Add(recipient);
		}

		var key = ConversationKeyPrefix + string.Join(' ', participants.Select(Identity).Order(StringComparer.Ordinal));
		// Who it is with comes from this page; when it last spoke, from whichever page is the later by id.
		var recency = OrderKey(entry.Message);
		_conversations[key] = _conversations.GetValueOrDefault(key) is { } known && known.Recency > recency
			? new Conversation(participants, known.LastAt, known.Recency)
			: new Conversation(participants, entry.Message.Timestamp, recency);
		return key;
	}

	private void DropLeastRecentConversations()
	{
		var excess = _conversations.Count - ConversationLimit;
		if (excess <= 0) return;

		// The conversation being viewed is never the one dropped: the view needs it to stay a conversation.
		foreach (var key in _conversations
			.Where(pair => !string.Equals(pair.Key, _viewing, StringComparison.OrdinalIgnoreCase))
			.OrderBy(pair => pair.Value.Recency).Take(excess).Select(pair => pair.Key).ToArray())
		{
			_conversations.Remove(key);
			_history.Remove(key);
			_unread.Remove(key);
			_kept.Remove(key);
			_pulledOnly.Remove(key);
			_listedOnly.Remove(key);
		}
	}

	/// <summary>
	/// Takes the viewer's own lines back out of the unread counts. They arrive before the viewer is known —
	/// on connect the player's own presence line comes ahead of the channel list that says who they are —
	/// and were counted then. A key's unread lines are its last <c>n</c>, so those are the ones recounted.
	/// </summary>
	private void UncountViewersOwnLines(CommParticipant viewer)
	{
		foreach (var (key, unread) in _unread.ToArray())
		{
			if (!_history.TryGetValue(key, out var lines)) continue;

			var others = lines.Skip(Math.Max(0, lines.Count - unread))
				.Count(line => !IsSame(new CommParticipant(line.From, line.FromObjId), viewer));
			if (others > 0) _unread[key] = others;
			else _unread.Remove(key);
		}
	}

	private static bool IsConversationKey(string key) => key.StartsWith(ConversationKeyPrefix, StringComparison.Ordinal);

	private CommParticipant? Viewer() =>
		_viewer
		?? _store.Room.Occupants.FirstOrDefault(occupant => occupant.You) switch
		{
			{ } you => new CommParticipant(you.Name, you.ObjId),
			null => null
		};

	/// <summary>The same person: by objid when both have one, by name (ignoring case) otherwise.</summary>
	private static bool IsSame(CommParticipant left, CommParticipant right) =>
		left.ObjId is not null && right.ObjId is not null
			? string.Equals(left.ObjId, right.ObjId, StringComparison.Ordinal)
			: string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

	private static string Identity(CommParticipant participant) =>
		participant.ObjId ?? $"name:{participant.Name.ToLowerInvariant()}";

	private void Clear()
	{
		if (_channels.Count == 0 && _history.Count == 0 && _unread.Count == 0 && _conversations.Count == 0
			&& _viewer is null && _viewing is null && _markers.Count == 0 && _syncedFor is null && _syncingFor is null
			&& _pageLogging is null)
			return;

		_generation++;
		_markers.Clear();
		_pulled.Clear();
		_serverCounted.Clear();
		_syncedFor = null;
		_syncingFor = null;
		_sync = Task.CompletedTask;
		_pageLogging = null;
		_conversationsListed = false;
		_pulledOnly.Clear();
		_listedOnly.Clear();

		_channels = [];
		_viewer = null;
		_viewing = null;
		_history.Clear();
		_unread.Clear();
		_kept.Clear();
		_conversations.Clear();
		Changed?.Invoke();
	}

	/// <summary>Where a key was read up to: its last line's id where lines have ids, and its time.</summary>
	private sealed record Marker(long? Id, DateTimeOffset At)
	{
		/// <summary>Whether <paramref name="line"/> came after this: by id when both have one, by time otherwise.</summary>
		public bool IsBefore(CommMessage line) => IsBefore(line.Id, line.Timestamp);

		public bool IsBefore(Marker other) => IsBefore(other.Id, other.At);

		private bool IsBefore(long? id, DateTimeOffset at) =>
			Id is { } mine && id is { } theirs ? theirs > mine : at > At;
	}

	/// <summary>
	/// A conversation's participants as its latest page named them, the viewer included, and its latest
	/// page: when it was sent, and its <see cref="OrderKey"/> (the id, which keeps rising when the clock steps
	/// back), by which conversations are ordered and the least recent dropped.
	/// </summary>
	private sealed record Conversation(IReadOnlyList<CommParticipant> Participants, DateTimeOffset LastAt, long Recency);
}
