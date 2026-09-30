using System.Net.Http.Json;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Client.Services;

/// <summary>
/// A startup snapshot of the registered Dynamic Applications (Area 21), used to bridge Widget-kind
/// applications into the layout system: the palette lists them, <c>SchemaWidget</c> resolves a
/// placement's schema/data routes from here by slug, and the nav menu builds its application links
/// from it. Loaded once (anonymous) per page load; an admin who adds an application sees it after a
/// reload.
/// </summary>
/// <remarks>
/// The load runs alongside the portal's first render rather than ahead of it: startup used to await
/// it before building the host, which put a full round trip (up to its five-second timeout) in front
/// of every visitor's first frame. Nothing needs it that early — an application-backed placement
/// renders through <c>SchemaWidget</c> whether or not the snapshot has arrived — so readers that do
/// need the whole list await <see cref="Loaded"/> instead.
/// </remarks>
public sealed class ApplicationCatalog
{
	private IReadOnlyDictionary<string, PortalApplication> _bySlug;

	/// <summary>A catalog that is complete already — tests, and anything that has the list in hand.</summary>
	public ApplicationCatalog(IEnumerable<PortalApplication> apps)
	{
		_bySlug = Index(apps);
		Loaded = Task.CompletedTask;
	}

	private ApplicationCatalog(Func<ApplicationCatalog, Task> load)
	{
		_bySlug = Index([]);
		Loaded = load(this);
	}

	/// <summary>Completes once the snapshot is in, whether or not the fetch succeeded. Never faults.</summary>
	public Task Loaded { get; }

	/// <summary>The application with this slug, or null. Null while <see cref="Loaded"/> is pending.</summary>
	public PortalApplication? Get(string? slug)
		=> slug is not null && _bySlug.TryGetValue(slug, out var app) ? app : null;

	/// <summary>Every registered application. Empty while <see cref="Loaded"/> is pending.</summary>
	public IReadOnlyList<PortalApplication> All => _bySlug.Values.ToList();

	/// <summary>All Widget-kind applications (those placeable in layout zones).</summary>
	public IReadOnlyList<PortalApplication> WidgetApps
		=> _bySlug.Values.Where(a => a.KindEnum == ApplicationKind.Widget).ToList();

	/// <summary>
	/// Starts loading the application registry and returns at once. Uses a bare client with a short
	/// timeout so a slow/unreachable API degrades to an empty catalog. The GET is anonymous.
	/// </summary>
	/// <param name="apiBaseAddress">
	/// The server's API root, already resolved by <see cref="ApiBaseAddressResolver"/> — this takes the
	/// answer rather than deriving its own, so it cannot disagree with the "api" HttpClient about where
	/// the API lives.
	/// </param>
	/// <param name="onLoaded">Runs once the snapshot is in (not on failure) — startup uses it to add the
	/// Widget-kind applications to the layout palette.</param>
	/// <param name="handler">The transport; tests supply one. Not disposed here.</param>
	public static ApplicationCatalog StartLoading(Uri apiBaseAddress, Action<ApplicationCatalog>? onLoaded = null,
		HttpMessageHandler? handler = null)
		=> new(catalog => catalog.LoadAsync(apiBaseAddress, onLoaded, handler));

	private async Task LoadAsync(Uri apiBaseAddress, Action<ApplicationCatalog>? onLoaded, HttpMessageHandler? handler)
	{
		try
		{
			using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
			http.BaseAddress = apiBaseAddress;
			http.Timeout = TimeSpan.FromSeconds(5);
			var apps = await http.GetFromJsonAsync<List<PortalApplication>>("api/applications");
			_bySlug = Index(apps ?? []);
			Console.WriteLine($"[ApplicationCatalog] Loaded {_bySlug.Count} application(s), {WidgetApps.Count} widget(s) from {apiBaseAddress}api/applications.");
			onLoaded?.Invoke(this);
		}
		catch (Exception ex)
		{
			// Network/parse/timeout — degrade gracefully. Rendering still works: SchemaWidget lazily
			// fetches an app by slug on a catalog miss; only the palette and nav listing are affected
			// this session.
			Console.WriteLine($"[ApplicationCatalog] Startup load failed ({ex.GetType().Name}: {ex.Message}); app widgets will resolve lazily.");
		}
	}

	private static Dictionary<string, PortalApplication> Index(IEnumerable<PortalApplication> apps)
		=> apps.ToDictionary(a => a.Slug, StringComparer.OrdinalIgnoreCase);
}
