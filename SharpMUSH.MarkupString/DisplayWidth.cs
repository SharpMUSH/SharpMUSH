using System.Globalization;
using System.Text;
namespace MarkupString;

/// <summary>
/// Terminal column width of text: 0 cells for controls, combining marks and format characters,
/// 2 for East Asian wide and fullwidth code points, 1 for everything else.
/// </summary>
public static class DisplayWidth
{
	private const int ZeroWidthJoiner = 0x200D;
	private const int HangulJungseongStart = 0x1160;
	private const int HangulJongseongEnd = 0x11FF;

	/// <summary>Cells occupied by <paramref name="rune"/> on its own: 0, 1 or 2.</summary>
	public static int OfRune(Rune rune)
	{
		var value = rune.Value;
		if (value < 0x20 || value is >= 0x7F and <= 0x9F) return 0;
		if (value < 0x300) return 1;
		if (value is >= HangulJungseongStart and <= HangulJongseongEnd) return 0;
		if (IsZeroWidthCategory(Rune.GetUnicodeCategory(rune))) return 0;
		return IsWide(value) ? 2 : 1;
	}

	/// <summary>
	/// Cells occupied by <paramref name="text"/>. A ZERO WIDTH JOINER collapses the emoji that
	/// follows it into the sequence it joins, so a joined family counts as one emoji.
	/// </summary>
	public static int Of(ReadOnlySpan<char> text)
	{
		var width = 0;
		var afterJoiner = false;
		foreach (var rune in text.EnumerateRunes())
		{
			var cells = OfRune(rune);
			if (afterJoiner && cells == 2) cells = 0;
			afterJoiner = rune.Value == ZeroWidthJoiner;
			width += cells;
		}
		return width;
	}

	/// <summary>
	/// The largest cluster boundary in <paramref name="text"/> whose prefix fits in
	/// <paramref name="cells"/> columns. Never returns an index inside a grapheme cluster.
	/// </summary>
	public static int IndexAtWidth(ReadOnlySpan<char> text, int cells)
	{
		if (cells <= 0) return 0;
		var used = 0;
		var position = 0;
		while (position < text.Length)
		{
			int length, width;
			if (IsSimple(text, position))
			{
				length = 1;
				width = 1;
			}
			else
			{
				length = StringInfo.GetNextTextElementLength(text[position..]);
				if (length <= 0) length = 1;
				width = Of(text.Slice(position, length));
			}
			if (used + width > cells) return position;
			used += width;
			position += length;
		}
		return text.Length;
	}

	/// <summary>A printable ASCII character not followed by anything that could extend it.</summary>
	private static bool IsSimple(ReadOnlySpan<char> text, int position) =>
		text[position] is >= ' ' and < '\u007F'
		&& (position + 1 >= text.Length || text[position + 1] is >= ' ' and < '\u007F');

	private static bool IsZeroWidthCategory(UnicodeCategory category) => category
		is UnicodeCategory.NonSpacingMark
		or UnicodeCategory.EnclosingMark
		or UnicodeCategory.Format;

	private static bool IsWide(int value)
	{
		var ranges = DisplayWidthTable.WideRanges;
		var low = 0;
		var high = ranges.Length - 1;
		while (low <= high)
		{
			var mid = (low + high) >> 1;
			var (start, end) = ranges[mid];
			if (value < start) high = mid - 1;
			else if (value > end) low = mid + 1;
			else return true;
		}
		return false;
	}
}
