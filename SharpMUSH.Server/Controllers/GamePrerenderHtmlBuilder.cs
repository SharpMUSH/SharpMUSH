using SharpMUSH.Configuration.Options;
using System.Text;
using System.Web;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The page <c>BotPrerenderMiddleware</c> serves a crawler for a portal page that is not a wiki article or a help
/// topic (Home, the character and scene lists, sign-in): the game's name, tagline, description and banner as
/// OpenGraph and Twitter card tags, so a link pasted into Discord shows the game rather than "SharpMUSH".
/// </summary>
public static class GamePrerenderHtmlBuilder
{
	/// <summary>The SharpMUSH logo as a PNG: link previews do not show an SVG.</summary>
	public const string DefaultImage = "/assets/logo.png";

	/// <summary>The paths that get the game's card: Home and the portal's public lists and entry pages.</summary>
	public static bool IsGamePath(string path)
	{
		var trimmed = path.Length > 1 ? path.TrimEnd('/') : path;
		return trimmed is "/" || Exact.Contains(trimmed)
			|| trimmed.StartsWith("/scenes/", StringComparison.OrdinalIgnoreCase);
	}

	private static readonly HashSet<string> Exact = new(StringComparer.OrdinalIgnoreCase)
	{
		"/characters", "/scenes", "/welcome", "/login", "/register", "/play"
	};

	/// <param name="options">The game's options: its name, and its portal identity (<see cref="PortalIdentity"/>).</param>
	/// <param name="baseUrl">The scheme and host the request came in on, which a relative picture is resolved against.</param>
	/// <param name="path">The requested path, for <c>og:url</c> and the canonical link.</param>
	public static string GenerateGamePrerenderHtml(SharpMUSHOptions options, string baseUrl, string path)
	{
		var name = string.IsNullOrWhiteSpace(options.Net.MudName) ? "SharpMUSH" : options.Net.MudName.Trim();
		var tagline = PortalIdentity.Tagline(options.Cosmetic);
		var description = PortalIdentity.PreviewDescription(options);
		var summary = description.Length > 0 ? description : tagline;
		var banner = PortalIdentity.Banner(options.Cosmetic);
		var image = Absolute(baseUrl, Previewable(banner) ?? Previewable(NonEmpty(options.Cosmetic.PortalLogo)) ?? DefaultImage);
		var title = tagline.Length > 0 ? $"{name}: {tagline}" : name;
		// A scene's own address has no card of its own yet, so every scene is one page to a crawler: the list.
		var url = $"{baseUrl}{(path.StartsWith("/scenes/", StringComparison.OrdinalIgnoreCase) ? "/scenes" : path)}";

		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE html>");
		sb.AppendLine("<html>");
		sb.AppendLine("<head>");
		sb.AppendLine("  <meta charset=\"utf-8\" />");
		sb.AppendLine($"  <title>{Encode(title)}</title>");
		sb.AppendLine($"  <link rel=\"canonical\" href=\"{Encode(url)}\" />");
		if (summary.Length > 0) sb.AppendLine($"  <meta name=\"description\" content=\"{Encode(Shorten(summary))}\" />");
		sb.AppendLine($"  <meta property=\"og:site_name\" content=\"{Encode(name)}\" />");
		sb.AppendLine($"  <meta property=\"og:title\" content=\"{Encode(title)}\" />");
		if (summary.Length > 0) sb.AppendLine($"  <meta property=\"og:description\" content=\"{Encode(Shorten(summary))}\" />");
		sb.AppendLine("  <meta property=\"og:type\" content=\"website\" />");
		sb.AppendLine($"  <meta property=\"og:url\" content=\"{Encode(url)}\" />");
		sb.AppendLine($"  <meta property=\"og:image\" content=\"{Encode(image)}\" />");
		// A banner is wide, so the large card; the logo is square, so the small one.
		sb.AppendLine($"  <meta name=\"twitter:card\" content=\"{(banner is null ? "summary" : "summary_large_image")}\" />");
		sb.AppendLine($"  <meta name=\"twitter:title\" content=\"{Encode(title)}\" />");
		if (summary.Length > 0) sb.AppendLine($"  <meta name=\"twitter:description\" content=\"{Encode(Shorten(summary))}\" />");
		sb.AppendLine($"  <meta name=\"twitter:image\" content=\"{Encode(image)}\" />");
		sb.AppendLine("</head>");
		sb.AppendLine("<body>");
		sb.AppendLine($"  <h1>{Encode(name)}</h1>");
		if (tagline.Length > 0) sb.AppendLine($"  <p>{Encode(tagline)}</p>");
		if (description.Length > 0) sb.AppendLine($"  <p>{Encode(description)}</p>");
		sb.AppendLine("</body>");
		sb.AppendLine("</html>");
		return sb.ToString();
	}

	/// <summary>A path on this server made absolute against <paramref name="baseUrl"/>; an http(s) address as it is.</summary>
	private static string Absolute(string baseUrl, string picture)
		=> picture.StartsWith('/') ? $"{baseUrl}{picture}" : picture;

	private static string Shorten(string text) => text.Length > 200 ? text[..200].TrimEnd() + "…" : text;

	private static string Encode(string text) => HttpUtility.HtmlEncode(text);

	/// <summary>The picture, unless it is an SVG, which link previews do not show.</summary>
	private static string? Previewable(string? picture) =>
		picture is not null && picture.Split('?', '#')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? null : picture;

	private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
