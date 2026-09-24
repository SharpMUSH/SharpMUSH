using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// The read-only list operations — <c>words</c>, <c>first</c>, <c>rest</c>, <c>extract</c> — over one
/// shared, interruptible scan (#977).
/// <para>
/// <see cref="MushText.SplitList"/> materialises one <see cref="MarkupText"/> per segment, so an input near the
/// output ceiling made of alternating delimiters costs millions of objects before any budget checkpoint
/// runs. These operations only ever need a count, or the segments they return, so the scan walks the plain
/// text once, allocates nothing for a segment it does not return, and checks the ambient
/// <see cref="ExecutionBudget"/> as it goes.
/// </para>
/// <para>
/// The results are those of <c>SplitList</c> followed by <c>Skip</c>/<c>Take</c>/<c>Join</c>. Segments are
/// the ordinal delimiter occurrences <see cref="MarkupText.Split(string)"/> finds, cut with
/// <see cref="MarkupText.Substring(int, int)"/>, and a single space drops empty segments. Where that
/// equivalence is not obvious — the text or the delimiter carries markup, or a delimiter sits against a
/// grapheme cluster — the returned segments are cut and joined one at a time exactly as before, still
/// without touching the ones that are skipped.
/// </para>
/// </summary>
public static class MushList
{
	/// <summary>Segments or characters scanned between two budget checks.</summary>
	private const int CheckpointInterval = 1 << 14;

	/// <summary>The number of list items: what <c>SplitList(...).Length</c> was.</summary>
	public static int Count(MarkupText delimiter, MarkupText text)
	{
		var scan = new Scan(delimiter, text);
		var count = 0;
		while (scan.Next(out _, out _)) count++;
		return count;
	}

	/// <summary>The first item, or empty when there is none.</summary>
	public static MarkupText First(MarkupText delimiter, MarkupText text)
	{
		var scan = new Scan(delimiter, text);
		return scan.Next(out var start, out var end) ? text.Substring(start, end - start) : MarkupText.Empty;
	}

	/// <summary>Every item after the first, joined with <paramref name="delimiter"/>.</summary>
	public static MarkupText Rest(MarkupText delimiter, MarkupText text)
		=> Range(delimiter, text, 1, long.MaxValue);

	/// <summary>
	/// <c>extract()</c>'s selection: a positive <paramref name="first"/> is a 1-based position and a
	/// non-positive one counts from the end; a positive <paramref name="length"/> takes that many items and a
	/// non-positive one takes that many from the end of what remains.
	/// </summary>
	public static MarkupText Extract(MarkupText delimiter, MarkupText text, int first, int length)
	{
		var total = first > 0 && length > 0 ? long.MaxValue : Count(delimiter, text);
		var from = first > 0 ? first - 1L : Math.Max(0, total - Math.Abs((long)first));
		if (from >= total) return Join(delimiter, []);

		var until = total;
		if (length > 0) until = from + length < total ? from + length : total;
		else from = Math.Max(from, total - Math.Abs((long)length));

		return Range(delimiter, text, from, until);
	}

	/// <summary>The items at positions <paramref name="from"/> up to (not including) <paramref name="until"/>.</summary>
	private static MarkupText Range(MarkupText delimiter, MarkupText text, long from, long until)
	{
		var scan = new Scan(delimiter, text);
		long index = 0;
		int start = 0, end = 0;
		var any = false;
		while (index < until && scan.Next(out var segmentStart, out var segmentEnd))
		{
			if (index++ < from) continue;
			if (!any) start = segmentStart;
			end = segmentEnd;
			any = true;
		}

		if (!any) return Join(delimiter, []);
		if (scan.CanSlice(start, end)) return scan.Slice(start, end);

		// Cut and joined one item at a time, as SplitList and Join did.
		var items = new List<MarkupText>();
		scan = new Scan(delimiter, text);
		index = 0;
		while (index < until && scan.Next(out var segmentStart, out var segmentEnd))
		{
			if (index++ >= from) items.Add(text.Substring(segmentStart, segmentEnd - segmentStart));
		}

		return Join(delimiter, items);
	}

	private static MarkupText Join(MarkupText delimiter, IEnumerable<MarkupText> items) => MarkupText.Join(delimiter, items);

	/// <summary>One left-to-right pass over the raw segments <c>SplitList</c> would produce.</summary>
	private struct Scan
	{
		private readonly MarkupText _source;
		private readonly string _text;
		private readonly string _delimiter;
		private readonly bool _dropEmpty;
		private readonly bool _plain;
		private int _position;
		private bool _finished;
		private int _work;

		public Scan(MarkupText delimiter, MarkupText text)
		{
			ArgumentNullException.ThrowIfNull(delimiter);
			ArgumentNullException.ThrowIfNull(text);
			_source = text;
			_text = text.Text;
			_delimiter = delimiter.Text;
			_dropEmpty = _delimiter == " ";
			_finished = _text.Length == 0;
			// The single-slice fast path returns the text as it stands, so it needs no markup to keep
			// or to replace in the output.
			_plain = delimiter.Equals(MarkupText.Plain(_delimiter)) && text.Equals(MarkupText.Plain(_text));
			_position = 0;
			_work = 0;
		}

		/// <summary>The next raw segment, skipping the empty ones for a single-space delimiter.</summary>
		public bool Next(out int start, out int end)
		{
			while (!_finished)
			{
				Checkpoint();
				start = _position;
				var gap = _delimiter.Length == 0 ? -1 : _text.AsSpan(_position).IndexOf(_delimiter, StringComparison.Ordinal);
				if (gap < 0)
				{
					end = _text.Length;
					_position = _text.Length;
					_finished = true;
				}
				else
				{
					end = _position + gap;
					_position = end + _delimiter.Length;
				}

				_work += end - start;
				if (!_dropEmpty || !IsEmptyAfterCutting(start, end)) return true;
			}

			start = end = 0;
			return false;
		}

		/// <summary>
		/// Whether <c>Substring</c> would return nothing for <c>[start, end)</c>. A cut inside a grapheme
		/// cluster moves inward, so it can swallow a segment that is not raw-empty.
		/// </summary>
		private readonly bool IsEmptyAfterCutting(int start, int end)
			=> start == end || (!IsBoundary(start) || !IsBoundary(end)) && _source.Substring(start, end - start).Length == 0;

		private readonly bool IsBoundary(int index) => Graphemes.IsBoundary(_text, index);

		private void Checkpoint()
		{
			if (++_work < CheckpointInterval) return;
			_work = 0;
			ExecutionBudget.Current?.ThrowIfExceeded();
		}

		/// <summary>
		/// Whether the raw run <c>[start, end)</c> is exactly the items' join: nothing to keep or replace in
		/// the markup, and every cut lands between clusters so each item survives <c>Substring</c> whole.
		/// </summary>
		public bool CanSlice(int start, int end)
			=> _plain
				&& IsBoundary(start) && IsBoundary(end)
				&& !HasHazardousDelimiterIn(start, end);

		private bool HasHazardousDelimiterIn(int start, int end)
		{
			// Only a delimiter that touches a cluster can make a cut land inside one, and only a
			// character that can extend a cluster makes a delimiter touch one.
			if (_delimiter.Length == 0) return false;
			var window = _text.AsSpan(start, end - start);
			var offset = start;
			while (true)
			{
				var found = window.IndexOf(_delimiter, StringComparison.Ordinal);
				if (found < 0) return false;
				Checkpoint();
				var at = offset + found;
				if (!IsBoundary(at) || !IsBoundary(at + _delimiter.Length)) return true;
				var next = found + _delimiter.Length;
				window = window[next..];
				offset += next;
			}
		}

		/// <summary>The run as one piece, with the space runs a single-space delimiter drops collapsed.</summary>
		public readonly MarkupText Slice(int start, int end)
		{
			var piece = _source.Substring(start, end - start);
			return _dropEmpty ? MushText.CompressSpaces(piece) : piece;
		}
	}
}
