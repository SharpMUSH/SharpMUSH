using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

// The DisplayWidth instance property below hides the type of the same name, so operations
// reach the measuring helpers through this alias.
using Cells = MarkupString.DisplayWidth;

namespace MarkupString;

/// <summary>
/// One replacement inside a <see cref="MarkupText.Splice"/> batch: the half-open range
/// <c>[Start, Start + Length)</c> of the target is replaced by <see cref="Replacement"/>.
/// </summary>
public readonly record struct Edit(int Start, int Length, MarkupText Replacement);

public sealed partial class MarkupText
{
	private int _displayWidth = -1;

	/// <summary>
	/// Width of the text in terminal cells: East Asian wide and fullwidth characters count two,
	/// combining marks and controls count zero. Unlike <see cref="Length"/> this is what a
	/// column layout has to align on.
	/// </summary>
	public int DisplayWidth => _displayWidth >= 0 ? _displayWidth : _displayWidth = Cells.Of(Text);

	/// <summary>The remainder of the text from <paramref name="start"/>.</summary>
	public MarkupText Substring(int start) => Substring(start, Length - Math.Max(0, start));

	/// <summary>
	/// The text from <paramref name="start"/> for <paramref name="length"/> code units, clamped
	/// to the text. Both ends snap down to a grapheme cluster boundary, so a substring never
	/// splits a cluster and never grows past what was asked for.
	/// </summary>
	public MarkupText Substring(int start, int length)
	{
		if (length <= 0 || start >= Length) return Empty;
		var from = Math.Max(0, start);
		var to = length >= Length - from ? Length : from + length;
		from = Graphemes.SnapStart(Text, from);
		to = Graphemes.SnapStart(Text, to);
		if (to <= from) return Empty;
		if (from == 0 && to == Length) return this;

		var runs = ImmutableArray.CreateBuilder<Run>();
		ClipInto(from, to, -from, runs);
		return new MarkupText(Text[from..to], runs.ToImmutable());
	}

	/// <summary>Splits on the plain text of <paramref name="delimiter"/>; its markup is ignored.</summary>
	public MarkupText[] Split(MarkupText delimiter) => Split(delimiter.Text);

	/// <summary>
	/// Splits on every non-overlapping ordinal occurrence of <paramref name="delimiter"/>. An
	/// empty text yields no segments; an empty delimiter yields this text unsplit.
	/// </summary>
	public MarkupText[] Split(string delimiter)
	{
		ArgumentNullException.ThrowIfNull(delimiter);
		if (Length == 0) return [];
		if (delimiter.Length == 0) return [this];

		List<int>? positions = null;
		var position = 0;
		while (position <= Length - delimiter.Length)
		{
			var found = Text.IndexOf(delimiter, position, StringComparison.Ordinal);
			if (found < 0) break;
			(positions ??= []).Add(found);
			position = found + delimiter.Length;
		}
		if (positions is null) return [this];

		var segments = new MarkupText[positions.Count + 1];
		var cursor = 0;
		for (var i = 0; i < positions.Count; i++)
		{
			segments[i] = Substring(cursor, positions[i] - cursor);
			cursor = positions[i] + delimiter.Length;
		}
		segments[^1] = Substring(cursor, Length - cursor);
		return segments;
	}

	/// <summary>Trims the plain text of <paramref name="chars"/> off the requested end(s).</summary>
	public MarkupText Trim(TrimType type, MarkupText chars) => Trim(type, chars.Text);

	/// <summary>
	/// Removes any leading and/or trailing characters that appear in <paramref name="chars"/>.
	/// </summary>
	public MarkupText Trim(TrimType type, string chars = " ")
	{
		ArgumentNullException.ThrowIfNull(chars);
		if (Length == 0 || chars.Length == 0) return this;

		var start = 0;
		var end = Length;
		if (type is TrimType.TrimStart or TrimType.TrimBoth)
			while (start < end && chars.Contains(Text[start])) start++;
		if (type is TrimType.TrimEnd or TrimType.TrimBoth)
			while (end > start && chars.Contains(Text[end - 1])) end--;

		return start == 0 && end == Length ? this : Substring(start, end - start);
	}

	/// <summary>
	/// Brings the text to <paramref name="width"/> display cells by adding <paramref name="fill"/>.
	/// Text already at or beyond the width is either cut on a cluster boundary
	/// (<see cref="TruncationType.Truncate"/>, then filled if a wide character left a cell short)
	/// or returned unchanged (<see cref="TruncationType.Overflow"/>).
	/// </summary>
	/// <remarks>
	/// The result is exactly <paramref name="width"/> display cells wide, with two exceptions:
	/// <see cref="TruncationType.Overflow"/> keeps text that is already wider, and
	/// <see cref="PadType.Full"/> has nowhere to put the cells when the text has no word gap to
	/// widen. Cells that <paramref name="fill"/> cannot express, such as the single cell left over
	/// by a two-cell fill, are taken by spaces, so the width holds whatever the fill is.
	/// </remarks>
	public MarkupText Pad(MarkupText fill, int width, PadType type, TruncationType truncation)
	{
		ArgumentNullException.ThrowIfNull(fill);
		if (type == PadType.Full) return PadFull(fill, width, truncation);

		var cells = DisplayWidth;
		if (cells < width) return PadTo(this, fill, fill, width - cells, type);
		if (truncation == TruncationType.Overflow) return this;

		var cut = Substring(0, Cells.IndexAtWidth(Text, width));
		var deficit = width - cut.DisplayWidth;
		return deficit <= 0 ? cut : PadTo(cut, fill, fill, deficit, type);
	}

	/// <summary>
	/// Centres the text in <paramref name="width"/> display cells between two different fills,
	/// the odd cell going to the right.
	/// </summary>
	/// <remarks>
	/// The result is exactly <paramref name="width"/> display cells wide unless
	/// <see cref="TruncationType.Overflow"/> keeps text that is already wider. Cells neither fill
	/// can express are taken by spaces.
	/// </remarks>
	public MarkupText Center(MarkupText fillLeft, MarkupText fillRight, int width, TruncationType truncation)
	{
		ArgumentNullException.ThrowIfNull(fillLeft);
		ArgumentNullException.ThrowIfNull(fillRight);

		var cells = DisplayWidth;
		if (cells < width) return PadTo(this, fillLeft, fillRight, width - cells, PadType.Center);
		if (truncation == TruncationType.Overflow) return this;

		var cut = Substring(0, Cells.IndexAtWidth(Text, width));
		var deficit = width - cut.DisplayWidth;
		return deficit <= 0 ? cut : PadTo(cut, fillLeft, fillRight, deficit, PadType.Center);
	}

	/// <summary>This text repeated <paramref name="count"/> times.</summary>
	public MarkupText Repeat(int count)
	{
		if (count <= 0 || Length == 0) return Empty;
		if (count == 1) return this;
		var parts = new MarkupText[count];
		Array.Fill(parts, this);
		return Concat(parts.AsSpan());
	}

	/// <summary>Removes <paramref name="length"/> code units from <paramref name="index"/>.</summary>
	public MarkupText Remove(int index, int length)
	{
		if (length <= 0 || index >= Length) return this;
		var start = Math.Max(0, index);
		var count = Math.Min(length, Length - start);
		return count <= 0 ? this : Splice([new Edit(start, count, Empty)]);
	}

	/// <summary>
	/// Replaces <paramref name="length"/> code units at <paramref name="index"/>. Unlike
	/// <see cref="Insert"/> the replacement never inherits surrounding markup.
	/// </summary>
	public MarkupText Replace(int index, int length, MarkupText replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		if (index >= Length) return Concat(this, replacement);
		if (index < 0) return Concat(replacement, this);
		return Splice([new Edit(index, Math.Clamp(length, 0, Length - index), replacement)]);
	}

	/// <summary>
	/// Inserts at <paramref name="index"/>. An insertion strictly inside a run inherits that
	/// run's markup as its outer layers, so styled text stays unbroken.
	/// </summary>
	public MarkupText Insert(int index, MarkupText insert)
	{
		ArgumentNullException.ThrowIfNull(insert);
		if (insert.Length == 0) return this;
		if (index <= 0) return Concat(insert, this);
		if (index >= Length) return Concat(this, insert);

		var enclosing = EnclosingMarkups(index);
		return Splice([new Edit(index, 0, enclosing is null ? insert : WrapWith(insert, enclosing))]);
	}

	/// <summary>
	/// Applies every edit in one pass. Edits must be sorted by <see cref="Edit.Start"/> and must
	/// not overlap; each range snaps outward to grapheme cluster boundaries, and a zero-length
	/// edit (an insertion) snaps to the start of the cluster it lands in. Where snapping makes
	/// one range swallow the next, the later edit applies to what is left of its range.
	/// </summary>
	/// <exception cref="ArgumentException">The edits are unsorted or overlapping.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An edit falls outside the text.</exception>
	public MarkupText Splice(ReadOnlySpan<Edit> edits)
	{
		if (edits.Length == 0) return this;

		var text = new StringBuilder(Length);
		var runs = ImmutableArray.CreateBuilder<Run>();
		var position = 0;
		var previousEnd = 0;
		foreach (var edit in edits)
		{
			if (edit.Start < 0 || edit.Start > Length)
				throw new ArgumentOutOfRangeException(nameof(edits), edit.Start, "Edit starts outside the text.");
			if (edit.Length < 0 || edit.Length > Length - edit.Start)
				throw new ArgumentOutOfRangeException(nameof(edits), edit.Length, "Edit ends outside the text.");
			if (edit.Start < previousEnd)
				throw new ArgumentException("Edits must be sorted by start and must not overlap.", nameof(edits));
			previousEnd = edit.Start + edit.Length;

			var start = Math.Max(position, Graphemes.SnapStart(Text, edit.Start));
			var end = edit.Length == 0 ? start : Math.Max(start, Graphemes.SnapEnd(Text, edit.Start + edit.Length));

			ClipInto(position, start, text.Length - position, runs);
			text.Append(Text, position, start - position);

			var replacement = edit.Replacement ?? Empty;
			var offset = text.Length;
			text.Append(replacement.Text);
			foreach (var run in replacement.Runs) runs.Add(run with { Start = run.Start + offset });

			position = end;
		}

		ClipInto(position, Length, text.Length - position, runs);
		text.Append(Text, position, Length - position);
		return new MarkupText(text.ToString(), runs.ToImmutable());
	}

	/// <summary>
	/// Replaces every non-overlapping ordinal occurrence of <paramref name="search"/> in one pass,
	/// leaving the markup on the text between matches untouched.
	/// </summary>
	public MarkupText ReplaceAll(string search, MarkupText replacement)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentNullException.ThrowIfNull(replacement);
		if (search.Length == 0 || Length == 0) return this;

		List<Edit>? edits = null;
		var position = 0;
		while (position <= Length - search.Length)
		{
			var found = Text.IndexOf(search, position, StringComparison.Ordinal);
			if (found < 0) break;
			(edits ??= []).Add(new Edit(found, search.Length, replacement));
			position = found + search.Length;
		}
		return edits is null ? this : Splice(CollectionsMarshal.AsSpan(edits));
	}

	/// <summary>Ordinal index of the first occurrence of <paramref name="search"/>, or -1.</summary>
	public int IndexOf(string search) => Text.IndexOf(search, StringComparison.Ordinal);

	/// <summary>Ordinal index of the last occurrence of <paramref name="search"/>, or -1.</summary>
	public int LastIndexOf(string search) => Text.LastIndexOf(search, StringComparison.Ordinal);

	/// <summary>Ordinal indexes of every non-overlapping occurrence of <paramref name="search"/>.</summary>
	public IEnumerable<int> IndexesOf(string search)
	{
		if (search.Length == 0) yield break;
		var position = 0;
		while (position <= Length - search.Length)
		{
			var found = Text.IndexOf(search, position, StringComparison.Ordinal);
			if (found < 0) yield break;
			yield return found;
			position = found + search.Length;
		}
	}

	/// <summary>
	/// Transforms the whole plain text. A transform that keeps the length keeps the runs; any
	/// other result is plain, because the run positions no longer mean anything.
	/// </summary>
	public MarkupText Apply(Func<string, string> transform)
	{
		ArgumentNullException.ThrowIfNull(transform);
		var text = transform(Text);
		return text.Length == Length ? new MarkupText(text, Runs) : Plain(text);
	}

	/// <summary>
	/// Transforms each styled run and each plain gap separately, then concatenates the results.
	/// </summary>
	public MarkupText Map(Func<MarkupText, MarkupText> transform)
	{
		ArgumentNullException.ThrowIfNull(transform);
		if (Length == 0) return this;

		var segments = new List<MarkupText>(Runs.Length * 2 + 1);
		var position = 0;
		foreach (var run in Runs)
		{
			if (run.Start > position) segments.Add(transform(Substring(position, run.Start - position)));
			segments.Add(transform(Substring(run.Start, run.Length)));
			position = run.End;
		}
		if (position < Length) segments.Add(transform(Substring(position, Length - position)));
		return Concat(CollectionsMarshal.AsSpan(segments));
	}

	/// <summary>
	/// Appends <paramref name="tail"/>, extending the outermost markup of a run that reaches the
	/// end of this text over it. Text that ends plain gets a plain concatenation.
	/// </summary>
	public MarkupText AttachTail(MarkupText tail)
	{
		ArgumentNullException.ThrowIfNull(tail);
		if (tail.Length == 0) return this;
		if (Runs.Length == 0 || Runs[^1].End != Length) return Concat(this, tail);
		return Concat(this, Wrap(Runs[^1].Markups.Outermost, tail));
	}

	/// <summary>Distributes <paramref name="cells"/> of fill around <paramref name="body"/>.</summary>
	private static MarkupText PadTo(MarkupText body, MarkupText left, MarkupText right, int cells, PadType type) =>
		type switch
		{
			PadType.Left => Concat(BuildFill(left, cells), body),
			PadType.Right => Concat(body, BuildFill(right, cells)),
			PadType.Center => Concat([BuildFill(left, cells / 2), body, BuildFill(right, cells - cells / 2)]),
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported pad type."),
		};

	/// <summary>
	/// <paramref name="fill"/> repeated and cut to exactly <paramref name="cells"/> display cells.
	/// The fill's own markup survives; cells the fill cannot express, because its last cluster is
	/// wider than what is left to fill (or because it has no width at all), take spaces instead.
	/// </summary>
	private static MarkupText BuildFill(MarkupText fill, int cells)
	{
		if (cells <= 0) return Empty;
		var unit = fill.DisplayWidth;
		if (unit <= 0) return Space.Repeat(cells);
		var repeated = fill.Repeat(cells / unit + 1);
		var built = repeated.Substring(0, Cells.IndexAtWidth(repeated.Text, cells));
		var residue = cells - built.DisplayWidth;
		return residue <= 0 ? built : Concat(built, Space.Repeat(residue));
	}

	/// <summary>Widens the gaps between space-separated words instead of appending fill.</summary>
	private MarkupText PadFull(MarkupText fill, int width, TruncationType truncation)
	{
		var cells = DisplayWidth;
		if (cells >= width)
		{
			if (truncation == TruncationType.Overflow) return this;
			var cut = Substring(0, Cells.IndexAtWidth(Text, width));
			var deficit = width - cut.DisplayWidth;
			return deficit <= 0 ? cut : PadTo(cut, fill, fill, deficit, PadType.Right);
		}

		var words = Split(" ");
		var fences = words.Length - 1;
		if (fences <= 0) return this;

		var totalSpaces = fences + (width - cells);
		var thin = Space.Repeat(totalSpaces / fences);
		var thick = Space.Repeat(totalSpaces / fences + 1);
		var thickCount = totalSpaces % fences;

		var parts = new List<MarkupText>(words.Length * 2 - 1);
		for (var i = 0; i < words.Length; i++)
		{
			if (i > 0) parts.Add(i <= thickCount ? thick : thin);
			parts.Add(words[i]);
		}
		return Concat(CollectionsMarshal.AsSpan(parts));
	}

	/// <summary>The markup of the run that strictly contains <paramref name="index"/>, if any.</summary>
	private MarkupSet? EnclosingMarkups(int index)
	{
		var i = FirstRunIndexAt(index);
		if (i >= Runs.Length) return null;
		var run = Runs[i];
		return run.Start < index && run.End > index ? run.Markups : null;
	}

	/// <summary>
	/// Copies the runs overlapping <c>[from, to)</c> into <paramref name="builder"/>, clipped to
	/// that range and shifted by <paramref name="offset"/>.
	/// </summary>
	private void ClipInto(int from, int to, int offset, ImmutableArray<Run>.Builder builder)
	{
		if (to <= from) return;
		for (var i = FirstRunIndexAt(from); i < Runs.Length; i++)
		{
			var run = Runs[i];
			if (run.Start >= to) break;
			var start = Math.Max(run.Start, from);
			var end = Math.Min(run.End, to);
			if (end > start) builder.Add(new Run(start + offset, end - start, run.Markups));
		}
	}

	/// <summary>
	/// Index of the last run starting at or before <paramref name="position"/>, or 0 — the first
	/// index a scan for runs overlapping <paramref name="position"/> has to start from.
	/// </summary>
	private int FirstRunIndexAt(int position)
	{
		var low = 0;
		var high = Runs.Length - 1;
		var result = 0;
		while (low <= high)
		{
			var mid = (low + high) >> 1;
			if (Runs[mid].Start <= position)
			{
				result = mid;
				low = mid + 1;
			}
			else
			{
				high = mid - 1;
			}
		}
		return result;
	}

	/// <summary>Layers <paramref name="outer"/> over every run and gap of <paramref name="inner"/>.</summary>
	private static MarkupText WrapWith(MarkupText inner, MarkupSet outer)
	{
		var builder = ImmutableArray.CreateBuilder<Run>(inner.Runs.Length * 2 + 1);
		var position = 0;
		foreach (var run in inner.Runs)
		{
			if (run.Start > position) builder.Add(new Run(position, run.Start - position, outer));
			builder.Add(new Run(run.Start, run.Length, Layer(run.Markups, outer)));
			position = run.End;
		}
		if (position < inner.Length) builder.Add(new Run(position, inner.Length - position, outer));
		return new MarkupText(inner.Text, builder.ToImmutable());
	}

	private static MarkupSet Layer(MarkupSet inner, MarkupSet outer)
	{
		var result = inner;
		foreach (var markup in outer) result = result.Append(markup);
		return result;
	}
}
