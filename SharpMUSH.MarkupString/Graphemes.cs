using System.Globalization;
namespace MarkupString;

/// <summary>
/// Grapheme cluster boundaries over UTF-16 text. Every index this returns is a cluster
/// boundary, so cutting there never leaves a lone surrogate, a stranded combining mark, or
/// half of an emoji sequence.
/// </summary>
/// <remarks>
/// The expensive path (<see cref="StringInfo.GetNextTextElementLength(ReadOnlySpan{char})"/>)
/// runs only when the characters around the index could belong to one cluster; plain text
/// pays two comparisons.
/// </remarks>
public static class Graphemes
{
	private const char ZeroWidthJoiner = '\u200D';

	/// <summary>
	/// How far back the cluster walk starts from a cut. Clusters longer than this (very deep
	/// emoji ZWJ sequences) may snap to an interior boundary rather than the cluster start.
	/// </summary>
	private const int ScanBack = 64;

	/// <summary>Returns the largest cluster boundary at or before <paramref name="index"/>.</summary>
	public static int SnapStart(ReadOnlySpan<char> text, int index)
	{
		if (index <= 0) return 0;
		if (index >= text.Length) return text.Length;
		return MayBeInsideCluster(text, index) ? BoundaryAtOrBefore(text, index) : index;
	}

	/// <summary>Returns the smallest cluster boundary at or after <paramref name="index"/>.</summary>
	public static int SnapEnd(ReadOnlySpan<char> text, int index)
	{
		if (index <= 0) return 0;
		if (index >= text.Length) return text.Length;
		if (!MayBeInsideCluster(text, index)) return index;
		var start = BoundaryAtOrBefore(text, index);
		if (start == index) return index;
		var length = StringInfo.GetNextTextElementLength(text[start..]);
		return length <= 0 ? index : Math.Min(text.Length, start + length);
	}

	/// <summary>True when <paramref name="index"/> is a cluster boundary (or an edge of the text).</summary>
	public static bool IsBoundary(ReadOnlySpan<char> text, int index)
	{
		if (index <= 0 || index >= text.Length) return true;
		return !MayBeInsideCluster(text, index) || BoundaryAtOrBefore(text, index) == index;
	}

	private static int BoundaryAtOrBefore(ReadOnlySpan<char> text, int index)
	{
		var scan = Math.Max(0, index - ScanBack);
		while (scan > 0 && MayBeInsideCluster(text, scan)) scan--;

		var position = scan;
		while (position < index)
		{
			var length = StringInfo.GetNextTextElementLength(text[position..]);
			if (length <= 0) break;
			if (position + length > index) return position;
			position += length;
		}
		return position;
	}

	/// <summary>
	/// Cheap over-approximation: false guarantees <paramref name="index"/> is a boundary, true
	/// only means the cluster walk has to decide. Never under-reports.
	/// </summary>
	private static bool MayBeInsideCluster(ReadOnlySpan<char> text, int index)
	{
		var current = text[index];
		var previous = text[index - 1];
		if (current < '\u0300' && previous < '\u0300') return previous == '\r' && current == '\n';
		if (char.IsSurrogate(current) || char.IsSurrogate(previous)) return true;
		if (current == ZeroWidthJoiner || previous == ZeroWidthJoiner) return true;
		return IsExtending(CharUnicodeInfo.GetUnicodeCategory(current));
	}

	private static bool IsExtending(UnicodeCategory category) => category
		is UnicodeCategory.NonSpacingMark
		or UnicodeCategory.SpacingCombiningMark
		or UnicodeCategory.EnclosingMark
		or UnicodeCategory.Format;
}
