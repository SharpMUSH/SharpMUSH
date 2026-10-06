using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	/// <summary>The wiki's revision streams as an <see cref="IHistoryStore"/>.</summary>
	public IHistoryStore WikiHistory => field ??= new WikiRevisionHistory(Store);

	/// <summary>
	/// Retention over <see cref="Tables.WikiRev"/>. A stream is one <c>(page, locale)</c>: the source text
	/// and each translation number their revisions separately, and each is bounded on its own.
	/// </summary>
	/// <remarks>
	/// <para>What a pass never removes: a stream's latest revision (it is the text the page or translation
	/// shows), and anything on a protected page — protection is how an admin says a page's history is
	/// not to be touched.</para>
	/// <para>A purged revision is gone from the live world: <c>GET …/revisions/{n}</c> answers 404 for it
	/// and a rollback to it is refused, while every revision that survives keeps its number — numbering
	/// continues from the page row, never from the history, so a gap never renumbers anything.</para>
	/// </remarks>
	private sealed class WikiRevisionHistory(LightningStore store) : IHistoryStore
	{
		/// <summary>Rows the read covers before a batch is cut at the next stream boundary.</summary>
		private const int ScanBudgetPerCandidate = 16;

		public string Kind => "wiki";

		public string Description => "wiki page and translation revisions";

		public ValueTask<HistoryUsage> MeasureAsync(CancellationToken ct = default)
			=> ValueTask.FromResult(store.Read(tx =>
			{
				long streams = 0, records = 0, bytes = 0;
				var last = ReadOnlyMemory<byte>.Empty;
				foreach (var (key, value) in tx.Range(Tables.WikiRev, []))
				{
					ct.ThrowIfCancellationRequested();
					var stream = StreamOf(key);
					if (!stream.Span.SequenceEqual(last.Span)) streams++;
					last = stream;
					records++;
					bytes += key.Length + value.Length;
				}

				return new HistoryUsage(Kind, Description, streams, records, bytes);
			}));

		public ValueTask<HistoryPurgeBatch> FindPurgeableAsync(HistoryRetentionRule rule, DateTimeOffset now,
			byte[] resumeFrom, int limit, CancellationToken ct = default)
			=> ValueTask.FromResult(store.Read(tx => HistoryScan.Collect(
				resumeFrom.Length == 0 ? tx.Range(Tables.WikiRev, []) : tx.RangeFromKey(Tables.WikiRev, resumeFrom),
				StreamOf,
				stream => Purgeable(tx, stream, rule, now)
					.Select(row => new HistoryPurgeCandidate(row.Key, row.Key.Length + row.Value.Length, row.Value)),
				limit,
				limit * ScanBudgetPerCandidate)));

		public async ValueTask<(int Records, long Bytes)> PurgeAsync(HistoryPurgeBatch batch, HistoryRetentionRule rule,
			DateTimeOffset now, CancellationToken ct = default)
			=> await store.WriteAsync(tx =>
			{
				var deleted = 0;
				var freed = 0L;
				// Judged again inside the write: an edit since the read made a new latest revision, and a
				// protect since then exempts the whole page.
				foreach (var group in batch.Candidates.GroupBy(c => Convert.ToHexString(StreamOf(c.Key).Span)))
				{
					var prefix = StreamOf(group.First().Key).ToArray();
					var chosen = group.Select(c => Convert.ToHexString(c.Key)).ToHashSet(StringComparer.Ordinal);
					var stillPurgeable = Purgeable(tx, tx.Range(Tables.WikiRev, prefix).ToList(), rule, now)
						.Where(row => chosen.Contains(Convert.ToHexString(row.Key)))
						.ToList();
					foreach (var (key, value) in stillPurgeable)
					{
						tx.Delete(Tables.WikiRev, key);
						deleted++;
						freed += key.Length + value.Length;
					}
				}

				return (deleted, freed);
			}, ct);

		/// <summary>The key without its trailing revision number: the <c>(page, locale)</c> stream.</summary>
		private static ReadOnlyMemory<byte> StreamOf(byte[] key) => key.AsMemory(0, key.Length - sizeof(uint));

		private static IEnumerable<(byte[] Key, byte[] Value)> Purgeable(ITx tx,
			IReadOnlyList<(byte[] Key, byte[] Value)> stream, HistoryRetentionRule rule, DateTimeOffset now)
		{
			if (stream.Count == 0) return [];

			var pageId = Codec.Deserialize<WikiRevisionRecord>(stream[0].Value).PageId;
			if (WikiPageHasRequirements(tx, pageId)) return [];

			var latest = stream.Count - 1;
			return HistoryScan.Purgeable(
				stream.Select((row, index) => (row.Key, row.Value, Index: index)).ToList(),
				row =>
				{
					var stamp = Codec.Deserialize<WikiRevisionRecord>(row.Value).Timestamp;
					// A stamp that does not parse is treated as written just now: a revision whose age is
					// unknown is never old enough to purge by age.
					var parsed = ParseWikiTimestamp(stamp);
					var at = parsed == default ? now : parsed;
					return (at, row.Index == latest);
				},
				rule, now)
				.Select(row => (row.Key, row.Value));
		}
	}
}
