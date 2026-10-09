using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Logging;

namespace SharpMUSH.Server.Middleware;

/// <summary>
/// Enforces canonical URL form for wiki and character routes:
/// - Spaces in path segments → underscores  (301)
/// - Wrong case on known path prefixes → lowercase prefix  (301)
/// - Trailing slash on non-root paths → stripped  (301)
/// - Pre-category-change wiki paths (/wiki/{ns}/{category}/{slug}) → /wiki/{ns}/{slug}  (301)
/// - Character biographies reached through the wiki → their /character/{slug} alias  (301)
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

		if (ExemptPrefixes.Any(exempt => path.StartsWith(exempt, StringComparison.OrdinalIgnoreCase)))
		{
			await next(context);
			return;
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
	/// 2. Percent-decode then replace spaces with underscores in each segment — except below
	///    <c>/help</c>, whose tail is a help topic rather than a slug and is left verbatim.
	/// 3. Strip trailing slash (except root "/").
	/// 4. Drop the category segment links from before categories left page identity still carry.
	/// 5. Rewrite a character biography's wiki route to its /character/{slug} alias.
	/// </summary>
	public static string BuildCanonical(string path)
	{
		if (path == "/")
			return path;

		if (path.Length > 1 && path.EndsWith('/'))
			path = path[..^1];

		var segments = path.Split('/');

		// Help topics are not slugs. They are the markdown headers inside the shipped helpfiles, and
		// they genuinely contain spaces ("getting started"), case ("MAIL") and punctuation ("@mail",
		// "#-1 exception"). Slugifying them would 301 every one of those to a topic that does not
		// exist, so below /help only the prefix itself is canonicalised.
		var verbatimTail = segments.Length > 2
			&& string.Equals(segments[1], "help", StringComparison.OrdinalIgnoreCase);

		for (var i = 0; i < segments.Length; i++)
		{
			var seg = segments[i];
			if (string.IsNullOrEmpty(seg))
				continue;

			if (i > 1 && verbatimTail)
				continue;

			var decoded = Uri.UnescapeDataString(seg);
			var noSpaces = decoded.Replace(' ', '_');

			if (i == 1)
				noSpaces = noSpaces.ToLowerInvariant();

			segments[i] = noSpaces;
		}

		segments = WithoutCategorySegment(segments) ?? segments;
		return CharacterAliasFor(segments) ?? string.Join('/', segments);
	}

	/// <summary>
	/// The <c>/character/{slug}</c> alias for <c>/wiki/character/{slug}</c>, or <c>null</c> when the path
	/// is not a character biography's wiki view route.
	/// <para>
	/// Deliberately narrow. Only the bare view route is aliased: the <c>/history</c>, <c>/diff</c> and
	/// <c>/edit</c> siblings have no equivalent under <c>/character</c> and keep working where they are —
	/// which is also what stops the profile page's own history link from bouncing.
	/// </para>
	/// </summary>
	private static string? CharacterAliasFor(string[] segments) =>
		// ["", "wiki", ns, slug] — exactly four, so /history and friends do not match.
		segments is ["", "wiki", var ns, var slug]
		&& !string.IsNullOrEmpty(slug)
		&& WikiRoutes.IsCharacterProfile(ns)
			? WikiRoutes.PathFor(ns, slug)
			: null;

	/// <summary>
	/// The page a link from before categories left page identity names: <c>/wiki/{ns}/{category}/{slug}</c>
	/// (and its <c>/edit</c>, <c>/history</c>, <c>/diff</c>) is now <c>/wiki/{ns}/{slug}</c>. Null for any
	/// other path, including <c>/wiki/{ns}/{slug}/edit</c> itself.
	/// </summary>
	private static string[]? WithoutCategorySegment(string[] segments) =>
		segments switch
		{
			["", "wiki", var ns, _, var slug] when IsNamespace(ns) && !IsPageAction(slug)
				=> ["", "wiki", ns, slug],
			["", "wiki", var ns, _, var slug, var action] when IsNamespace(ns) && IsPageAction(action)
				=> ["", "wiki", ns, slug, action],
			_ => null,
		};

	private static bool IsNamespace(string segment) => WikiHelpers.ParseNamespace(segment) is not null;

	private static bool IsPageAction(string segment) => segment is "edit" or "history" or "diff";

	[GeneratedRegex(@"\.[a-zA-Z0-9]+$")]
	private static partial Regex FileExtensionRegex();

	private static bool HasFileExtension(string path)
		=> FileExtensionRegex().IsMatch(path);
}
