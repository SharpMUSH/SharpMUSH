using Microsoft.JSInterop;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side service that tracks the character currently active in this
/// browser tab/circuit.  Follows the same localStorage-persistence + event
/// pattern as <see cref="ThemeService"/>.
/// </summary>
public sealed class CharacterStateService : ICharacterStateService
{
	private const string LocalStorageKey = "sharpmush_last_character";

	private sealed record PersistedCharacter(string Dbref, string Name);

	private readonly IJSRuntime _js;

	private string? _currentCharacterDbref;
	private string? _currentCharacterName;
	private string? _currentRoomDbref;

	public event Action? OnCharacterChanged;
	public event Action? OnRoomChanged;

	public CharacterStateService(IJSRuntime js) => _js = js;

	/// <inheritdoc/>
	public string? CurrentCharacterDbref => _currentCharacterDbref;

	/// <inheritdoc/>
	public string? CurrentCharacterName => _currentCharacterName;

	/// <inheritdoc/>
	public string? CurrentRoomDbref => _currentRoomDbref;

	/// <inheritdoc/>
	public async Task SetCharacterAsync(string dbref, string name)
	{
		_currentCharacterDbref = dbref;
		_currentCharacterName = name;

		// Persist as "dbref|name" for simplicity (no JSON dependency in client services)
		await _js.SetItemAsync(BrowserStore.Local, LocalStorageKey, $"{dbref}|{name}");

		OnCharacterChanged?.Invoke();
	}

	/// <inheritdoc/>
	public Task SetRoomAsync(string roomDbref)
	{
		_currentRoomDbref = roomDbref;
		OnRoomChanged?.Invoke();
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public async Task InitializeAsync()
	{
		var stored = await _js.GetItemAsync(BrowserStore.Local, LocalStorageKey);
		if (stored?.IndexOf('|') is int sep and > 0)
		{
			_currentCharacterDbref = stored[..sep];
			_currentCharacterName = stored[(sep + 1)..];
		}
	}
}
