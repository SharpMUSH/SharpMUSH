namespace SharpMUSH.Client.Services;

/// <summary>
/// Minimal line-based diff used by the wiki revision history dialog.
/// Classic LCS dynamic-programming algorithm — wiki pages are small enough
/// that the O(n*m) table is never a concern, and full snapshots (not deltas)
/// are what the revision store keeps anyway.
/// </summary>
public static class LineDiff
{
	public enum LineKind { Unchanged, Added, Removed }

	public record DiffLine(LineKind Kind, string Text);

	/// <summary>
	/// Computes the line diff transforming <paramref name="oldText"/> into <paramref name="newText"/>.
	/// Removed lines come from the old text, added lines from the new.
	/// </summary>
	public static IReadOnlyList<DiffLine> Compute(string oldText, string newText)
	{
		var oldLines = SplitLines(oldText);
		var newLines = SplitLines(newText);

		var lcs = new int[oldLines.Length + 1, newLines.Length + 1];
		for (var i = oldLines.Length - 1; i >= 0; i--)
		{
			for (var j = newLines.Length - 1; j >= 0; j--)
			{
				lcs[i, j] = oldLines[i] == newLines[j]
					? lcs[i + 1, j + 1] + 1
					: Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
			}
		}

		var result = new List<DiffLine>();
		var (oi, ni) = (0, 0);
		while (oi < oldLines.Length && ni < newLines.Length)
		{
			if (oldLines[oi] == newLines[ni])
			{
				result.Add(new DiffLine(LineKind.Unchanged, oldLines[oi]));
				oi++; ni++;
			}
			else if (lcs[oi + 1, ni] >= lcs[oi, ni + 1])
			{
				result.Add(new DiffLine(LineKind.Removed, oldLines[oi]));
				oi++;
			}
			else
			{
				result.Add(new DiffLine(LineKind.Added, newLines[ni]));
				ni++;
			}
		}

		while (oi < oldLines.Length) result.Add(new DiffLine(LineKind.Removed, oldLines[oi++]));
		while (ni < newLines.Length) result.Add(new DiffLine(LineKind.Added, newLines[ni++]));

		return result;
	}

	private static string[] SplitLines(string text) =>
		string.IsNullOrEmpty(text) ? [] : text.Replace("\r\n", "\n").Split('\n');
}
