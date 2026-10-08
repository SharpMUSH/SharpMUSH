using MarkupString;

namespace SharpMUSH.RenderingWorker.Services;

/// <summary>
/// The pictures an MXP or Pueblo client fetches itself. It is sent the address as written, so it can
/// fetch only an absolute http or https one; the game's own pictures (<c>/assets/logo.png</c>) are
/// relative to the portal, which such a client knows nothing of.
/// </summary>
public static class ClientFetchedPictures
{
	/// <summary>
	/// <paramref name="text"/> with each picture the client could not fetch dropped, leaving the text it
	/// stands in for: a figure's art, or the picture's description.
	/// </summary>
	public static MarkupText Fetchable(MarkupText text)
	{
		if (text.Runs.IsDefaultOrEmpty
			|| !text.Runs.Any(run => run.Markups.Any(markup => markup is ImageMarkup image && !Reachable(image.Source))))
			return text;

		var parts = new List<MarkupText>();
		var position = 0;
		foreach (var run in text.Runs)
		{
			if (run.Start > position) parts.Add(MarkupText.Plain(text.Text[position..run.Start]));
			var kept = run.Markups.Where(markup => markup is not ImageMarkup image || Reachable(image.Source)).ToList();
			var part = text.Text.Substring(run.Start, run.Length);
			parts.Add(kept.Count == 0 ? MarkupText.Plain(part) : MarkupText.Wrap(MarkupSet.Of(kept), part));
			position = run.End;
		}
		if (position < text.Length) parts.Add(MarkupText.Plain(text.Text[position..]));
		return MarkupText.Concat(parts);
	}

	/// <summary>
	/// Whether <paramref name="source"/> is an absolute http or https address. Checked by scheme: on Unix
	/// a rooted path parses as an absolute file: address.
	/// </summary>
	public static bool Reachable(string source) =>
		Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
