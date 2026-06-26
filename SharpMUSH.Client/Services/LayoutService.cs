using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Services;

/// <summary>
/// DB-backed, scope-aware layout service. Reads layouts from <c>/api/layouts/{scope}</c> (anonymous-
/// friendly) and writes them back with <c>layout.admin</c>. A scope that has never been customized
/// (HTTP 404) or that fails to load resolves to <see cref="GetDefaultLayout"/>.
/// </summary>
public sealed class LayoutService(IHttpClientFactory httpClientFactory, ILogger<LayoutService> logger) : ILayoutService
{
	private static readonly JsonSerializerOptions JsonOptions = LayoutSerialization.Options;

	private readonly Dictionary<string, LayoutConfiguration> _cache = new(StringComparer.OrdinalIgnoreCase);

	public event Action<string>? OnLayoutChanged;

	public async Task<LayoutConfiguration> GetLayoutAsync(string scope)
	{
		if (_cache.TryGetValue(scope, out var cached))
		{
			return cached;
		}

		var resolved = await FetchAsync(scope) ?? GetDefaultLayout(scope);
		_cache[scope] = resolved;
		return resolved;
	}

	public async Task<bool> SaveLayoutAsync(string scope, LayoutConfiguration layout)
	{
		ArgumentNullException.ThrowIfNull(layout);

		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PutAsJsonAsync($"api/layouts/{Uri.EscapeDataString(scope)}", layout, JsonOptions);
			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Saving layout for scope {Scope} failed (HTTP {Status}).", scope, (int)response.StatusCode);
				return false;
			}

			_cache[scope] = layout;
			OnLayoutChanged?.Invoke(scope);
			return true;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Could not reach the server saving layout for scope {Scope}.", scope);
			return false;
		}
	}

	public async Task<bool> ResetLayoutAsync(string scope)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.DeleteAsync($"api/layouts/{Uri.EscapeDataString(scope)}");
			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Resetting layout for scope {Scope} failed (HTTP {Status}).", scope, (int)response.StatusCode);
				return false;
			}

			_cache[scope] = GetDefaultLayout(scope);
			OnLayoutChanged?.Invoke(scope);
			return true;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Could not reach the server resetting layout for scope {Scope}.", scope);
			return false;
		}
	}

	public async Task<IReadOnlyList<string>> GetCustomizedScopesAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var scopes = await http.GetFromJsonAsync<List<string>>("api/layouts");
			return scopes ?? [];
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to list customized layout scopes.");
			return [];
		}
	}

	private async Task<LayoutConfiguration?> FetchAsync(string scope)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.GetAsync($"api/layouts/{Uri.EscapeDataString(scope)}");
			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				return null;
			}

			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Loading layout for scope {Scope} failed (HTTP {Status}).", scope, (int)response.StatusCode);
				return null;
			}

			return await response.Content.ReadFromJsonAsync<LayoutConfiguration>(JsonOptions);
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Loading layout for scope {Scope} failed.", scope);
			return null;
		}
	}

	public LayoutConfiguration GetDefaultLayout(string scope) => scope switch
	{
		LayoutScopes.Home => new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] =
				[
					new WidgetPlacement("Stats", 0, null, Span: 12),
					new WidgetPlacement("ActiveScene", 1, null, Span: 8),
					new WidgetPlacement("OnlineCharacters", 2, null, Span: 4),
					new WidgetPlacement("RecentWikiActivity", 3, null, Span: 8),
					new WidgetPlacement("Quickstart", 4, null, Span: 4)
				]
			},
			SidebarsOff),

		LayoutScopes.WikiIndex => new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] = [new WidgetPlacement("WikiIndex", 0, null)]
			},
			SidebarsOff),

		LayoutScopes.Profile => new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] =
				[
					// The character header is a Widget application (slug "character-header"), seeded at
					// startup and bridged into the widget registry; it renders through SchemaWidget.
					new WidgetPlacement("character-header", 0, null),
					new WidgetPlacement("WikiBody", 1, null)
				],
				[WidgetZone.RightSidebar] = [new WidgetPlacement("CharacterGallery", 0, null)]
			},
			SidebarsOff),

		_ => new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.TopBar] = [new WidgetPlacement("QuickLinks", 0, null)],
				[WidgetZone.LeftSidebar] = [],
				[WidgetZone.RightSidebar] = [],
				[WidgetZone.MainContent] = [],
				[WidgetZone.Footer] = []
			},
			SidebarsOff)
	};

	private static LayoutSettings SidebarsOff => new(LeftSidebarEnabled: false, RightSidebarEnabled: false);
}
