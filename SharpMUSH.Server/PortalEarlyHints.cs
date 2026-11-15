using System.Text.RegularExpressions;

namespace SharpMUSH.Server;

/// <summary>
/// The <c>Link</c> header the portal's shell is sent with, naming the files <c>index.html</c> loads first, so a
/// browser starts fetching them before it has the page.
/// <para>
/// A CDN in front (Cloudflare's Early Hints) keeps the header and answers the next request for the shell with a
/// <c>103 Early Hints</c> carrying it, while it is still waiting for the server; without one, a browser still
/// reads the header before it parses the page. Kestrel cannot send a 103 itself, so the header is the whole of
/// the server's part.
/// </para>
/// <para>
/// The list is read from the shell itself, once, so it always names this build's files: the stylesheets and
/// fonts it preloads, its own stylesheets, and the Blazor boot script. A preload carrying <c>integrity</c> (the
/// SDK's <c>dotnet.js</c>) is left out, because a hint without the same integrity would not be reused for it and
/// the browser would fetch the file twice; so are the page's other scripts, which are not on the way to the
/// first render.
/// </para>
/// </summary>
public static partial class PortalEarlyHints
{
	/// <summary>The scripts worth hinting: the one that starts the runtime.</summary>
	private const string BootScriptPrefix = "_framework/";

	[GeneratedRegex(@"<(?<tag>link|script)\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase)]
	private static partial Regex Tag();

	[GeneratedRegex(@"(?<name>[a-zA-Z-]+)\s*=\s*""(?<value>[^""]*)""")]
	private static partial Regex Attribute();

	[GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
	private static partial Regex Comment();

	/// <summary>The <c>Link</c> header value for <paramref name="indexHtml"/>, or <c>null</c> when it names nothing worth hinting.</summary>
	public static string? LinkHeader(string indexHtml)
	{
		var links = Tag().Matches(Comment().Replace(indexHtml, ""))
			.Select(match => Hint(match.Groups["tag"].Value.ToLowerInvariant(), Attributes(match.Groups["attrs"].Value)))
			.OfType<string>()
			.Distinct()
			.ToList();

		return links.Count == 0 ? null : string.Join(", ", links);
	}

	/// <summary>The <c>Link</c> header value for the shell in <paramref name="webRoot"/>, or <c>null</c> when there is none.</summary>
	public static string? ForWebRoot(string? webRoot)
	{
		var path = Path.Join(webRoot ?? "wwwroot", "index.html");
		return File.Exists(path) ? LinkHeader(File.ReadAllText(path)) : null;
	}

	private static Dictionary<string, string> Attributes(string attrs)
		=> Attribute().Matches(attrs)
			.GroupBy(match => match.Groups["name"].Value.ToLowerInvariant())
			.ToDictionary(group => group.Key, group => group.First().Groups["value"].Value);

	private static string? Hint(string tag, Dictionary<string, string> attrs) => tag switch
	{
		"link" => attrs.GetValueOrDefault("rel")?.ToLowerInvariant() switch
		{
			"preload" when !attrs.ContainsKey("integrity") && attrs.GetValueOrDefault("as") is { Length: > 0 } kind
				=> Preload(attrs.GetValueOrDefault("href"), kind, attrs),
			"stylesheet" => Preload(attrs.GetValueOrDefault("href"), "style", attrs),
			_ => null,
		},
		"script" when !attrs.ContainsKey("integrity")
			&& attrs.GetValueOrDefault("src") is { } src
			&& src.TrimStart('/').StartsWith(BootScriptPrefix, StringComparison.Ordinal)
			=> Preload(src, "script", attrs),
		_ => null,
	};

	private static string? Preload(string? href, string kind, Dictionary<string, string> attrs)
	{
		// Only this site's own files: an absolute URL, or a placeholder the build did not fill in, is not hinted.
		if (string.IsNullOrWhiteSpace(href) || href.Contains("://") || href.StartsWith("//") || href.Contains('#') || href.Contains('{'))
		{
			return null;
		}

		var link = $"</{href.TrimStart('/')}>; rel=preload; as={kind}";
		if (attrs.GetValueOrDefault("type") is { Length: > 0 } type)
		{
			link += $"; type=\"{type}\"";
		}

		if (attrs.TryGetValue("crossorigin", out var crossOrigin))
		{
			link += crossOrigin is "" or "anonymous" ? "; crossorigin" : $"; crossorigin={crossOrigin}";
		}

		return link;
	}
}
