using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Which characters <c>/client</c> keeps connected in this tab, kept in the tab's own storage, so a reload brings
/// each one back: the focused character connects as Play always does, and the others connect in the background,
/// resuming their sessions when the server still holds them.
/// </summary>
/// <remarks>
/// The list is the tab's (<c>sessionStorage</c>): another tab of the same browser keeps characters of its own.
/// </remarks>
public sealed class KeptCharacters(
	CharacterSessionRegistry sessions,
	AccountAuthService accountAuth,
	IJSRuntime js,
	NavigationManager navigation,
	ILogger<KeptCharacters> logger)
{
	public const string StorageKey = "sharpmush.client.characters";

	private Task? _restoring;
	private bool _restored;
	private bool _restoringNow;
	private string? _saved;

	/// <summary>
	/// Connects every character the tab kept that has no session yet, once per page load, and from then on keeps
	/// the list as sessions open and close. Until the account is signed in and its characters are read, nothing is
	/// done and a later call tries again.
	/// </summary>
	public async Task RestoreAsync()
	{
		if (_restored) return;

		_restoring ??= RestoreOnceAsync();
		try
		{
			await _restoring;
		}
		finally
		{
			if (!_restored) _restoring = null;
		}
	}

	private async Task RestoreOnceAsync()
	{
		await accountAuth.InitAsync();
		// Nothing to restore for a visitor; a later sign-in in this tab asks again.
		if (!accountAuth.IsLoggedIn) return;

		var kept = Read(await js.GetItemAsync(BrowserStore.Session, StorageKey));
		// Without the account's characters every kept one would look gone, and saving would forget them.
		if (kept.Count > 0 && accountAuth.Characters.Count == 0 && await accountAuth.GetCharactersAsync() is ApiFailure) return;

		_restored = true;
		_saved = Join(kept);
		sessions.Changed += OnChanged;
		if (kept.Count == 0) return;

		var acting = accountAuth.ActiveCharacter?.Objid;
		// Saved once at the end: a list saved part way would forget the characters not reached yet.
		_restoringNow = true;
		try
		{
			foreach (var objid in kept)
			{
				if (objid == acting || !sessions.CanOpen) continue;
				if (accountAuth.Characters.FirstOrDefault(character => character.Objid == objid) is not { } character) continue;
				if (TerminalIdentity.Of(accountAuth.Username, character) is not { } identity) continue;
				if (sessions.For(identity) is CharacterSession) continue;

				await ConnectAsync(character, identity);
			}
		}
		finally
		{
			_restoringNow = false;
		}

		await SaveAsync();
	}

	private async Task ConnectAsync(AccountAuthService.CharacterSummary character, TerminalIdentity identity)
	{
		var session = sessions.Open();
		try
		{
			if (await accountAuth.GetOttForCharacterAsync(character) is not string ott)
			{
				await sessions.CloseAsync(session);
				return;
			}

			session.Terminal.ConnectedPlayerName = character.Name;
			await session.Terminal.ConnectWithOttAsync(TerminalEndpoint.Resolve(navigation.BaseUri), ott, identity);
		}
		catch (Exception ex)
		{
			// The gateway may be down; the character stays on the rail, disconnected, as a dropped one does.
			logger.LogWarning(ex, "Could not connect a kept character in the background");
		}
	}

	private void OnChanged()
	{
		if (!_restoringNow) _ = SaveAsync();
	}

	private async Task SaveAsync()
	{
		var now = Join(sessions.Sessions.Select(session => session.Identity?.Character).OfType<string>().ToList());
		if (now == _saved) return;

		_saved = now;
		await js.SetItemAsync(BrowserStore.Session, StorageKey, now);
	}

	private static string Join(IReadOnlyList<string> objids) => JsonSerializer.Serialize(objids);

	/// <summary>The stored list; anything unreadable is no list.</summary>
	private static IReadOnlyList<string> Read(string? json)
	{
		if (string.IsNullOrEmpty(json)) return [];
		try
		{
			return JsonSerializer.Deserialize<List<string>>(json) ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}
}
