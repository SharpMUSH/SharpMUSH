using System.Text.RegularExpressions;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// Points the server's site-relative file paths (<c>/api/wiki-assets/{id}/{name}</c>, gallery images)
/// at the API. A browser resolves such a path against the page, which is only the API when the two
/// share an origin at the root: the dev split (see <see cref="Services.ApiBaseAddressResolver"/>) and a
/// portal under a base path both lost every uploaded image. Stored content keeps the relative path, so
/// it moves between hosts; only what is put in front of the browser is resolved.
/// </summary>
/// <remarks>
/// <see cref="Base"/> is set once at startup from the "api" client's address. Unset (a test host) means
/// same-origin: paths pass through unchanged.
/// </remarks>
public static partial class ApiUrl
{
	private const string ApiPrefix = "/api/";

	public static Uri? Base { get; private set; }

	public static void UseBase(Uri apiBase) => Base = apiBase;

	/// <summary>The URL to put in a <c>src</c>: trimmed, and a server path made absolute against the API.</summary>
	public static string Resolve(string url) => Resolve(url, Base);

	public static string Resolve(string url, Uri? apiBase)
	{
		var s = url.Trim();
		return apiBase is not null && s.StartsWith(ApiPrefix, StringComparison.Ordinal)
			? new Uri(apiBase, s[1..]).ToString()
			: s;
	}

	/// <summary>Rendered HTML (a wiki article) with its <c>src</c> and <c>href</c> server paths resolved.</summary>
	public static string ResolveHtml(string html) => ResolveHtml(html, Base);

	public static string ResolveHtml(string html, Uri? apiBase) =>
		apiBase is null
			? html
			: ServerPathAttribute().Replace(html, m => $"{m.Groups["attr"].Value}=\"{Resolve(m.Groups["path"].Value, apiBase)}\"");

	[GeneratedRegex(@"(?<attr>\b(?:src|href))=""(?<path>/api/[^""]*)""")]
	private static partial Regex ServerPathAttribute();
}
