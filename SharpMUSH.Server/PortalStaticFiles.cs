using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace SharpMUSH.Server;

/// <summary>
/// How this server hands out the Blazor WASM portal it is bundled with.
/// <para>
/// The published image copies the client's <c>wwwroot</c> into the server's web root and, beside it
/// in the content root, the client's <see cref="ManifestFileName">static web assets endpoints
/// manifest</see>. When that manifest is present the portal is served by <c>MapStaticAssets</c>, which
/// reads every response header from it: fingerprinted files (<c>_framework/*.{hash}.wasm</c> and the
/// like) are <c>max-age=31536000, immutable</c>, everything else is <c>no-cache</c> with a content
/// ETag, and the <c>.br</c>/<c>.gz</c> files published beside each asset are negotiated for every
/// path rather than only under <c>_framework</c>.
/// </para>
/// <para>
/// It replaces <c>UseBlazorFrameworkFiles</c> + <c>UseStaticFiles</c>, which marked every
/// <c>_framework</c> file <c>no-cache</c> — content-hashed names included, so a repeat visit
/// revalidated ~130 files one round trip each — and sent no <c>Cache-Control</c> at all for
/// <c>index.html</c>, the CSS and <c>_content/**</c>, leaving browsers free to keep a stale
/// <c>index.html</c> pointing at a previous deploy's assets. Those two remain the path when there is
/// no manifest (a development run, a test host), so nothing that serves files without one breaks.
/// </para>
/// </summary>
public static class PortalStaticFiles
{
	/// <summary>Written by <c>dotnet publish</c> of SharpMUSH.Client into its publish root.</summary>
	public const string ManifestFileName = "SharpMUSH.Client.staticwebassets.endpoints.json";

	/// <summary>
	/// The SPA shell names the content-hashed assets of one particular deploy, so it must be
	/// revalidated on every navigation — otherwise a cached copy loads a build that no longer exists.
	/// </summary>
	public const string IndexCacheControl = "no-cache";

	/// <summary>The portal's endpoints manifest in the content root, or <c>null</c> when there is none.</summary>
	public static string? FindManifest(IHostEnvironment environment)
	{
		var path = Path.Combine(environment.ContentRootPath, ManifestFileName);
		return File.Exists(path) ? path : null;
	}

	/// <summary>
	/// The middleware half, placed where the static-file middleware belongs in the pipeline. With a
	/// manifest it adds nothing: the assets are endpoints (see <see cref="MapPortal"/>).
	/// </summary>
	public static IApplicationBuilder UsePortalStaticFiles(this IApplicationBuilder app, string? manifestPath)
	{
		if (manifestPath is null)
		{
			app.UseBlazorFrameworkFiles();
			app.UseStaticFiles();
		}

		return app;
	}

	/// <summary>
	/// The endpoint half: the manifest's asset endpoints, when there is one, and the SPA fallback that
	/// sends every other non-file route to <c>index.html</c> so Blazor routes deep links client-side.
	/// </summary>
	/// <remarks>
	/// The asset endpoints are anonymous and exempt from rate limiting. They are the same bytes for
	/// every caller; the limiter and authorization exist for the API.
	/// </remarks>
	public static IEndpointRouteBuilder MapPortal(this IEndpointRouteBuilder endpoints, string? manifestPath)
	{
		if (manifestPath is not null)
		{
			endpoints.MapStaticAssets(manifestPath)
				.AllowAnonymous()
				.DisableRateLimiting();
		}

		endpoints.MapFallbackToFile("index.html", new StaticFileOptions
		{
			OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = IndexCacheControl,
		})
			// Held back by PortalStartupPage until the server is first ready.
			.WithMetadata(new PortalStartupPage.PortalShellEndpoint());

		return endpoints;
	}
}
