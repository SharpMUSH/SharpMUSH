using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The Play page's settings that follow the player (<see cref="PlayPreferences"/>): kept on the account through
/// <c>api/preferences/portal.play</c>, so every computer and every character on it gets the same ones. Without a
/// signed-in account they are kept in this browser instead (<c>play.pref.&lt;key&gt;</c>).
/// </summary>
/// <remarks>
/// <see cref="LoadAsync"/> reads them once per account and again whenever the tab comes back into view, so a
/// change made on another computer shows here. <see cref="Get{T}"/> then answers from memory. A value this
/// browser kept before Play's settings moved to the account is moved there the first time the account has none.
/// </remarks>
public sealed class PlaySettings(PreferencesService preferences, AccountAuthService auth, IJSRuntime js, TimeProvider? time = null)
{
	private readonly TimeProvider _time = time ?? TimeProvider.System;

	/// <summary>The group's id.</summary>
	public static readonly string GroupId = Preferences.GroupId("portal", PlayPreferences.GroupName);

	/// <summary>Where this browser keeps a setting while no account is signed in.</summary>
	private const string LocalPrefix = "play.pref.";

	/// <summary>A reload when the tab comes back is skipped this soon after the last one.</summary>
	private static readonly TimeSpan RefreshAfter = TimeSpan.FromSeconds(5);

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	/// <summary>
	/// What Play kept in this browser before its settings moved to the account: the old key, and the value of the
	/// setting it meant.
	/// </summary>
	private static readonly IReadOnlyList<(string Key, string OldKey, Func<string, JsonElement?> Value)> OldKeys =
	[
		(PlayPreferences.Echo, "play.echo", old => old == "off" ? JsonSerializer.SerializeToElement(false) : null),
		(PlayPreferences.HidePages, "play.hidepages", old => old == "on" ? JsonSerializer.SerializeToElement(true) : null),
		(PlayPreferences.HideChannels, "play.hidechannels", old => old == "on" ? JsonSerializer.SerializeToElement(true) : null)
	];

	private Dictionary<string, JsonElement> _values = new(StringComparer.OrdinalIgnoreCase);
	private string? _loadedFor;
	private DateTimeOffset _loadedAt;
	private Task? _loading;
	private bool _watching;

	/// <summary>A setting changed: written here, or found changed when the settings were read again. Carries its key.</summary>
	public event Action<string>? Changed;

	/// <summary>Whether the settings are kept on the account, rather than in this browser.</summary>
	public bool OnAccount => _loadedFor is { Length: > 0 };

	/// <summary>
	/// Reads the settings, unless they were already read for the account signed in now. Safe to call from every
	/// page that shows Play; callers share one read.
	/// </summary>
	public Task LoadAsync()
	{
		var owner = Owner;
		if (_loadedFor == owner && _loading is { IsCompleted: true }) return Task.CompletedTask;
		if (_loadedFor == owner && _loading is not null) return _loading;
		_loadedFor = owner;
		return _loading = ReadAsync(owner);
	}

	/// <summary>
	/// The value of <paramref name="key"/> (one of <see cref="PlayPreferences"/>' keys), or
	/// <paramref name="fallback"/> before the settings are read or when the value is not a <typeparamref name="T"/>.
	/// </summary>
	public T Get<T>(string key, T fallback)
	{
		if (!_values.TryGetValue(key, out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
		try
		{
			return value.Deserialize<T>(Json) is { } typed ? typed : fallback;
		}
		catch (JsonException)
		{
			return fallback;
		}
	}

	/// <summary>
	/// Sets <paramref name="key"/> to <paramref name="value"/>, on the account or in this browser. False when the
	/// account refused it (the value stays in effect until the settings are next read) or no store took it.
	/// </summary>
	public async Task<bool> SetAsync<T>(string key, T value)
	{
		var element = JsonSerializer.SerializeToElement(value, Json);
		_values[key] = element;
		Changed?.Invoke(key);
		if (!OnAccount) return await js.SetItemAsync(BrowserStore.Local, LocalPrefix + key, element.GetRawText());
		return await preferences.SetAsync(GroupId, new Dictionary<string, JsonElement> { [key] = element }) switch
		{
			PreferenceGroupResponse group => Took(group),
			_ => false
		};
	}

	/// <summary>Returns <paramref name="key"/> to its default.</summary>
	public async Task<bool> ResetAsync(string key)
	{
		if (!OnAccount)
		{
			_values[key] = Default(key);
			Changed?.Invoke(key);
			return await js.RemoveItemAsync(BrowserStore.Local, LocalPrefix + key);
		}

		return await preferences.ResetAsync(GroupId, key) switch
		{
			PreferenceGroupResponse group => Took(group),
			_ => false
		};
	}

	/// <summary>The tab came back into view: read the settings again, so another computer's change shows here.</summary>
	[JSInvokable]
	public Task OnShownAsync()
	{
		if (_time.GetUtcNow() - _loadedAt < RefreshAfter) return Task.CompletedTask;
		_loadedFor = null;
		return LoadAsync();
	}

	/// <summary>The account the settings are read for: its username, or empty when none is signed in.</summary>
	private string Owner => auth.IsLoggedIn ? auth.Username ?? string.Empty : string.Empty;

	private async Task ReadAsync(string owner)
	{
		await WatchAsync();
		var read = owner.Length > 0
			? await ReadAccountAsync()
			: await ReadBrowserAsync();
		_loadedAt = _time.GetUtcNow();
		if (_loadedFor != owner) return;
		Apply(read);
	}

	/// <summary>The account's values, with any this browser kept from before moved onto it first.</summary>
	private async Task<Dictionary<string, JsonElement>> ReadAccountAsync()
	{
		if (await preferences.GroupAsync(GroupId) is not PreferenceGroupResponse group)
		{
			// The server could not say: keep showing what was in effect.
			return new Dictionary<string, JsonElement>(_values, StringComparer.OrdinalIgnoreCase);
		}

		var moving = new Dictionary<string, JsonElement>();
		foreach (var (key, oldKey, value) in OldKeys)
		{
			if (await js.GetItemAsync(BrowserStore.Local, oldKey) is not { } old) continue;
			if (value(old) is { } moved && group.Preferences.FirstOrDefault(p => p.Key == key) is { IsSet: false })
			{
				moving[key] = moved;
			}
		}

		if (moving.Count > 0 && await preferences.SetAsync(GroupId, moving) is PreferenceGroupResponse updated)
		{
			group = updated;
		}

		if (moving.Count == 0 || group.Preferences.Where(p => moving.ContainsKey(p.Key)).All(p => p.IsSet))
		{
			foreach (var (_, oldKey, _) in OldKeys) await js.RemoveItemAsync(BrowserStore.Local, oldKey);
		}

		return Values(group);
	}

	/// <summary>The values this browser keeps, each key's default where it keeps none.</summary>
	private async Task<Dictionary<string, JsonElement>> ReadBrowserAsync()
	{
		var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		foreach (var spec in PlayPreferences.Group.Keys)
		{
			values[spec.Key] = Default(spec.Key);
			if (await js.GetItemAsync(BrowserStore.Local, LocalPrefix + spec.Key) is { } stored && Parse(stored) is { } kept)
			{
				values[spec.Key] = kept;
			}
		}

		foreach (var (key, oldKey, value) in OldKeys)
		{
			if (await js.GetItemAsync(BrowserStore.Local, oldKey) is not { } old) continue;
			if (value(old) is { } moved && await js.GetItemAsync(BrowserStore.Local, LocalPrefix + key) is null)
			{
				values[key] = moved;
				await js.SetItemAsync(BrowserStore.Local, LocalPrefix + key, moved.GetRawText());
			}

			await js.RemoveItemAsync(BrowserStore.Local, oldKey);
		}

		return values;
	}

	private bool Took(PreferenceGroupResponse group)
	{
		Apply(Values(group));
		return true;
	}

	/// <summary>Takes <paramref name="values"/> as the settings, telling listeners which keys changed.</summary>
	private void Apply(Dictionary<string, JsonElement> values)
	{
		var changed = values.Keys.Union(_values.Keys, StringComparer.OrdinalIgnoreCase)
			.Where(key => !Same(values.GetValueOrDefault(key), _values.GetValueOrDefault(key)))
			.ToList();
		_values = values;
		foreach (var key in changed) Changed?.Invoke(key);
	}

	private static Dictionary<string, JsonElement> Values(PreferenceGroupResponse group)
		=> group.Values is { } values
			? new Dictionary<string, JsonElement>(values, StringComparer.OrdinalIgnoreCase)
			: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

	private static JsonElement Default(string key)
		=> PlayPreferences.Group.Find(key) is { Default: { } stored } spec
			? PreferenceJson.Typed(spec, stored)
			: JsonSerializer.SerializeToElement<object?>(null);

	private static JsonElement? Parse(string text)
	{
		try
		{
			using var document = JsonDocument.Parse(text);
			return document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static bool Same(JsonElement a, JsonElement b)
		=> a.ValueKind == b.ValueKind && (a.ValueKind == JsonValueKind.Undefined || a.GetRawText() == b.GetRawText());

	/// <summary>Asks the page to say when the tab comes back into view, once.</summary>
	private async Task WatchAsync()
	{
		if (_watching) return;
		_watching = true;
		try
		{
			await js.InvokeVoidAsync("SharpMUSH.onShown", DotNetObjectReference.Create(this), nameof(OnShownAsync));
		}
		catch (JSException)
		{
			// No interop script: the settings are read again on the next sign-in or reload instead.
		}
	}
}
