using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>
/// One language for the portal and the game. The portal's language is the acting character's <c>LOCALE</c>
/// attribute, the one <c>@locale</c> sets: the picker writes it with <c>@locale</c>, and a tab whose character
/// carries a language the portal speaks switches to it. Without a character the language stays in this browser
/// alone (<c>localStorage["locale"]</c>, read by <c>Program.cs</c>).
/// </summary>
/// <remarks>
/// Blazor WASM sets its culture once, at startup, so every change of language is a stored locale and a reload.
/// </remarks>
public sealed class CharacterLanguage : IDisposable
{
	/// <summary>The <c>localStorage</c> key <c>Program.cs</c> reads the culture from.</summary>
	public const string StorageKey = "locale";

	private readonly IAccountAuthState _account;
	private readonly GameCommandService _commands;
	private readonly IJSRuntime _js;
	private readonly NavigationManager _navigation;
	private readonly ILogger<CharacterLanguage> _logger;
	private bool _switching;

	public CharacterLanguage(IAccountAuthState account, GameCommandService commands, IJSRuntime js,
		NavigationManager navigation, ILogger<CharacterLanguage> logger)
	{
		_account = account;
		_commands = commands;
		_js = js;
		_navigation = navigation;
		_logger = logger;
		_account.ActiveCharacterChanged += Follow;
	}

	/// <summary>The portal locale this tab runs in, as <c>Program.cs</c> set it.</summary>
	public static string Current
		=> CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? PortalLocales.Match(name) ?? name : "en";

	/// <summary>
	/// Switches the portal to <paramref name="locale"/> and reloads. With an acting character it is set first with
	/// <c>@locale</c>, so the game speaks it too and another browser finds it. The character's <c>LOCALE</c> is read
	/// back in the same queue entry: a refusal (a restriction, a hook) still answers 200 with its message, and only
	/// the stored value says whether the game took it. If it did not, nothing changes, since the next load would take
	/// the character's old language back. False when the language did not change.
	/// </summary>
	public async Task<bool> ChooseAsync(string locale)
	{
		if (locale == Current) return false;

		if (_account.ActiveCharacter is { } character)
		{
			var stored = await _commands.RunAsync($"@locale {locale}", "get(%#/LOCALE)") switch
			{
				PortalCommandResponse response => PortalLocales.Match(response.Result) == locale ? null : string.Join(" ", response.Output),
				ApiFailure failure => failure.Message,
			};
			if (stored is not null)
			{
				_logger.LogWarning("Could not set {Character}'s locale to {Locale}: {Reason}", character.Name, locale, stored);
				return false;
			}
		}

		return await SwitchAsync(locale);
	}

	/// <summary>Follows the acting character's <c>LOCALE</c> when the portal speaks it and this tab does not yet.</summary>
	private void Follow() => _ = FollowAsync();

	private async Task FollowAsync()
	{
		if (PortalLocales.Match(_account.ActiveCharacter?.Locale) is not { } locale || locale == Current) return;

		_logger.LogInformation("Switching to {Character}'s language {Locale}", _account.ActiveCharacter!.Name, locale);
		await SwitchAsync(locale);
	}

	/// <summary>
	/// Stores <paramref name="locale"/> and reloads. A locale that could not be stored would be lost to the reload,
	/// so the tab stays as it is instead.
	/// </summary>
	private async Task<bool> SwitchAsync(string locale)
	{
		if (_switching) return false;

		if (!await _js.SetItemAsync(BrowserStore.Local, StorageKey, locale))
		{
			_logger.LogWarning("Could not store the locale {Locale}; keeping {Current}", locale, Current);
			return false;
		}

		_switching = true;
		_navigation.NavigateTo(_navigation.Uri, forceLoad: true);
		return true;
	}

	public void Dispose() => _account.ActiveCharacterChanged -= Follow;
}
