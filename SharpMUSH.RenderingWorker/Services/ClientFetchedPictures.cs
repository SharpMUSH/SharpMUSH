using MarkupString;

namespace SharpMUSH.RenderingWorker.Services;

/// <summary>
/// The pictures an MXP or Pueblo client fetches itself. It is sent the address as written, so it can
/// fetch only an absolute http or https one. The game's own pictures (<c>/assets/logo.png</c>) are
/// relative to the portal, so such a client is sent them at the game's web address (<c>mud_url</c>),
/// or not at all while that is unset.
/// </summary>
public static class ClientFetchedPictures
{
	/// <summary>
	/// <paramref name="text"/> with each picture at an address the client can fetch: the game's own at
	/// <paramref name="website"/>, and any it still could not fetch dropped, leaving the text it stands in
	/// for: a figure's art, or the picture's description.
	/// </summary>
	public static MarkupText Fetchable(MarkupText text, string? website)
	{
		if (text.Runs.IsDefaultOrEmpty
			|| !text.Runs.Any(run => run.Markups.Any(markup => markup is ImageMarkup image && !IsAbsolute(image.Source))))
			return text;

		var parts = new List<MarkupText>();
		var position = 0;
		foreach (var run in text.Runs)
		{
			if (run.Start > position) parts.Add(MarkupText.Plain(text.Text[position..run.Start]));
			var kept = new List<IMarkup>();
			foreach (var markup in run.Markups)
			{
				if (markup is not ImageMarkup image || IsAbsolute(image.Source)) kept.Add(markup);
				else if (Resolve(image.Source, website) is { } address) kept.Add(image with { Source = address });
			}
			var part = text.Text.Substring(run.Start, run.Length);
			parts.Add(kept.Count == 0 ? MarkupText.Plain(part) : MarkupText.Wrap(MarkupSet.Of(kept), part));
			position = run.End;
		}
		if (position < text.Length) parts.Add(MarkupText.Plain(text.Text[position..]));
		return MarkupText.Concat(parts);
	}

	/// <summary>
	/// Where the client can fetch <paramref name="source"/>: the address itself when it is absolute http or
	/// https, the game's own picture at <paramref name="website"/> when that is one, else null.
	/// </summary>
	public static string? Resolve(string source, string? website)
	{
		if (IsAbsolute(source)) return source.Trim();
		var path = source.Trim();
		// "//host/x" names another host, not one of the game's pictures.
		if (path.Length == 0 || path.StartsWith("//", StringComparison.Ordinal) || !IsAbsolute(website)) return null;
		return Uri.TryCreate(new Uri(website!.Trim()), path, out var address) && address.Scheme is "http" or "https"
			? address.AbsoluteUri
			: null;
	}

	/// <summary>
	/// Whether <paramref name="source"/> is an absolute http or https address. Checked by scheme: on Unix
	/// a rooted path parses as an absolute file: address.
	/// </summary>
	private static bool IsAbsolute(string? source) =>
		source is not null && Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
