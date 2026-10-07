namespace SharpMUSH.Library.Markup;

/// <summary>
/// The <c>image_hosts</c> and <c>image_host_list</c> options: which pictures the game shows.
/// </summary>
/// <remarks>
/// A relative address names one of the game's own pictures and is shown unless pictures are off.
/// Only http and https addresses are ever shown from elsewhere.
/// </remarks>
public static class ImageHostPolicy
{
	/// <summary>Whether <paramref name="source"/> may be shown under <paramref name="mode"/> and <paramref name="hosts"/>.</summary>
	/// <param name="source">The picture's address.</param>
	/// <param name="mode"><c>any</c> (also when empty), <c>allow</c>, <c>block</c> or <c>off</c>.</param>
	/// <param name="hosts">Space- or comma-separated hosts; <c>*.example.com</c> covers every subdomain.</param>
	public static bool Allows(string source, string? mode, string? hosts)
	{
		mode = mode?.Trim().ToLowerInvariant();
		if (mode == "off" || string.IsNullOrWhiteSpace(source)) return false;
		// Read the way a browser reads it: tabs and line breaks dropped wherever they are, and a
		// backslash taken as a slash, so "/\\host" and "/<tab>/host" are the address "//host".
		source = string.Concat(source.Trim().Where(c => c is not ('\t' or '\n' or '\r'))).Replace('\\', '/');

		// Checked by hand: on Unix a rooted path parses as an absolute file: address, and "//host"
		// as one on another host.
		if (source.StartsWith("//", StringComparison.Ordinal)) return false;
		if (source.StartsWith('/')) return true;
		if (!Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out var uri)) return false;
		if (!uri.IsAbsoluteUri) return true;
		if (uri.Scheme is not ("http" or "https")) return false;

		return mode switch
		{
			"allow" => Listed(uri.Host, hosts),
			"block" => !Listed(uri.Host, hosts),
			_ => true,
		};
	}

	private static bool Listed(string host, string? hosts)
	{
		foreach (var entry in (hosts ?? string.Empty).Split([' ', ',', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries))
		{
			if (entry.StartsWith("*.", StringComparison.Ordinal))
			{
				if (host.EndsWith(entry[1..], StringComparison.OrdinalIgnoreCase)) return true;
			}
			else if (string.Equals(host, entry, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
