using SharpMUSH.Client.Models;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Who is on a channel now, for the channel view's member list: <c>@channel/who</c>'s list for the session's
/// character, kept current while the view is open.
/// </summary>
public interface IChannelWho
{
	/// <summary>The channel's members by name, or null while the list has not been read.</summary>
	IReadOnlyList<CommParticipant>? Members(string channel);

	/// <summary>Reads the channel's list from the server and keeps it current until <see cref="Unwatch"/>.</summary>
	Task WatchAsync(string channel);

	/// <summary>Stops keeping <paramref name="channel"/>'s list, if it is the one watched.</summary>
	void Unwatch(string channel);

	event Action? Changed;
}

/// <summary>
/// <see cref="IChannelWho"/> over <c>api/comm/channels/{channel}/who</c> and the <c>comm-feed</c> package's
/// <c>comm.who</c> pushes, read off the play terminal's OOB store as <see cref="OobCommFeed"/> reads its own.
/// </summary>
/// <remarks>
/// <para>The list is read when a channel is watched, and each <c>comm.who</c> for it then adds or removes one
/// member. A push that arrives while the read is under way is applied to its answer, so a member who came or
/// went in between is not lost. Nothing sent while the connection was down is replayed, so every
/// <c>comm.channels</c> (sent on connect and on resume) reads the watched list again.</para>
/// <para>The store raises <see cref="IOobChannelStore.ChannelUpdated"/> with nothing to read when it is cleared
/// (a new connection, a character switch), and the lists go with it.</para>
/// <para>It assumes, as the store does, a single-threaded dispatcher (Blazor WASM).</para>
/// </remarks>
public sealed class ChannelWhoFeed : IChannelWho, IDisposable
{
	private readonly IOobChannelStore _store;
	private readonly ICommHistory? _server;

	private string? _watching;
	private List<CommParticipant>? _members;

	/// <summary>The pushes for the watched channel that arrived while its list was being read.</summary>
	private List<CommWhoChange>? _pending;

	/// <summary>Moves on with every read and every clear, so a read answered after either is dropped.</summary>
	private int _generation;

	public ChannelWhoFeed(IOobChannelStore store, ICommHistory? server = null)
	{
		_store = store;
		_server = server;
		_store.ChannelUpdated += OnChannelUpdated;
	}

	public event Action? Changed;

	public IReadOnlyList<CommParticipant>? Members(string channel) =>
		IsWatched(channel) ? _members : null;

	public async Task WatchAsync(string channel)
	{
		if (!IsWatched(channel))
		{
			_watching = channel;
			_members = null;
			Changed?.Invoke();
		}

		await ReadAsync();
	}

	public void Unwatch(string channel)
	{
		if (!IsWatched(channel)) return;

		_generation++;
		_watching = null;
		_members = null;
		_pending = null;
	}

	public void Dispose() => _store.ChannelUpdated -= OnChannelUpdated;

	private bool IsWatched(string channel) =>
		_watching is not null && string.Equals(_watching, channel, StringComparison.OrdinalIgnoreCase);

	private async Task ReadAsync()
	{
		if (_server is null || _watching is not { } channel) return;

		var generation = ++_generation;
		_pending = [];
		var read = await _server.WhoAsync(channel);
		if (generation != _generation) return;

		var pending = _pending;
		_pending = null;
		if (read is not ChannelWhoList list) return;

		_members = list.Members.Select(member => new CommParticipant(member.Name, member.Objid)).ToList();
		foreach (var change in pending) Apply(_members, change);
		Sort(_members);
		Changed?.Invoke();
	}

	private void OnChannelUpdated(string package)
	{
		if (package is not (CommPayloadParser.WhoPackage or CommPayloadParser.ChannelsPackage)) return;

		if (_store.Get(package) is not { } json)
		{
			Clear();
			return;
		}

		if (package == CommPayloadParser.ChannelsPackage)
		{
			_ = ReadAsync();
			return;
		}

		if (CommPayloadParser.ParseWho(json) is not { } change || !IsWatched(change.Channel)) return;

		if (_pending is not null) _pending.Add(change);
		if (_members is null) return;

		Apply(_members, change);
		Sort(_members);
		Changed?.Invoke();
	}

	private static void Apply(List<CommParticipant> members, CommWhoChange change)
	{
		members.RemoveAll(member => string.Equals(member.ObjId, change.Member.ObjId, StringComparison.Ordinal));
		if (change.Online) members.Add(change.Member);
	}

	private static void Sort(List<CommParticipant> members) =>
		members.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

	private void Clear()
	{
		if (_members is null && _pending is null) return;

		_generation++;
		_members = null;
		_pending = null;
		Changed?.Invoke();
	}
}
