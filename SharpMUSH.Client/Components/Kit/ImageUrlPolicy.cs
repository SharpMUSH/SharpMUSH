namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// Which image URLs the portal will put in an <c>&lt;img src&gt;</c>. Softcode supplies these
/// (the <c>IMAGE</c> attributes, OOB payloads, profile fields), so the client accepts only what a
/// site can serve safely: a site-relative path or https. Anything else — http, protocol-relative,
/// <c>data:</c>, <c>javascript:</c> — renders the no-image fallback instead.
/// </summary>
public static class ImageUrlPolicy
{
	public static bool IsRenderable(string? url)
	{
		if (string.IsNullOrWhiteSpace(url)) return false;
		var s = url.Trim();
		if (s.StartsWith("//", StringComparison.Ordinal)) return false;
		if (s.StartsWith('/')) return true;
		return s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
	}
}
