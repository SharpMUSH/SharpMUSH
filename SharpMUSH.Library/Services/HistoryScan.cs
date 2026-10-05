using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The scan every key-ordered history store shares: rows arrive in key order, the rows of one stream
/// (one page's locale, one pose's edit log) are contiguous because the stream's id leads the key, and a
/// batch is cut only at a stream boundary — or, when one stream alone has more to purge than the batch
/// holds, part-way through it, resuming at that stream's start next time. Rescanning the stream then is
/// correct because what the batch took is gone by the time the next one reads.
/// </summary>
public static class HistoryScan
{
	/// <summary>
	/// Collects up to <paramref name="limit"/> candidates from <paramref name="rows"/>, reading no more than
	/// roughly <paramref name="scanBudget"/> rows (a stream is always read to its end).
	/// </summary>
	/// <param name="rows">The store's rows in key order, from the resume point.</param>
	/// <param name="streamOf">The stream a key belongs to — the key's prefix that every row of the stream
	/// shares.</param>
	/// <param name="judge">What in one whole stream may be purged, oldest first.</param>
	/// <param name="limit">The most candidates the batch takes.</param>
	/// <param name="scanBudget">The rows the read may cover before the batch is cut at the next stream
	/// boundary, so a pass over a history with little to purge still holds no read transaction for long.</param>
	public static HistoryPurgeBatch Collect(
		IEnumerable<(byte[] Key, byte[] Value)> rows,
		Func<byte[], ReadOnlyMemory<byte>> streamOf,
		Func<IReadOnlyList<(byte[] Key, byte[] Value)>, IEnumerable<HistoryPurgeCandidate>> judge,
		int limit,
		int scanBudget)
	{
		var candidates = new List<HistoryPurgeCandidate>();
		var stream = new List<(byte[] Key, byte[] Value)>();
		var streamId = ReadOnlyMemory<byte>.Empty;
		var scanned = 0;

		foreach (var row in rows)
		{
			var id = streamOf(row.Key);
			if (stream.Count > 0 && !id.Span.SequenceEqual(streamId.Span))
			{
				if (Take(stream) is { Length: > 0 } cutInside) return new HistoryPurgeBatch(candidates, cutInside);
				if (candidates.Count >= limit || scanned >= scanBudget) return new HistoryPurgeBatch(candidates, row.Key);
				stream.Clear();
			}

			streamId = id;
			stream.Add(row);
			scanned++;
		}

		return stream.Count > 0 && Take(stream) is { Length: > 0 } cutInLast
			? new HistoryPurgeBatch(candidates, cutInLast)
			: new HistoryPurgeBatch(candidates, []);

		// Adds the stream's purgeable rows; when they do not all fit, the ones that do and the stream's
		// first key to resume at.
		byte[] Take(List<(byte[] Key, byte[] Value)> whole)
		{
			var purgeable = judge(whole).ToList();
			var room = limit - candidates.Count;
			if (purgeable.Count <= room)
			{
				candidates.AddRange(purgeable);
				return [];
			}

			candidates.AddRange(purgeable.Take(room));
			return whole[0].Key;
		}
	}

	/// <summary>
	/// The rows of one stream (oldest first) that <paramref name="rule"/> allows purging, oldest first:
	/// each is ranked by how many newer versions the stream holds.
	/// </summary>
	public static IEnumerable<T> Purgeable<T>(IReadOnlyList<T> oldestFirst, Func<T, (DateTimeOffset At, bool Pinned)> describe,
		HistoryRetentionRule rule, DateTimeOffset now)
	{
		for (var i = 0; i < oldestFirst.Count; i++)
		{
			var (at, pinned) = describe(oldestFirst[i]);
			if (rule.IsPurgeable(oldestFirst.Count - 1 - i, at, pinned, now)) yield return oldestFirst[i];
		}
	}
}
