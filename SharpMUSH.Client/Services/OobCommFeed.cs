using SharpMUSH.Client.Models;

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

	private readonly Dictionary<string, Queue<CommMessage>> _history = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, int> _unread = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);
	private IReadOnlyList<CommChannel> _channels = [];
	private CommParticipant? _viewer;
	private string? _viewing;

	public OobCommFeed(IOobChannelStore store, TimeProvider? time = null)
	{
		_store = store;
		_time = time ?? TimeProvider.System;
		_store.ChannelUpdated += OnChannelUpdated;
	}

	public event Action? Changed;

	public IReadOnlyList<CommChannel> Channels =>
		_channels.Select(channel => channel with { Unread = UnreadFor(channel.Name) }).ToArray();

	public IReadOnlyList<CommConversation> Conversations =>
		_conversations
			.Select(pair => new CommConversation(
				pair.Key, pair.Value.With, pair.Value.WithObjIds, UnreadFor(pair.Key), pair.Value.LastAt))
			.OrderByDescending(conversation => conversation.LastAt)
			.ToArray();

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
	}

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
		_viewer = list.Viewer ?? _viewer;
		foreach (var (name, unread) in list.ServerUnread)
		{
			if (unread > 0) _unread[name] = unread;
			else _unread.Remove(name);
		}

		return true;
	}

	private bool Add(string json)
	{
		if (CommPayloadParser.ParseMessage(json, _time.GetUtcNow()) is not { } entry) return false;

		var message = entry.Message;
		var viewer = Viewer();
		var key = message.Channel ?? ConversationFor(entry, viewer);

		if (!_history.TryGetValue(key, out var lines)) _history[key] = lines = new Queue<CommMessage>();
		lines.Enqueue(message);
		while (lines.Count > HistoryLimit) lines.Dequeue();

		var fromViewer = viewer is not null && IsSame(new CommParticipant(message.From, message.FromObjId), viewer);
		if (!fromViewer && !string.Equals(key, _viewing, StringComparison.OrdinalIgnoreCase))
			_unread[key] = UnreadFor(key) + 1;

		if (message.Channel is null) DropLeastRecentConversations();
		return true;
	}

	/// <summary>Files a page under its conversation, updating who it is with and when it last spoke.</summary>
	private string ConversationFor(CommEntry entry, CommParticipant? viewer)
	{
		var participants = new List<CommParticipant> { new(entry.Message.From, entry.Message.FromObjId) };
		foreach (var recipient in entry.Recipients)
		{
			if (!participants.Any(known => IsSame(known, recipient))) participants.Add(recipient);
		}

		var key = ConversationKeyPrefix + string.Join(' ', participants.Select(Identity).Order(StringComparer.Ordinal));
		var others = participants.Where(participant => viewer is null || !IsSame(participant, viewer)).ToArray();
		var lastAt = _conversations.TryGetValue(key, out var known) && known.LastAt > entry.Message.Timestamp
			? known.LastAt
			: entry.Message.Timestamp;

		_conversations[key] = new Conversation(
			others.Select(other => other.Name).ToArray(),
			others.Select(other => other.ObjId).ToArray(),
			lastAt);
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
			&& _viewer is null && _viewing is null)
			return;

		_channels = [];
		_viewer = null;
		_viewing = null;
		_history.Clear();
		_unread.Clear();
		_conversations.Clear();
		Changed?.Invoke();
	}

	private sealed record Conversation(IReadOnlyList<string> With, IReadOnlyList<string?> WithObjIds, DateTimeOffset LastAt);
}
