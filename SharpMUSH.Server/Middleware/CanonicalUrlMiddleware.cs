using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharpMUSH.Server.Helpers;

namespace SharpMUSH.Server.Middleware;

/// <summary>
/// Enforces canonical URL form for wiki and character routes:
/// - Spaces in path segments → underscores  (301)
/// - Wrong case on known path prefixes → lowercase prefix  (301)
/// - Trailing slash on non-root paths → stripped  (301)
/// API, hub, and static asset routes are exempted.
/// </summary>
public sealed partial class CanonicalUrlMiddleware(RequestDelegate next, ILogger<CanonicalUrlMiddleware> logger)
{
	private static readonly string[] CanonicalisedPrefixes =
	[
		"/wiki/", "/character/", "/characters", "/scenes/",
		"/help/", "/mail/", "/play", "/settings",
		"/admin/", "/login", "/register",
	];

	private static readonly string[] ExemptPrefixes =
	[
		"/api/", "/hubs/", "/mush/", "/_framework/", "/_content/",
		"/health", "/ready", "/metrics",
	];

	public async Task InvokeAsync(HttpContext context)
	{
		var req = context.Request;
		var path = req.Path.Value ?? "/";

		foreach (var exempt in ExemptPrefixes)
		{
			if (path.StartsWith(exempt, StringComparison.OrdinalIgnoreCase))
			{
				await next(context);
				return;
			}
		}

		if (HasFileExtension(path))
		{
			await next(context);
			return;
		}

		var canonical = BuildCanonical(path);

		if (!string.Equals(path, canonical, StringComparison.Ordinal))
		{
			var qs = req.QueryString.Value ?? string.Empty;
			var target = canonical + qs;
			logger.LogDebug("Canonical redirect {From} → {To}", LogSanitizer.Sanitize(path), LogSanitizer.Sanitize(target));
			context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
			context.Response.Headers.Location = target;
			return;
		}

		await next(context);
	}

	/// <summary>
	/// Produces the canonical form of a path:
	/// 1. Lowercase the first path segment prefix (e.g. /Wiki → /wiki).
	/// 2. Percent-decode then replace spaces with underscores in each segment.
	/// 3. Strip trailing slash (except root "/").
	/// </summary>
	public static string BuildCanonical(string path)
	{
		if (path == "/")
			return path;

		if (path.Length > 1 && path.EndsWith('/'))
			path = path[..^1];

		var segments = path.Split('/');
		for (var i = 0; i < segments.Length; i++)
		{
			var seg = segments[i];
			if (string.IsNullOrEmpty(seg))
				continue;

			var decoded = Uri.UnescapeDataString(seg);
			var noSpaces = decoded.Replace(' ', '_');

			if (i == 1)
				noSpaces = noSpaces.ToLowerInvariant();

			segments[i] = noSpaces;
		}

		return string.Join('/', segments);
	}

	[GeneratedRegex(@"\.[a-zA-Z0-9]+$")]
	private static partial Regex FileExtensionRegex();

	private static bool HasFileExtension(string path)
		=> FileExtensionRegex().IsMatch(path);
}
