using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Logging;

namespace SharpMUSH.Client.Services;

/// <summary>
/// DB-backed, scope-aware layout service. Reads layouts from <c>/api/layouts/{scope}</c> (anonymous-
/// friendly) and writes them back with <c>layout.admin</c>. A scope that has never been customized
/// (HTTP 204) or that fails to load resolves to <see cref="GetDefaultLayout"/>.
/// </summary>
public sealed class LayoutService(IHttpClientFactory httpClientFactory, ILogger<LayoutService> logger) : ILayoutService
{
	private static readonly JsonSerializerOptions JsonOptions = LayoutSerialization.Options;

	private readonly Dictionary<string, LayoutConfiguration> _cache = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Concurrent first reads of one scope share a request. Only a resolved layout is kept (in
	/// <see cref="_cache"/>); a failed fetch is not, so the next read tries again.
	/// </summary>
	private readonly SingleFlight<string, LayoutConfiguration> _loads = new(StringComparer.OrdinalIgnoreCase);

	public event Action<string>? OnLayoutChanged;

	public Task<LayoutConfiguration> GetLayoutAsync(string scope) =>
		_cache.TryGetValue(scope, out var cached)
			? Task.FromResult(cached)
			: _loads.RunAsync(scope, () => LoadAsync(scope));

	private async Task<LayoutConfiguration> LoadAsync(string scope) =>
		await FetchAsync(scope) switch
		{
			LayoutConfiguration stored => Keep(scope, stored),
			NotFound => Keep(scope, GetDefaultLayout(scope)),
			// The default stands in for this read only. Kept, it would pin the tab to the default for
			// the rest of its life because the server stumbled once while the page loaded.
			Error<string> => GetDefaultLayout(scope),
		};

	private LayoutConfiguration Keep(string scope, LayoutConfiguration resolved) =>
		// A save or reset that landed while this read was in flight is newer than what it fetched.
		_cache.TryAdd(scope, resolved) ? resolved : _cache[scope];

	public async Task<bool> SaveLayoutAsync(string scope, LayoutConfiguration layout)
	{
		ArgumentNullException.ThrowIfNull(layout);

		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PutAsJsonAsync($"api/layouts/{Uri.EscapeDataString(scope)}", layout, JsonOptions);
			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Saving layout for scope {Scope} failed (HTTP {Status}).", LogSanitizer.Sanitize(scope), (int)response.StatusCode);
				return false;
			}

			_cache[scope] = layout;
			OnLayoutChanged?.Invoke(scope);
			return true;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Could not reach the server saving layout for scope {Scope}.", LogSanitizer.Sanitize(scope));
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
				logger.LogWarning("Resetting layout for scope {Scope} failed (HTTP {Status}).", LogSanitizer.Sanitize(scope), (int)response.StatusCode);
				return false;
			}

			_cache[scope] = GetDefaultLayout(scope);
			OnLayoutChanged?.Invoke(scope);
			return true;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Could not reach the server resetting layout for scope {Scope}.", LogSanitizer.Sanitize(scope));
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

	/// <summary>The stored layout; <see cref="NotFound"/> when the scope has never been customized; an
	/// error when the server could not be asked or did not answer usably.</summary>
	private async Task<FoundResult<LayoutConfiguration>> FetchAsync(string scope)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.GetAsync($"api/layouts/{Uri.EscapeDataString(scope)}");

			// 204 is the server saying "nothing stored for this scope" — the ordinary state of an
			// uncustomized game, and not a failure. 404 is still accepted so a client served from a
			// cached build keeps working against an older server (and vice versa) during a rollout.
			if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
			{
				return new NotFound();
			}

			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Loading layout for scope {Scope} failed (HTTP {Status}).", LogSanitizer.Sanitize(scope), (int)response.StatusCode);
				return new Error<string>($"HTTP {(int)response.StatusCode}");
			}

			return await response.Content.ReadFromJsonAsync<LayoutConfiguration>(JsonOptions) is { } layout
				? layout
				: new Error<string>("empty layout body");
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Loading layout for scope {Scope} failed.", LogSanitizer.Sanitize(scope));
			return new Error<string>(ex.Message);
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
					new WidgetPlacement("WikiBody", 1, HomeWikiPage, Span: 12),
					new WidgetPlacement("ActiveScene", 2, null, Span: 8),
					new WidgetPlacement("OnlineCharacters", 3, null, Span: 4),
					new WidgetPlacement("RecentWikiActivity", 4, null, Span: 8),
					new WidgetPlacement("Quickstart", 5, null, Span: 4)
				]
			},
			SidebarsOff),

		LayoutScopes.WikiIndex => new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] = [new WidgetPlacement("WikiIndex", 0, null)],
				// D1 README §6.1: the wiki home aside is "Recently changed" then "Live now".
				[WidgetZone.RightSidebar] =
				[
					new WidgetPlacement("RecentWikiActivity", 0, null),
					new WidgetPlacement("ActiveScene", 1, null)
				]
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
				// README §6.3: Gallery, then Recent scenes, then Often plays with.
				[WidgetZone.RightSidebar] =
				[
					new WidgetPlacement("CharacterGallery", 0, null),
					new WidgetPlacement("RecentScenes", 1, null),
					new WidgetPlacement("OftenPlaysWith", 2, null)
				]
			},
			SidebarsOff),

		// README §5.6 / §7.4: Here, then Exits.
		LayoutScopes.Play => new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.RightSidebar] =
				[
					new WidgetPlacement("Here", 0, null),
					new WidgetPlacement("Exits", 1, null)
				]
			},
			SidebarsOff),

		_ => new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				// README §10 Q1: no top bar in D1; the zone draws a strip only once an admin fills it.
				[WidgetZone.TopBar] = [],
				[WidgetZone.LeftSidebar] = [],
				[WidgetZone.RightSidebar] = [],
				[WidgetZone.MainContent] = [],
				[WidgetZone.Footer] = []
			},
			SidebarsOff)
	};

	/// <summary>
	/// Addresses the Wiki Body widget at <c>main/general/home</c> for the landing page. The config is
	/// explicit because that widget's no-config behaviour is the character biography, which the home
	/// page has no character for. Parsed rather than serialized from an anonymous type: the client
	/// publishes trimmed, and this needs no reflection to survive it.
	/// </summary>
	private static readonly JsonElement HomeWikiPage = Config("""{"slug":"home","namespace":"main"}""");

	/// <summary>
	/// A standalone <see cref="JsonElement"/> parsed from literal JSON. The document owns pooled
	/// buffers and the clone does not, so the document has to be disposed once the copy is taken.
	/// </summary>
	private static JsonElement Config(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	private static LayoutSettings SidebarsOff => new(LeftSidebarEnabled: false, RightSidebarEnabled: false);
}
