namespace SharpMUSH.Library.Services;

/// <summary>A region where both sides changed the same base lines differently.</summary>
/// <param name="Base">The base's lines in the region.</param>
/// <param name="Ours">The game's lines (the live page).</param>
/// <param name="Theirs">The package's lines (the new version).</param>
public sealed record LineMergeConflict(IReadOnlyList<string> Base, IReadOnlyList<string> Ours, IReadOnlyList<string> Theirs);

/// <summary>
/// A three-way merge, as a list of regions: each either settled (the lines every side agrees on, or the one side
/// that changed them) or a <see cref="LineMergeConflict"/>.
/// </summary>
public sealed class LineMergeResult
{
	private readonly IReadOnlyList<object> _regions;

	internal LineMergeResult(IReadOnlyList<object> regions) => _regions = regions;

	/// <summary>Each clash, in order.</summary>
	public IReadOnlyList<LineMergeConflict> Conflicts => _regions.OfType<LineMergeConflict>().ToList();

	/// <summary>True when the two sides changed no region differently.</summary>
	public bool IsClean => !_regions.OfType<LineMergeConflict>().Any();

	/// <summary>The merged text with each clash settled for the game (<paramref name="preferOurs"/>) or the package.</summary>
	public string Resolve(bool preferOurs)
		=> Join(_regions.SelectMany(region => region switch
		{
			LineMergeConflict conflict => preferOurs ? conflict.Ours : conflict.Theirs,
			IReadOnlyList<string> lines => lines,
			_ => []
		}));

	/// <summary>
	/// The merged text with each clash written out between markers, the game's lines first, as an edited merge
	/// starts from: <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt; <paramref name="ours"/></c>, <c>=======</c>,
	/// <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt; <paramref name="theirs"/></c>.
	/// </summary>
	public string Marked(string ours, string theirs)
		=> Join(_regions.SelectMany(region => region switch
		{
			LineMergeConflict conflict =>
				[$"<<<<<<< {ours}", .. conflict.Ours, "=======", .. conflict.Theirs, $">>>>>>> {theirs}"],
			IReadOnlyList<string> lines => lines,
			_ => []
		}));

	private static string Join(IEnumerable<string> lines) => string.Join('\n', lines);
}

/// <summary>
/// A line-based diff3 (Khanna, Kunal and Pierce): each side is diffed against the base by longest common
/// subsequence, the lines all three share are the anchors, and between two anchors a region one side left as
/// the base takes the other side's lines. A region both sides changed, to different lines, is a clash.
/// </summary>
/// <remarks>
/// Lines are split on <c>\n</c>, so a trailing line end is an empty last line like any other. The common prefix
/// and suffix are matched without a table; the rest uses the classic dynamic-programming LCS, and a middle too
/// large for it (<see cref="MaxCells"/>) is taken as changed as a whole, which is never wrong, only coarse.
/// </remarks>
public static class LineMerge
{
	/// <summary>The largest LCS table (lines of base times lines of a side, after the shared ends) computed.</summary>
	public const long MaxCells = 4_000_000;

	/// <summary>Merges <paramref name="ours"/> and <paramref name="theirs"/>, both descended from <paramref name="baseText"/>.</summary>
	public static LineMergeResult Merge(string baseText, string ours, string theirs)
	{
		var o = Split(baseText);
		var a = Split(ours);
		var b = Split(theirs);
		var toA = Match(o, a);
		var toB = Match(o, b);

		var regions = new List<object>();
		var stable = new List<string>();
		int io = 0, ia = 0, ib = 0;
		while (io < o.Length || ia < a.Length || ib < b.Length)
		{
			// Lines all three sides share, in step.
			if (io < o.Length && toA[io] == ia && toB[io] == ib)
			{
				stable.Add(o[io]);
				io++;
				ia++;
				ib++;
				continue;
			}

			if (stable.Count > 0)
			{
				regions.Add(stable.ToArray());
				stable.Clear();
			}

			// The next base line both sides kept closes the region.
			var next = io;
			while (next < o.Length && (toA[next] < 0 || toB[next] < 0))
			{
				next++;
			}

			var endA = next < o.Length ? toA[next] : a.Length;
			var endB = next < o.Length ? toB[next] : b.Length;
			var baseLines = o[io..next];
			var aLines = a[ia..endA];
			var bLines = b[ib..endB];

			if (aLines.SequenceEqual(baseLines, StringComparer.Ordinal))
			{
				regions.Add(bLines);
			}
			else if (bLines.SequenceEqual(baseLines, StringComparer.Ordinal) || aLines.SequenceEqual(bLines, StringComparer.Ordinal))
			{
				regions.Add(aLines);
			}
			else
			{
				regions.Add(new LineMergeConflict(baseLines, aLines, bLines));
			}

			io = next;
			ia = endA;
			ib = endB;
		}

		if (stable.Count > 0)
		{
			regions.Add(stable.ToArray());
		}

		return new LineMergeResult(regions);
	}

	private static string[] Split(string text) => text.Replace("\r\n", "\n").Split('\n');

	/// <summary>For each line of <paramref name="from"/>, the index of the line of <paramref name="to"/> it is matched with, or -1.</summary>
	private static int[] Match(string[] from, string[] to)
	{
		var map = new int[from.Length];
		Array.Fill(map, -1);

		var prefix = 0;
		while (prefix < from.Length && prefix < to.Length && from[prefix] == to[prefix])
		{
			map[prefix] = prefix;
			prefix++;
		}

		var suffix = 0;
		while (suffix < from.Length - prefix && suffix < to.Length - prefix
			&& from[from.Length - 1 - suffix] == to[to.Length - 1 - suffix])
		{
			map[from.Length - 1 - suffix] = to.Length - 1 - suffix;
			suffix++;
		}

		var n = from.Length - prefix - suffix;
		var m = to.Length - prefix - suffix;
		if (n == 0 || m == 0 || (long)n * m > MaxCells)
		{
			return map;
		}

		// lengths[i, j]: the LCS of from[prefix + i..] and to[prefix + j..], in the middle.
		var lengths = new int[(n + 1) * (m + 1)];
		for (var i = n - 1; i >= 0; i--)
		{
			for (var j = m - 1; j >= 0; j--)
			{
				lengths[i * (m + 1) + j] = from[prefix + i] == to[prefix + j]
					? lengths[(i + 1) * (m + 1) + j + 1] + 1
					: Math.Max(lengths[(i + 1) * (m + 1) + j], lengths[i * (m + 1) + j + 1]);
			}
		}

		for (int i = 0, j = 0; i < n && j < m;)
		{
			if (from[prefix + i] == to[prefix + j])
			{
				map[prefix + i] = prefix + j;
				i++;
				j++;
			}
			else if (lengths[(i + 1) * (m + 1) + j] >= lengths[i * (m + 1) + j + 1])
			{
				i++;
			}
			else
			{
				j++;
			}
		}

		return map;
	}
}
