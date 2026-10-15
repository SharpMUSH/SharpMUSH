using System.Collections.Concurrent;

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Push;

/// <summary>
/// Which portal tabs are in view, per character. Each tab's hub connection reports when its page is shown or
/// hidden; <c>@push</c> leaves a player alone while one of their tabs is in view.
/// </summary>
public interface IPortalPresence
{
	/// <summary>Records that a hub connection for <paramref name="character"/> is (or is not) in view.</summary>
	void Report(string connectionId, DBRef character, bool visible);

	/// <summary>Forgets a hub connection that closed.</summary>
	void Remove(string connectionId);

	/// <summary>Whether any portal tab playing <paramref name="character"/> is in view. Compared by dbref number.</summary>
	bool IsInView(DBRef character);
}

/// <inheritdoc />
public sealed class PortalPresence : IPortalPresence
{
	private readonly ConcurrentDictionary<string, (int Character, bool Visible)> _connections = new();

	/// <inheritdoc />
	public void Report(string connectionId, DBRef character, bool visible)
		=> _connections[connectionId] = (character.Number, visible);

	/// <inheritdoc />
	public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

	/// <inheritdoc />
	public bool IsInView(DBRef character)
		=> _connections.Values.Any(connection => connection.Visible && connection.Character == character.Number);
}
