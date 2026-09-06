namespace MarkupString.Ansi;

/// <summary>Whether a link target may be rendered as something a client will navigate to.</summary>
public static class UrlSafety
{
	/// <summary>
	/// True when <paramref name="url"/> is safe to render as a navigable hyperlink. Relative URLs
	/// (no scheme) and a small allow-list of schemes pass; dangerous schemes such as
	/// <c>javascript:</c>, <c>data:</c>, <c>vbscript:</c> and <c>file:</c> are rejected, so a link
	/// carrying one renders as plain text instead. This applies to URL links only — a command link
	/// carries a MUSH command, never a browser-navigable href.
	/// </summary>
	public static bool IsSafeNavigableUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url)) return false;
		if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)) return false;

		// A relative URL or a bare fragment cannot name a scheme, so there is none to distrust.
		if (!uri.IsAbsoluteUri) return true;

		// Uri.Scheme is already lower-cased, so "JavaScript:" is caught here too.
		return uri.Scheme is "http" or "https" or "mailto" or "ftp" or "ftps" or "tel";
	}
}
