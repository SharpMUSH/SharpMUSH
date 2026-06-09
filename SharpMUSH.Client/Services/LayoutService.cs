using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Services;

/// <inheritdoc/>
public sealed class LayoutService : ILayoutService
{
	private const string LocalStorageKey = "sharpmush_layout";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	private readonly IJSRuntime _js;
	private LayoutConfiguration? _cached;

	public event Action? OnLayoutChanged;

	public LayoutService(IJSRuntime js) => _js = js;

	/// <inheritdoc/>
	public async Task<LayoutConfiguration> GetLayoutAsync()
	{
		if (_cached is not null)
			return _cached;

		try
		{
			var json = await _js.InvokeAsync<string?>("localStorage.getItem", LocalStorageKey);
			if (json is not null)
			{
				var loaded = JsonSerializer.Deserialize<LayoutConfiguration>(json, JsonOptions);
				if (loaded is not null)
				{
					_cached = loaded;
					return _cached;
				}
			}
		}
		catch (JSException)
		{
			// localStorage unavailable — fall through to default
		}
		catch (JSDisconnectedException)
		{
			// Circuit disconnected — fall through to default
		}
		catch (JsonException)
		{
			// Malformed JSON in localStorage — fall through to default
		}

		_cached = GetDefaultLayout();
		return _cached;
	}

	/// <inheritdoc/>
	public async Task SaveLayoutAsync(LayoutConfiguration layout)
	{
		ArgumentNullException.ThrowIfNull(layout);
		_cached = layout;

		try
		{
			var json = JsonSerializer.Serialize(layout, JsonOptions);
			await _js.InvokeVoidAsync("localStorage.setItem", LocalStorageKey, json);
		}
		catch (JSException)
		{
			// localStorage unavailable — in-memory only
		}
		catch (JSDisconnectedException)
		{
			// Circuit disconnected — in-memory only
		}

		OnLayoutChanged?.Invoke();
	}

	/// <inheritdoc/>
	public LayoutConfiguration GetDefaultLayout()
	{
		return new LayoutConfiguration(
			Zones: new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.TopBar] =
				[
					new WidgetPlacement("QuickLinks", 0, null)
				],
				[WidgetZone.LeftSidebar] = [],
				[WidgetZone.RightSidebar] = [],
				[WidgetZone.MainContent] =
				[
					new WidgetPlacement("WelcomeText", 0, null)
				],
				[WidgetZone.Footer] = []
			},
			Settings: new LayoutSettings(
				LeftSidebarEnabled: false,
				RightSidebarEnabled: false));
	}
}
