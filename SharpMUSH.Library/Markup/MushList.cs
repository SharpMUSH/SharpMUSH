using System.Runtime.InteropServices;
using MarkupString;
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
/// <see cref="MarkupText.Substring(int, int)"/>, and a single space drops empty segments. A range of items
/// is one slice of the text with the gaps that differ from the delimiter — in length or in markup —
/// spliced over. Where a delimiter sits against a grapheme cluster, so a slice would cut differently, the
/// returned segments are cut and joined one at a time exactly as before, still without touching the ones
/// that are skipped.
/// </para>
/// </summary>
public static class MushList
{
	/// <summary>Segments or characters scanned between two budget checks.</summary>
	private const int CheckpointInterval = 1 << 14;

	/// <summary>The most items, or gap edits, held at once while a range is put together.</summary>
	private const int PieceSize = 1 << 12;

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
		return scan.CanSlice(start, end)
			? Spliced(delimiter, text, from, until)
			: CutOneByOne(delimiter, text, from, until);
	}

	/// <summary>
	/// The run of items as one slice of the text, with each gap between two items that is not already
	/// exactly the delimiter replaced by it: a run of spaces a single-space delimiter drops, a gap with
	/// markup on it, or any gap when the delimiter has markup of its own. That is what joining the items
	/// gives, at the cost of the answer and one edit per replaced gap, so a long list with markup costs no
	/// more than a plain one with the same number of marked gaps. The edits are applied a piece at a time
	/// so no more than one piece's worth are held at once.
	/// </summary>
	private static MarkupText Spliced(MarkupText delimiter, MarkupText text, long from, long until)
	{
		var runs = text.Runs;
		var run = 0;
		var pieces = new List<MarkupText>();
		var edits = new List<Edit>();
		var scan = new Scan(delimiter, text);
		long index = 0;
		int pieceStart = -1, previousEnd = 0;
		while (index < until && scan.Next(out var segmentStart, out var segmentEnd))
		{
			if (index++ < from) continue;
			if (pieceStart < 0) pieceStart = segmentStart;
			else if (NeedsReplacing(previousEnd, segmentStart))
			{
				if (edits.Count < PieceSize)
					edits.Add(new Edit(previousEnd - pieceStart, segmentStart - previousEnd, delimiter));
				else
				{
					// Joining the pieces puts the delimiter in this gap.
					pieces.Add(Piece(pieceStart, previousEnd));
					pieceStart = segmentStart;
				}
			}

			previousEnd = segmentEnd;
		}

		pieces.Add(Piece(pieceStart, previousEnd));
		return pieces.Count == 1 ? pieces[0] : Join(delimiter, pieces);

		bool NeedsReplacing(int gapStart, int gapEnd)
		{
			if (gapEnd - gapStart != delimiter.Length || delimiter.Runs.Length > 0) return true;
			while (run < runs.Length && runs[run].End <= gapStart) run++;
			return run < runs.Length && runs[run].Start < gapEnd;
		}

		MarkupText Piece(int start, int end)
		{
			var piece = text.Substring(start, end - start).Splice(CollectionsMarshal.AsSpan(edits));
			edits.Clear();
			return piece;
		}
	}

	/// <summary>
	/// Each item cut on its own and the items joined, as <c>SplitList</c> and <c>Join</c> did, for a run
	/// whose cuts a slice cannot reproduce. They are joined a piece at a time, so no more than one piece's
	/// worth of items are held at once.
	/// </summary>
	private static MarkupText CutOneByOne(MarkupText delimiter, MarkupText text, long from, long until)
	{
		var pieces = new List<MarkupText>();
		var items = new List<MarkupText>();
		var scan = new Scan(delimiter, text);
		long index = 0;
		while (index < until && scan.Next(out var segmentStart, out var segmentEnd))
		{
			if (index++ < from) continue;
			items.Add(text.Substring(segmentStart, segmentEnd - segmentStart));
			if (items.Count < PieceSize) continue;
			pieces.Add(Join(delimiter, items));
			items.Clear();
		}

		if (items.Count > 0) pieces.Add(Join(delimiter, items));
		return Join(delimiter, pieces);
	}

	private static MarkupText Join(MarkupText delimiter, IEnumerable<MarkupText> items) => MarkupText.Join(delimiter, items);

	/// <summary>One left-to-right pass over the raw segments <c>SplitList</c> would produce.</summary>
	private struct Scan
	{
		private readonly MarkupText _source;
		private readonly string _text;
		private readonly string _delimiter;
		private readonly bool _dropEmpty;
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
				var at = _delimiter.Length == 0 ? -1 : IndexOfDelimiter(_position, _text.Length);
				if (at < 0)
				{
					end = _text.Length;
					_position = _text.Length;
					_finished = true;
				}
				else
				{
					end = at;
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
		/// The first delimiter in <c>[from, to)</c>, or -1. The search goes a checkpoint's worth of text at a
		/// time, so one long item cannot outrun the budget.
		/// </summary>
		private readonly int IndexOfDelimiter(int from, int to)
		{
			while (true)
			{
				var window = Math.Min(to - from, CheckpointInterval + _delimiter.Length - 1);
				var found = _text.AsSpan(from, window).IndexOf(_delimiter, StringComparison.Ordinal);
				if (found >= 0) return from + found;
				if (from + window >= to) return -1;
				from += CheckpointInterval;
				ExecutionBudget.Current?.ThrowIfExceeded();
			}
		}

		/// <summary>
		/// Whether the raw run <c>[start, end)</c> can be sliced out whole: every cut lands between clusters,
		/// so each item survives <c>Substring</c> exactly as it would on its own.
		/// </summary>
		public bool CanSlice(int start, int end)
			=> IsBoundary(start) && IsBoundary(end) && !HasHazardousDelimiterIn(start, end);

		private bool HasHazardousDelimiterIn(int start, int end)
		{
			// Only a delimiter that touches a cluster can make a cut land inside one, and only a
			// character that can extend a cluster makes a delimiter touch one.
			if (_delimiter.Length == 0) return false;
			var position = start;
			while (true)
			{
				var at = IndexOfDelimiter(position, end);
				if (at < 0) return false;
				Checkpoint();
				if (!IsBoundary(at) || !IsBoundary(at + _delimiter.Length)) return true;
				position = at + _delimiter.Length;
			}
		}
	}
}
