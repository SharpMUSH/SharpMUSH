using MarkupString;

namespace SharpMUSH.Library.Markup;

/// <summary>The bounded, ordinal grapheme edit-distance contract exposed by strdistance().</summary>
public static class StringDistance
{
	public const int MaxInputCodeUnits = 65_536;
	public const int MaxInputGraphemes = 4096;
	public const long MaxEditCells = 4_000_000;
	public const string WorkLimitExceeded = "#-1 STRING DISTANCE WORK LIMIT EXCEEDED";

	public static bool TryCalculate(MarkupText source, MarkupText target, out int distance)
	{
		distance = 0;
		if (source.Length > MaxInputCodeUnits || target.Length > MaxInputCodeUnits) return false;
		var sourceCount = source.GraphemeCount;
		var targetCount = target.GraphemeCount;
		// Check CPU work before allocating token arrays or dynamic-programming rows.
		if (sourceCount > MaxInputGraphemes || targetCount > MaxInputGraphemes
			|| checked((long)sourceCount * targetCount) > MaxEditCells) return false;
		if (sourceCount == 0 || targetCount == 0)
		{
			distance = Math.Max(sourceCount, targetCount);
			return true;
		}

		// Intern cluster contents once so each edit-cell comparison is constant-time, even for long clusters.
		var symbols = new Dictionary<string, int>(StringComparer.Ordinal);
		var left = Tokenize(source.Text, sourceCount, symbols);
		var right = Tokenize(target.Text, targetCount, symbols);
		if (left.Length < right.Length) (left, right) = (right, left);
		var previous = new int[right.Length + 1];
		var current = new int[right.Length + 1];
		for (var column = 0; column <= right.Length; column++) previous[column] = column;
		for (var row = 1; row <= left.Length; row++)
		{
			current[0] = row;
			for (var column = 1; column <= right.Length; column++)
			{
				var replacement = previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1);
				current[column] = Math.Min(replacement, Math.Min(previous[column] + 1, current[column - 1] + 1));
			}
			(previous, current) = (current, previous);
		}
		distance = previous[right.Length];
		return true;
	}

	private static int[] Tokenize(string text, int count, Dictionary<string, int> symbols)
	{
		var result = new int[count];
		var index = 0;
		// A cluster seen before is looked up by span; only a new one is copied out as the key.
		var lookup = symbols.GetAlternateLookup<ReadOnlySpan<char>>();
		foreach (var range in Graphemes.Enumerate(text))
		{
			var cluster = text.AsSpan(range);
			if (!lookup.TryGetValue(cluster, out var symbol))
			{
				symbol = symbols.Count;
				lookup.TryAdd(cluster, symbol);
			}
			result[index++] = symbol;
		}
		return result;
	}
}
