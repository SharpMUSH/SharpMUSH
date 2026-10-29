using MarkupString;

namespace SharpMUSH.RenderingWorker.Services;

/// <summary>
/// The pictures of a client that draws none, written as their addresses. A description is for a screen reader;
/// anyone else gets the address, which they can open and which many clients preview as a picture of their own.
/// </summary>
public static class PictureAddresses
{
	/// <summary>
	/// <paramref name="text"/> with each picture's description (an inline picture's text, or the description a
	/// figure without art is drawn as) replaced by its address: as it is when absolute, else the game's own at
	/// <paramref name="website"/> (<see cref="ClientFetchedPictures.Resolve"/>). A figure's art is kept, and so is a
	/// description whose address the client could not open.
	/// </summary>
	public static MarkupText Shown(MarkupText text, string? website)
	{
		var runs = text.Runs;
		if (runs.IsDefaultOrEmpty || !runs.Any(run => Described(run) is not null)) return text;

		var edits = new List<(int Start, int End, MarkupText Address)>();
		for (var i = 0; i < runs.Length; i++)
		{
			if (Described(runs[i]) is not { } image || ClientFetchedPictures.Resolve(image.Source, website) is not { } address) continue;

			// A description wrapped over several lines is one picture, and one address.
			var last = i;
			while (last + 1 < runs.Length && Equals(Described(runs[last + 1]), image)
				&& Wrapped(text.Text.AsSpan(runs[last].End, runs[last + 1].Start - runs[last].End)))
				last++;

			var kept = runs[i].Markups.Where(markup => markup is not ImageMarkup).ToArray();
			edits.Add((runs[i].Start, runs[last].End,
				kept.Length == 0 ? MarkupText.Plain(address) : MarkupText.Wrap(MarkupSet.Of(kept), address)));
			i = last;
		}

		for (var i = edits.Count - 1; i >= 0; i--)
			text = text.Replace(edits[i].Start, edits[i].End - edits[i].Start, edits[i].Address);
		return text;
	}

	/// <summary>The picture whose description <paramref name="run"/> holds, without its row, or null.</summary>
	private static ImageMarkup? Described(Run run)
	{
		foreach (var markup in run.Markups)
		{
			if (markup is ImageMarkup { Source.Length: > 0 } image && image.Row is null or { IsDescription: true })
				return image with { Row = null };
		}
		return null;
	}

	/// <summary>Whether <paramref name="gap"/> is nothing, or the line break and padding a wrap puts between two parts of one text.</summary>
	private static bool Wrapped(ReadOnlySpan<char> gap) =>
		gap.IsEmpty || gap.Contains('\n') && gap.IsWhiteSpace();
}
