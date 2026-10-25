namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// The page of a listing that starts <see cref="Skip"/> admitted rows in: the first page at 0. Every row before it is
/// read, so this is for a page reached without a token, such as a link that names an offset; the window it returns
/// carries the tokens that move on from there.
/// </summary>
public readonly record struct WikiPageAt(int Skip);

/// <summary>The page of a listing that follows the row <see cref="Token"/> names (a window's <see cref="WikiPageWindow.Next"/>).</summary>
public sealed record WikiPageAfter(string Token);

/// <summary>The page of a listing that ends just before the row <see cref="Token"/> names (a window's <see cref="WikiPageWindow.Previous"/>).</summary>
public sealed record WikiPageBefore(string Token);

/// <summary>
/// Where a page of a listing starts. A token is a position in the store's own order, not a grant: the reader's
/// visibility is applied again on every read, and a row deleted since the token was written leaves it valid.
/// </summary>
public union WikiPageCursor(WikiPageAt, WikiPageAfter, WikiPageBefore);

/// <summary>
/// One page of a listing read from a cursor, so a later page costs what lies between the two rows and not every row
/// before it. <see cref="Previous"/> is null at the start of the listing and <see cref="Next"/> at its end; either
/// may name a row that is gone by the time it is used, which only moves where the next read starts. A page read
/// backwards that reaches the start has a null <see cref="Previous"/>, whatever offset the reader thought it was at.
/// </summary>
public sealed record WikiPageWindow(IReadOnlyList<WikiPage> Pages, string? Previous, string? Next);

/// <summary>Bounds shared by the providers' cursor tokens.</summary>
public static class WikiPageWindows
{
	/// <summary>The longest token a provider reads, in characters; a title or slug long enough to exceed it cannot be a key.</summary>
	public const int MaxTokenLength = 4096;

	/// <summary>The bytes of <paramref name="token"/> after <paramref name="prefix"/>, or null for one this provider did not write.</summary>
	public static byte[]? Decode(string token, string prefix)
	{
		if (token.Length > MaxTokenLength || !token.StartsWith(prefix, StringComparison.Ordinal)) return null;
		var payload = token[prefix.Length..].Replace('-', '+').Replace('_', '/');
		payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
		var bytes = new byte[payload.Length];
		return payload.Length > 0 && Convert.TryFromBase64String(payload, bytes, out var written) ? bytes[..written] : null;
	}

	/// <summary><paramref name="bytes"/> as a URL-safe token after <paramref name="prefix"/>.</summary>
	public static string Encode(ReadOnlySpan<byte> bytes, string prefix)
		=> prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
