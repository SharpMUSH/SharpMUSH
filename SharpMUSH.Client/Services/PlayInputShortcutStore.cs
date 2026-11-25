using SharpMUSH.Client.Models.PlayInput;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The player's Play input shortcuts (<see cref="InputShortcuts"/>), kept with Play's other settings that follow the
/// account (<see cref="PlaySettings"/>) as three values: the keys, the channel leads and the escape character. A value
/// that is missing, unreadable or breaks a rule is read as the default, so nothing stored can stop the input sending.
/// </summary>
public sealed class PlayInputShortcutStore
{
	private readonly PlaySettings _settings;

	public PlayInputShortcutStore(PlaySettings settings)
	{
		_settings = settings;
		_settings.Changed += OnSettingChanged;
	}

	public InputShortcuts Current { get; private set; } = InputShortcuts.None;

	/// <summary>Raised after <see cref="Current"/> changes, here or on another computer.</summary>
	public event Action? Changed;

	/// <summary>Reads Play's settings, unless they were already read for the account signed in now.</summary>
	public async Task LoadAsync()
	{
		await _settings.LoadAsync();
		Read();
	}

	/// <summary>
	/// Keeps <paramref name="shortcuts"/> as <see cref="InputShortcuts.Sanitize"/> reads them back. False when the
	/// account or the browser refused any of them: they still hold until the settings are next read.
	/// </summary>
	public async Task<bool> SaveAsync(InputShortcuts shortcuts)
	{
		await _settings.LoadAsync();
		var kept = InputShortcuts.Sanitize(shortcuts.Keys, shortcuts.Leads, shortcuts.Escape.ToString());
		var took = await _settings.SetAsync(PlayPreferences.Shortcuts, kept.Keys)
			& await _settings.SetAsync(PlayPreferences.ChannelLeads, kept.Leads)
			& await _settings.SetAsync(PlayPreferences.Escape, kept.Escape.ToString());
		Read();
		return took;
	}

	private void OnSettingChanged(string key)
	{
		if (key is PlayPreferences.Shortcuts or PlayPreferences.ChannelLeads or PlayPreferences.Escape) Read();
	}

	private void Read()
	{
		var read = InputShortcuts.Sanitize(
			_settings.Get<List<KeyShortcut>?>(PlayPreferences.Shortcuts, null),
			_settings.Get<List<ChannelLead>?>(PlayPreferences.ChannelLeads, null),
			_settings.Get<string?>(PlayPreferences.Escape, null));
		if (Same(read, Current)) return;
		Current = read;
		Changed?.Invoke();
	}

	private static bool Same(InputShortcuts a, InputShortcuts b)
		=> a.Escape == b.Escape && a.Keys.SequenceEqual(b.Keys) && a.Leads.SequenceEqual(b.Leads);
}
