namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// Which image URLs the portal will put in an <c>&lt;img src&gt;</c>. Softcode supplies these
/// (the <c>IMAGE</c> attributes, OOB payloads, profile fields), so the client accepts only what a
/// site can serve safely: a site-relative path or https. Anything else — http, protocol-relative,
/// <c>data:</c>, <c>javascript:</c> — renders the no-image fallback instead.
/// </summary>
/// <remarks>
/// The WHATWG URL parser treats a backslash like a slash for special schemes, so <c>/\host/x</c>
/// is protocol-relative and loads from <c>host</c>; browsers also strip tabs and newlines before
/// parsing, so <c>/\t\host</c> is the same thing. A backslash or an ASCII control character
/// anywhere is therefore rejected outright.
/// </remarks>
public static class ImageUrlPolicy
{
	public static bool IsRenderable(string? url)
	{
		if (string.IsNullOrWhiteSpace(url)) return false;
		var s = url.Trim();
		if (s.Any(c => c == '\\' || char.IsControl(c))) return false;
		if (s.StartsWith("//", StringComparison.Ordinal)) return false;
		if (s.StartsWith('/')) return true;
		return s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
	}
}
