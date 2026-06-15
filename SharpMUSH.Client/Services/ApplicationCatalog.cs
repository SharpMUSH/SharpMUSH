using System.Net.Http.Json;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Client.Services;

/// <summary>
/// A startup snapshot of the registered Dynamic Applications (Area 21), used to bridge Widget-kind
/// applications into the layout system: the palette lists them and <c>SchemaWidget</c> resolves a
/// placement's schema/data routes from here by slug. Loaded once (anonymous) at boot; an admin who
/// adds an application sees it after a reload.
/// </summary>
public sealed class ApplicationCatalog
{
	private readonly Dictionary<string, PortalApplication> _bySlug;

	public ApplicationCatalog(IEnumerable<PortalApplication> apps)
		=> _bySlug = apps.ToDictionary(a => a.Slug, StringComparer.OrdinalIgnoreCase);

	/// <summary>The application with this slug, or null.</summary>
	public PortalApplication? Get(string? slug)
		=> slug is not null && _bySlug.TryGetValue(slug, out var app) ? app : null;

	/// <summary>All Widget-kind applications (those placeable in layout zones).</summary>
	public IReadOnlyList<PortalApplication> WidgetApps
		=> _bySlug.Values.Where(a => a.KindEnum == ApplicationKind.Widget).ToList();

	/// <summary>
	/// Loads the application registry once at startup. Uses a bare client with a short timeout so a
	/// slow/unreachable API degrades to an empty catalog instead of hanging boot. The GET is anonymous.
	/// </summary>
	public static async Task<ApplicationCatalog> LoadAsync(string hostBaseAddress)
	{
		try
		{
			var uri = new UriBuilder(hostBaseAddress) { Scheme = "https", Port = 8081 }.Uri;
			using var http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(5) };
			var apps = await http.GetFromJsonAsync<List<PortalApplication>>("api/applications");
			var catalog = new ApplicationCatalog(apps ?? []);
			Console.WriteLine($"[ApplicationCatalog] Loaded {catalog._bySlug.Count} application(s), {catalog.WidgetApps.Count} widget(s) from {uri}api/applications.");
			return catalog;
		}
		catch (Exception ex)
		{
			// Network/parse/timeout — degrade gracefully. Rendering still works: SchemaWidget lazily
			// fetches an app by slug on a catalog miss; only the palette listing is affected this session.
			Console.WriteLine($"[ApplicationCatalog] Startup load failed ({ex.GetType().Name}: {ex.Message}); app widgets will resolve lazily.");
			return new ApplicationCatalog([]);
		}
	}
}
