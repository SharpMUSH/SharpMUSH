using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.OAuth;

/// <summary>
/// An app acting for a player: its client id and name, the character it acts as and the scopes the player allowed it.
/// <see cref="Current"/> is the app that sent the command running on this flow. While it is set, the character holds
/// only the permissions <see cref="Scopes"/> cover (<see cref="DelegatedScopes.Covers"/>), and a command restricted with
/// <c>noapp</c> refuses the character. The queue carries it to the work the command queues.
/// </summary>
public sealed record DelegatedApp(string ClientId, string Name, DBRef Character, IReadOnlyList<string> Scopes)
{
	private static readonly AsyncLocal<DelegatedApp?> Ambient = new();

	/// <summary>The app whose command is running on this flow, or null when a player typed it or softcode ran it.</summary>
	public static DelegatedApp? Current => Ambient.Value;

	/// <summary>Whether <paramref name="obj"/> is the character the app acts as.</summary>
	public bool ActsAs(DBRef obj) => obj.Number == Character.Number;

	/// <summary>The app acting as <paramref name="obj"/> on this flow, or null.</summary>
	public static DelegatedApp? ActingAs(DBRef obj) => Current is { } app && app.ActsAs(obj) ? app : null;

	/// <summary>Until disposed, what runs on this flow was sent by <paramref name="app"/>.</summary>
	public static IDisposable Begin(DelegatedApp? app)
	{
		var previous = Ambient.Value;
		Ambient.Value = app;
		return new Restore(previous);
	}

	private sealed class Restore(DelegatedApp? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
