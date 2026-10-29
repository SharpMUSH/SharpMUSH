using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IActivityStore"/> in <see cref="Tables.ActivityPlayer"/>, <see cref="Tables.ActivityRoom"/> and
/// <see cref="Tables.ActivityLast"/>. Day rows are keyed day first, so a report window is one key range and
/// retention drops whole days from the front.
/// </summary>
public partial class LightningDatabase : IActivityStore
{
	/// <summary>Activity's retention, as an <see cref="IHistoryStore"/> of kind <c>activity</c>.</summary>
	public IHistoryStore ActivityHistory => field ??= new ActivityRetention(Store);

	public ValueTask MergeActivityAsync(ActivityBatch batch, CancellationToken cancellationToken = default)
	{
		if (batch.IsEmpty) return ValueTask.CompletedTask;

		return Store.WriteAsync(tx =>
		{
			foreach (var day in batch.Players)
			{
				var key = DayKey(day.Day, day.Player);
				var kept = tx.TryGet(Tables.ActivityPlayer, key, out var value)
					? Codec.Deserialize<ActivityPlayerRecord>(value)
					: new ActivityPlayerRecord();
				tx.Put(Tables.ActivityPlayer, key, Codec.Serialize(new ActivityPlayerRecord
				{
					Poses = kept.Poses + day.Poses,
					Hours = kept.Hours | day.Hours
				}));
			}

			foreach (var day in batch.Rooms)
			{
				var key = DayKey(day.Day, day.Room);
				var kept = tx.TryGet(Tables.ActivityRoom, key, out var value)
					? Codec.Deserialize<ActivityRoomRecord>(value)
					: new ActivityRoomRecord();
				tx.Put(Tables.ActivityRoom, key, Codec.Serialize(new ActivityRoomRecord
				{
					Poses = kept.Poses + day.Poses,
					Scenes = kept.Scenes + day.Scenes,
					Posers = kept.Posers.Union(day.Posers).Order().ToArray()
				}));
			}

			foreach (var (dbref, at) in batch.LastPoses)
			{
				var key = Keys.Dbref(dbref);
				if (tx.TryGet(Tables.ActivityLast, key, out var value) && Keys.ReadDbref(value) >= at) continue;
				tx.Put(Tables.ActivityLast, key, Keys.Dbref(at));
			}
		}, cancellationToken);
	}

	public ValueTask<ActivityWindow> GetActivityAsync(int fromDay, int toDay, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read(tx =>
		{
			var players = new List<ActivityPlayerDay>();
			foreach (var (key, value) in Days(tx, Tables.ActivityPlayer, fromDay, toDay))
			{
				var record = Codec.Deserialize<ActivityPlayerRecord>(value);
				players.Add(new ActivityPlayerDay(ReadDay(key), ReadSubject(key), record.Poses, record.Hours));
			}

			var rooms = new List<ActivityRoomDay>();
			foreach (var (key, value) in Days(tx, Tables.ActivityRoom, fromDay, toDay))
			{
				var record = Codec.Deserialize<ActivityRoomRecord>(value);
				rooms.Add(new ActivityRoomDay(ReadDay(key), ReadSubject(key), record.Poses, record.Scenes, record.Posers));
			}

			var last = new Dictionary<long, long>();
			foreach (var (key, value) in tx.Range(Tables.ActivityLast, []))
			{
				last[Keys.ReadDbref(key)] = Keys.ReadDbref(value);
			}

			return new ActivityWindow(players, rooms, last);
		}));

	private static IEnumerable<(byte[] Key, byte[] Value)> Days(ITx tx, TableDef table, int fromDay, int toDay)
		=> tx.RangeFromKey(table, Keys.Dbref(fromDay)).TakeWhile(row => ReadDay(row.Key) <= toDay);

	private static byte[] DayKey(int day, long subject) => Keys.Concat(Keys.Dbref(day), Keys.Dbref(subject));

	private static int ReadDay(ReadOnlySpan<byte> key) => (int)Keys.ReadDbref(key[..8]);

	private static long ReadSubject(ReadOnlySpan<byte> key) => Keys.ReadDbref(key[8..16]);

	/// <summary>
	/// Drops a freed object's day rows and last pose, so they are not reported for whatever holds its number
	/// next. Only a character or room that has posed has any, and every one that has has a last pose. A room's
	/// list of who posed in it keeps the number; it only counts distinct posers.
	/// </summary>
	private static void DeleteActivitySubject(ITx tx, long subject)
	{
		if (!tx.TryGet(Tables.ActivityLast, Keys.Dbref(subject), out _)) return;
		foreach (var table in (TableDef[])[Tables.ActivityPlayer, Tables.ActivityRoom])
		{
			foreach (var (key, _) in tx.Range(table, []).Where(row => ReadSubject(row.Key) == subject).ToList())
			{
				tx.Delete(table, key);
			}
		}

		tx.Delete(Tables.ActivityLast, Keys.Dbref(subject));
	}

	/// <summary>
	/// Retention over the two day tables: by age only, whole days at a time. A candidate's key is the
	/// table's marker byte (0 characters, 1 rooms) followed by the row's key. Last poses are one row per
	/// object and stay.
	/// </summary>
	private sealed class ActivityRetention(LightningStore store) : IHistoryStore
	{
		private static readonly TableDef[] DayTables = [Tables.ActivityPlayer, Tables.ActivityRoom];

		public string Kind => "activity";

		public string Description => "play activity per character and room per day (poses, scenes)";

		public ValueTask<HistoryUsage> MeasureAsync(CancellationToken ct = default)
			=> ValueTask.FromResult(store.Read(tx =>
			{
				long records = 0, bytes = 0;
				var days = new HashSet<int>();
				foreach (var table in DayTables)
				{
					foreach (var (key, value) in tx.Range(table, []))
					{
						ct.ThrowIfCancellationRequested();
						records++;
						bytes += key.Length + value.Length;
						days.Add(ReadDay(key));
					}
				}

				return new HistoryUsage(Kind, Description, days.Count, records, bytes);
			}));

		public ValueTask<HistoryPurgeBatch> FindPurgeableAsync(HistoryRetentionRule rule, DateTimeOffset now,
			byte[] resumeFrom, int limit, CancellationToken ct = default)
		{
			if (rule.MaxAge is not { } age) return ValueTask.FromResult(new HistoryPurgeBatch([], []));
			var cutoff = CutoffDay(now, age);

			return ValueTask.FromResult(store.Read(tx =>
			{
				var candidates = new List<HistoryPurgeCandidate>();
				var first = resumeFrom.Length == 0 ? 0 : resumeFrom[0];
				for (var marker = first; marker < DayTables.Length; marker++)
				{
					var rows = marker == first && resumeFrom.Length > 1
						? tx.RangeFromKey(DayTables[marker], resumeFrom[1..])
						: tx.Range(DayTables[marker], []);
					foreach (var (key, value) in rows)
					{
						if (ReadDay(key) >= cutoff) break;
						var candidateKey = Keys.Concat([(byte)marker], key);
						if (candidates.Count == limit) return new HistoryPurgeBatch(candidates, candidateKey);
						candidates.Add(new HistoryPurgeCandidate(candidateKey, key.Length + value.Length, value));
					}
				}

				return new HistoryPurgeBatch(candidates, []);
			}));
		}

		public async ValueTask<(int Records, long Bytes)> PurgeAsync(HistoryPurgeBatch batch, HistoryRetentionRule rule,
			DateTimeOffset now, CancellationToken ct = default)
		{
			if (rule.MaxAge is not { } age) return (0, 0);
			var cutoff = CutoffDay(now, age);
			return await store.WriteAsync(tx =>
			{
				var deleted = 0;
				var freed = 0L;
				foreach (var candidate in batch.Candidates)
				{
					var table = DayTables[candidate.Key[0]];
					var key = candidate.Key[1..];
					if (ReadDay(key) >= cutoff || !tx.TryGet(table, key, out var value)) continue;

					tx.Delete(table, key);
					freed += key.Length + value.Length;
					deleted++;
				}

				return (deleted, freed);
			}, ct);
		}

		/// <summary>The first day kept: a day goes once all of it is older than <paramref name="age"/>.</summary>
		private static int CutoffDay(DateTimeOffset now, TimeSpan age)
			=> DateOnly.FromDateTime((now - age).UtcDateTime).DayNumber;
	}
}
