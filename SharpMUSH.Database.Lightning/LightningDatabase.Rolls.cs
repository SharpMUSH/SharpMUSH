using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IRollStore"/> in <see cref="Tables.Roll"/>, keyed by id. Ids come from the
/// <c>next_roll</c> counter rather than the table's last key, so a roll purged by retention never has
/// its id handed to another and a claim of "roll 12" can only ever mean one roll.
/// </summary>
public partial class LightningDatabase : IRollStore
{
	/// <summary>Recorded rolls' retention, as an <see cref="IHistoryStore"/> of kind <c>rolls</c>.</summary>
	public IHistoryStore RollHistory => field ??= new RollRetention(Store);

	public ValueTask<RecordedRoll> RecordRollAsync(RollDraft draft, CancellationToken cancellationToken = default)
	{
		var record = new RollRecord
		{
			AtMs = draft.At.ToUnixTimeMilliseconds(),
			RollerObjid = draft.RollerObjid,
			RollerName = draft.RollerName,
			ObjectObjid = draft.ObjectObjid,
			Formula = draft.Formula,
			Total = draft.Total,
			Text = draft.Text,
			Json = draft.Json
		};

		return Store.WriteAsync(tx =>
		{
			var id = AllocateRollId(tx);
			tx.Put(Tables.Roll, Keys.Dbref(id), Codec.Serialize(record));
			return new RecordedRoll(id, draft);
		}, cancellationToken);
	}

	public ValueTask<Found<RecordedRoll>> GetRollAsync(long id, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<RecordedRoll>>(tx =>
			id >= 0 && tx.TryGet(Tables.Roll, Keys.Dbref(id), out var value)
				? ToRecordedRoll(id, Codec.Deserialize<RollRecord>(value))
				: new NotFound()));

	private static long AllocateRollId(ITx tx)
	{
		// Ids start at 1, so "roll 0" is never a real one.
		var next = tx.TryGet(Tables.Meta, Keys.Str("next_roll"), out var v) ? Keys.ReadDbref(v) : 1;
		tx.Put(Tables.Meta, Keys.Str("next_roll"), Keys.Dbref(next + 1));
		return next;
	}

	private static RecordedRoll ToRecordedRoll(long id, RollRecord record)
		=> new(id, new RollDraft(DateTimeOffset.FromUnixTimeMilliseconds(record.AtMs), record.RollerObjid,
			record.RollerName, record.ObjectObjid, record.Formula, record.Total, record.Text, record.Json));

	/// <summary>
	/// Retention over <see cref="Tables.Roll"/>: by age only, like the audit log. Ids grow with time, so
	/// everything before the first roll young enough to keep is purgeable.
	/// </summary>
	private sealed class RollRetention(LightningStore store) : IHistoryStore
	{
		public string Kind => "rolls";

		public string Description => "recorded rolls (@roll, roll() with record)";

		public ValueTask<HistoryUsage> MeasureAsync(CancellationToken ct = default)
			=> ValueTask.FromResult(store.Read(tx =>
			{
				long records = 0, bytes = 0;
				foreach (var (key, value) in tx.Range(Tables.Roll, []))
				{
					ct.ThrowIfCancellationRequested();
					records++;
					bytes += key.Length + value.Length;
				}

				return new HistoryUsage(Kind, Description, records, records, bytes);
			}));

		public ValueTask<HistoryPurgeBatch> FindPurgeableAsync(HistoryRetentionRule rule, DateTimeOffset now,
			byte[] resumeFrom, int limit, CancellationToken ct = default)
		{
			if (rule.MaxAge is not { } age) return ValueTask.FromResult(new HistoryPurgeBatch([], []));
			var cutoff = (now - age).ToUnixTimeMilliseconds();

			return ValueTask.FromResult(store.Read(tx =>
			{
				var candidates = new List<HistoryPurgeCandidate>();
				var rows = resumeFrom.Length == 0 ? tx.Range(Tables.Roll, []) : tx.RangeFromKey(Tables.Roll, resumeFrom);
				foreach (var (key, value) in rows)
				{
					if (Codec.Deserialize<RollRecord>(value).AtMs > cutoff) break;
					if (candidates.Count == limit) return new HistoryPurgeBatch(candidates, key);
					candidates.Add(new HistoryPurgeCandidate(key, key.Length + value.Length, value));
				}

				return new HistoryPurgeBatch(candidates, []);
			}));
		}

		public async ValueTask<(int Records, long Bytes)> PurgeAsync(HistoryPurgeBatch batch, HistoryRetentionRule rule,
			DateTimeOffset now, CancellationToken ct = default)
		{
			if (rule.MaxAge is not { } age) return (0, 0);
			var cutoff = (now - age).ToUnixTimeMilliseconds();
			return await store.WriteAsync(tx =>
			{
				var deleted = 0;
				var freed = 0L;
				// A kept roll never changes, so only whether it is still there needs checking again.
				foreach (var candidate in batch.Candidates)
				{
					if (!tx.TryGet(Tables.Roll, candidate.Key, out var value)
						|| Codec.Deserialize<RollRecord>(value).AtMs > cutoff)
					{
						continue;
					}

					tx.Delete(Tables.Roll, candidate.Key);
					freed += candidate.Key.Length + value.Length;
					deleted++;
				}

				return (deleted, freed);
			}, ct);
		}
	}
}
