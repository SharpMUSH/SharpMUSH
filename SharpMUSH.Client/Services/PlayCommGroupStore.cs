using System.Text.Json;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Channel groups kept with the account's Play settings, under <see cref="PlayPreferences.CommGroups"/>: on the
/// account when someone is signed in, else in this browser.
/// </summary>
public sealed class PlayCommGroupStore : ICommGroupStore, IDisposable
{
	private readonly PlaySettings _settings;

	/// <summary>A write of this store's own is under way: the settings' change for it is not news from elsewhere.</summary>
	private bool _writing;

	public PlayCommGroupStore(PlaySettings settings)
	{
		_settings = settings;
		_settings.Changed += OnSettingChanged;
	}

	public async Task<Found<string>> ReadAsync()
	{
		await _settings.LoadAsync();
		return _settings.Get<JsonElement?>(PlayPreferences.CommGroups, null) is { ValueKind: JsonValueKind.Array } groups
			? groups.GetRawText()
			: new NotFound();
	}

	public async Task<bool> WriteAsync(string json)
	{
		using var document = JsonDocument.Parse(json);
		_writing = true;
		try
		{
			return await _settings.SetAsync(PlayPreferences.CommGroups, document.RootElement.Clone());
		}
		finally
		{
			_writing = false;
		}
	}

	public event Action? Changed;

	private void OnSettingChanged(string key)
	{
		if (!_writing && key == PlayPreferences.CommGroups) Changed?.Invoke();
	}

	public void Dispose() => _settings.Changed -= OnSettingChanged;
}
