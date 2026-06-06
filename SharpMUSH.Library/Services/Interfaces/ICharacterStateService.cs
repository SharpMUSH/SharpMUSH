namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Tracks the character currently active in this browser tab (circuit).
/// Persists the last-used character dbref to localStorage so it can be
/// restored after a page reload.
/// </summary>
public interface ICharacterStateService
{
	/// <summary>The dbref string of the active character, e.g. "#5".  Null when no character is selected.</summary>
	string? CurrentCharacterDbref { get; }

	/// <summary>Display name of the active character.  Null when no character is selected.</summary>
	string? CurrentCharacterName { get; }

	/// <summary>Dbref of the room the active character is currently in.  Null when unknown.</summary>
	string? CurrentRoomDbref { get; }

	/// <summary>Raised whenever <see cref="CurrentCharacterDbref"/> or <see cref="CurrentCharacterName"/> changes.</summary>
	event Action? OnCharacterChanged;

	/// <summary>Raised whenever <see cref="CurrentRoomDbref"/> changes.</summary>
	event Action? OnRoomChanged;

	/// <summary>
	/// Sets the active character and persists the selection to localStorage.
	/// </summary>
	Task SetCharacterAsync(string dbref, string name);

	/// <summary>
	/// Updates the current room for the active character.
	/// </summary>
	Task SetRoomAsync(string roomDbref);

	/// <summary>
	/// Restores a previously-persisted character selection from localStorage.
	/// Falls back silently if nothing was stored or localStorage is unavailable.
	/// </summary>
	Task InitializeAsync();
}
