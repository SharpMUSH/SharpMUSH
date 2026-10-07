using MarkupString.Ansi;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.RenderingWorker.Services;

/// <summary>
/// Which Kitty images each connection's terminal already holds, so a picture is sent to it once and its
/// placeholders alone after that. Forgetting is harmless — the terminal is sent the picture again and
/// replaces the one it had under that id — so this keeps only so many connections and drops the oldest.
/// </summary>
public sealed class ConnectionPictures
{
	/// <summary>How many connections are remembered at once.</summary>
	public const int MaxConnections = 4096;

	private readonly ConcurrentDictionary<string, Sent> _connections = new(StringComparer.Ordinal);

	/// <summary>The images the terminal on <paramref name="connection"/> holds.</summary>
	internal Sent For(string connection)
	{
		var sent = _connections.GetOrAdd(connection, static _ => new Sent());
		sent.LastUsed = Environment.TickCount64;
		if (_connections.Count > MaxConnections)
		{
			foreach (var (key, _) in _connections.OrderBy(pair => pair.Value.LastUsed).Take(_connections.Count - MaxConnections))
				_connections.TryRemove(key, out _);
		}

		return sent;
	}

	internal sealed class Sent
	{
		private readonly HashSet<uint> _ids = [];
		public long LastUsed;

		public bool Contains(uint id)
		{
			lock (_ids) return _ids.Contains(id);
		}

		public void AddRange(IEnumerable<uint> ids)
		{
			lock (_ids) _ids.UnionWith(ids);
		}
	}
}

/// <summary>
/// The pictures one render of one connection's output draws from. What it marks as transmitted is only
/// recorded for the connection by <see cref="Commit"/>, once the output is the one actually sent: a render
/// thrown away because a picture arrived in the meantime must not leave the terminal thought to hold an
/// image it was never sent.
/// </summary>
internal sealed class RenderPictureSource(TerminalPictureStore store, ConnectionPictures.Sent sent) : ITerminalPictureSource
{
	private readonly HashSet<uint> _transmitted = [];

	/// <summary>The fetches this render found a picture missing for.</summary>
	public List<Task> Pending { get; } = [];

	public bool TryGetPicture(ImageMarkup image, [NotNullWhen(true)] out TerminalPicture? picture)
	{
		if (store.TryGet(image.Source, out picture, out var pending) && picture is not null) return true;
		if (pending is not null) Pending.Add(pending);
		return false;
	}

	public bool MarkTransmitted(uint imageId) => !sent.Contains(imageId) && _transmitted.Add(imageId);

	/// <summary>Records the images this render sent as held by the terminal.</summary>
	public void Commit() => sent.AddRange(_transmitted);
}
