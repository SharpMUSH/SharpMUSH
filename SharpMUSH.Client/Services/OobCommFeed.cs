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
/// and pulls each channel's recall buffer (and a channel's again on <see cref="LoadHistoryAsync"/>). A
/// line with an id is kept once however it arrived, pulled, pushed, or replayed on a resumed connection.
/// A key with a marker counts as unread only what came after it from someone else — so the count survives
/// a reload and a change of device — and a key without one counts lines as they arrive, as before.
/// <see cref="MarkRead"/>, and a line arriving for <see cref="Viewing"/>, move the server's marker to the
/// key's last line. The markers are the session's acting character's: a feed whose viewer is someone else
/// (the server says whose they are) uses none and writes none. A page has no id and no server history,
/// so a conversation's marker is the time of its last page read, keyed by the others in it by objid; one
/// with someone known only by name is not marked.</para>
/// <para><b>Clearing.</b> The store raises <see cref="IOobChannelStore.ChannelUpdated"/> for each package
/// it drops, with nothing left to read (a new connection, or a character switch through
/// <see cref="OobChannelStoreProxy"/>), and the feed drops everything with it, <see cref="Viewing"/>
/// included, and raises <see cref="Changed"/> once.</para>
/// <para>It assumes, as the store does, a single-threaded dispatcher (Blazor WASM).</para>
/// </remarks>
public sealed class OobCommFeed : ICommFeed, IDisposable
{
	/// <summary>How many lines are kept per channel or conversation; older ones are dropped.</summary>
	public const int HistoryLimit = 200;

	/// <summary>How many page conversations are kept; the least recent are dropped, history and all.</summary>
	public const int ConversationLimit = 100;

	private const string ConversationKeyPrefix = "page ";

	private readonly IOobChannelStore _store;
	private readonly TimeProvider _time;

	private readonly ICommHistory? _server;

	private readonly Dictionary<string, List<CommMessage>> _history = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, int> _unread = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);
	private IReadOnlyList<CommChannel> _channels = [];
	private CommParticipant? _viewer;
	private string? _viewing;

	/// <summary>Where the viewer has read up to, by key, as the server has it plus what this feed has since moved.</summary>
	private readonly Dictionary<string, Marker> _markers = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The channels pulled since the markers were read.</summary>
	private readonly HashSet<string> _pulled = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The objid the markers were read for, or null while there are none to use.</summary>
	private string? _syncedFor;

	/// <summary>The objid a read of the markers was started for.</summary>
	private string? _syncingFor;

	/// <summary>Moves on with every clear, so an answer to a request made before it is dropped.</summary>
	private int _generation;

	private Task _sync = Task.CompletedTask;

	public OobCommFeed(IOobChannelStore store, TimeProvider? time = null, ICommHistory? history = null)
	{
		_store = store;
		_time = time ?? TimeProvider.System;
		_server = history;
		_store.ChannelUpdated += OnChannelUpdated;
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
					return new CommConversation(pair.Key, others.Select(other => other.Name).ToArray(),
						others.Select(other => other.ObjId).ToArray(), UnreadFor(pair.Key), pair.Value.LastAt);
				})
				.OrderByDescending(conversation => conversation.LastAt)
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
		if (_server is null || _syncedFor is null || IsConversationKey(key)) return;

		var generation = _generation;
		var pulled = await _server.RecallAsync(key);
		if (generation != _generation || pulled is not IReadOnlyList<ChannelRecallLine> lines) return;

		_pulled.Add(key);
		if (Merge(key, lines)) Changed?.Invoke();
	}

	/// <summary>The read of the markers and the pulls that follow it, once started; completed otherwise.</summary>
	public Task Synced => _sync;

	public void Dispose() => _store.ChannelUpdated -= OnChannelUpdated;

	private int UnreadFor(string key) => _unread.GetValueOrDefault(key);

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

	private bool ReplaceChannels(string json)
	{
		if (CommPayloadParser.ParseChannels(json) is not { } list) return false;

		var listed = list.Channels.Select(channel => channel.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var key in _history.Keys.Concat(_unread.Keys).Where(key => !IsConversationKey(key) && !listed.Contains(key))
			.ToArray())
		{
			_history.Remove(key);
			_unread.Remove(key);
		}

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
	/// Reads the viewer's markers once the feed knows who the viewer is, then pulls every channel's history;
	/// with the markers read, pulls only the channels not pulled yet (one just joined).
	/// </summary>
	private void SyncWithServer()
	{
		if (_server is null || _viewer?.ObjId is not { } viewer) return;

		if (string.Equals(_syncedFor, viewer, StringComparison.Ordinal))
		{
			var unpulled = _channels.Select(channel => channel.Name).Where(name => !_pulled.Contains(name)).ToArray();
			if (unpulled.Length > 0) _sync = PullAsync(unpulled);
			return;
		}

		if (string.Equals(_syncingFor, viewer, StringComparison.Ordinal)) return;

		_syncingFor = viewer;
		_syncedFor = null;
		_markers.Clear();
		_pulled.Clear();
		_sync = ReadMarkersAsync(_server, viewer, _generation);
	}

	private async Task ReadMarkersAsync(ICommHistory server, string viewer, int generation)
	{
		var answer = await server.MarkersAsync();
		if (generation != _generation || !string.Equals(_syncingFor, viewer, StringComparison.Ordinal)) return;

		_syncingFor = null;
		if (answer is not CommReadMarkers markers || !string.Equals(markers.Character, viewer, StringComparison.Ordinal)) return;

		_syncedFor = viewer;
		// Lines pushed while the markers were on their way were counted one by one; count them again from
		// the marker, here for a key the pull below adds nothing to.
		foreach (var channel in markers.Channels)
		{
			_markers[channel.Channel] = new Marker(channel.LastReadId, channel.LastReadAt);
			Recount(channel.Channel);
		}

		foreach (var conversation in markers.Conversations)
		{
			var key = ConversationKeyPrefix + string.Join(' ', conversation.With.Append(viewer).Distinct().Order(StringComparer.Ordinal));
			_markers[key] = new Marker(conversation.LastReadId, conversation.LastReadAt);
			Recount(key);
		}

		await PullAsync(_channels.Select(channel => channel.Name).ToArray());
		Changed?.Invoke();
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
	/// whole is put back in the order the lines were sent. Then the key's unread count is taken again from
	/// its marker, if it has one, and a key being viewed has its marker moved to the new last line.
	/// </summary>
	private bool Merge(string key, IReadOnlyList<ChannelRecallLine> pulled)
	{
		if (!_history.TryGetValue(key, out var lines)) _history[key] = lines = [];

		var held = lines.Select(line => line.Id).OfType<long>().ToHashSet();
		var added = pulled
			.Where(line => held.Add(line.Id))
			.Select(line => new CommMessage(CommPayloadParser.ChannelKind, key, [], line.From, line.FromObjid, line.Text,
				DateTimeOffset.FromUnixTimeMilliseconds(line.Ts), line.Id))
			.ToList();
		if (added.Count == 0) return false;

		lines.AddRange(added);
		// Stable: lines sent in the same millisecond keep the order of their ids, and those without one
		// keep the order they arrived in.
		var ordered = lines.OrderBy(line => line.Timestamp).ThenBy(line => line.Id ?? long.MaxValue).ToList();
		lines.Clear();
		lines.AddRange(ordered.Skip(Math.Max(0, ordered.Count - HistoryLimit)));

		Recount(key);
		if (string.Equals(key, _viewing, StringComparison.OrdinalIgnoreCase)) AdvanceMarker(key);
		return true;
	}

	/// <summary>A key's unread count from its marker: the lines after it from someone else. A key without one keeps its count.</summary>
	private void Recount(string key)
	{
		if (!_markers.TryGetValue(key, out var marker) || !_history.TryGetValue(key, out var lines)) return;

		var viewer = Viewer();
		var unread = string.Equals(key, _viewing, StringComparison.OrdinalIgnoreCase)
			? 0
			: lines.Count(line => marker.IsBefore(line) && !IsFrom(line, viewer));
		if (unread > 0) _unread[key] = unread;
		else _unread.Remove(key);
	}

	/// <summary>
	/// Moves the viewer's marker for a key to its last line, here and on the server, unless it is already
	/// there or further on. Nothing is written for a feed whose markers were not read for its viewer.
	/// </summary>
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
			_markers[key] = moved;
			_ = _server.MarkChannelAsync(key, new ReadMarkerUpdate(last.Id, last.Timestamp));
			return;
		}

		if (!_conversations.TryGetValue(key, out var conversation)) return;

		var others = conversation.Participants.Where(participant => participant.ObjId != viewer).ToArray();
		if (others.Length == 0 || others.Any(participant => participant.ObjId is null)) return;

		_markers[key] = moved with { Id = null };
		_ = _server.MarkConversationAsync(new ConversationReadMarkerUpdate(
			others.Select(participant => participant.ObjId!).ToArray(), null, last.Timestamp));
	}

	private static bool IsFrom(CommMessage line, CommParticipant? viewer) =>
		viewer is not null && IsSame(new CommParticipant(line.From, line.FromObjId), viewer);

	private bool Add(string json)
	{
		if (CommPayloadParser.ParseMessage(json, _time.GetUtcNow()) is not { } entry) return false;

		var message = entry.Message;
		var viewer = Viewer();

		// Already held — pulled from the server, or pushed before and replayed on a resumed connection.
		if (message.Id is { } id && message.Channel is { } channel
			&& _history.TryGetValue(channel, out var held) && held.Any(line => line.Id == id))
			return false;

		var key = message.Channel ?? ConversationFor(entry);

		if (!_history.TryGetValue(key, out var lines)) _history[key] = lines = [];
		lines.Add(message);
		if (lines.Count > HistoryLimit) lines.RemoveRange(0, lines.Count - HistoryLimit);

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
		var lastAt = _conversations.TryGetValue(key, out var known) && known.LastAt > entry.Message.Timestamp
			? known.LastAt
			: entry.Message.Timestamp;

		_conversations[key] = new Conversation(participants, lastAt);
		return key;
	}

	private void DropLeastRecentConversations()
	{
		var excess = _conversations.Count - ConversationLimit;
		if (excess <= 0) return;

		foreach (var key in _conversations.OrderBy(pair => pair.Value.LastAt).Take(excess).Select(pair => pair.Key).ToArray())
		{
			_conversations.Remove(key);
			_history.Remove(key);
			_unread.Remove(key);
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
			&& _viewer is null && _viewing is null && _markers.Count == 0 && _syncedFor is null && _syncingFor is null)
			return;

		_generation++;
		_markers.Clear();
		_pulled.Clear();
		_syncedFor = null;
		_syncingFor = null;
		_sync = Task.CompletedTask;

		_channels = [];
		_viewer = null;
		_viewing = null;
		_history.Clear();
		_unread.Clear();
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

	/// <summary>A conversation's participants as its latest page named them, the viewer included.</summary>
	private sealed record Conversation(IReadOnlyList<CommParticipant> Participants, DateTimeOffset LastAt);
}
