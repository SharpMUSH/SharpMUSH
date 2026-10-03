using System.Text;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Storage;

public sealed partial class LightningSceneStorage
{
	/// <summary>Rows a retention read covers per candidate it may return before the batch is cut at the
	/// next stream boundary.</summary>
	private const int RetentionScanBudgetPerCandidate = 16;

	/// <summary>Every pose's content-version log (<c>scene.log</c>) as an <see cref="IHistoryStore"/>.</summary>
	public IHistoryStore EditHistory => field ??= new PoseEditHistory(this);

	/// <summary>Soft-deleted poses, content and log together, as an <see cref="IHistoryStore"/>.</summary>
	public IHistoryStore DeletedPoseHistory => field ??= new DeletedPoseRetention(this);

	/// <summary>
	/// Retention over <c>scene.log</c>, one stream per pose.
	/// </summary>
	/// <remarks>
	/// What a pass never removes: the version a pose shows (its <c>current_edit</c> pointer) and every
	/// version after it, because those are what a redo reaches. Undo walks back through what survives and
	/// stops at the oldest retained version, exactly as it stops at version 1 on a pose that was never
	/// purged. Sequences are not renumbered, so the next edit still lands at pointer + 1.
	/// </remarks>
	private sealed class PoseEditHistory(LightningSceneStorage scenes) : IHistoryStore
	{
		public string Kind => "scene.edits";

		public string Description => "scene pose edit versions";

		public ValueTask<HistoryUsage> MeasureAsync(CancellationToken ct = default)
			=> ValueTask.FromResult(scenes._accessor.Read(tx =>
			{
				long poses = 0, records = 0, bytes = 0;
				var last = ReadOnlyMemory<byte>.Empty;
				foreach (var (key, value) in tx.Range(scenes._log, []))
				{
					ct.ThrowIfCancellationRequested();
					var stream = StreamOf(key);
					if (!stream.Span.SequenceEqual(last.Span)) poses++;
					last = stream;
					records++;
					bytes += key.Length + value.Length;
				}

				return new HistoryUsage(Kind, Description, poses, records, bytes);
			}));

		public ValueTask<HistoryPurgeBatch> FindPurgeableAsync(HistoryRetentionRule rule, DateTimeOffset now,
			byte[] resumeFrom, int limit, CancellationToken ct = default)
			=> ValueTask.FromResult(scenes._accessor.Read(tx => HistoryScan.Collect(
				resumeFrom.Length == 0 ? tx.Range(scenes._log, []) : tx.RangeFromKey(scenes._log, resumeFrom),
				StreamOf,
				stream => Purgeable(tx, stream, rule, now)
					.Select(row => new HistoryPurgeCandidate(row.Key, row.Key.Length + row.Value.Length, row.Value)),
				limit,
				limit * RetentionScanBudgetPerCandidate)));

		public async ValueTask<(int Records, long Bytes)> PurgeAsync(HistoryPurgeBatch batch, HistoryRetentionRule rule,
			DateTimeOffset now, CancellationToken ct = default)
			=> await scenes._accessor.WriteAsync(tx =>
			{
				var deleted = 0;
				var freed = 0L;
				// Judged again inside the write: an undo since the read moved the pointer back, and the
				// version it now shows must stay.
				foreach (var group in batch.Candidates.GroupBy(c => Convert.ToHexString(StreamOf(c.Key).Span)))
				{
					var prefix = StreamOf(group.First().Key).ToArray();
					var chosen = group.Select(c => Convert.ToHexString(c.Key)).ToHashSet(StringComparer.Ordinal);
					var stillPurgeable = Purgeable(tx, tx.Range(scenes._log, prefix).ToList(), rule, now)
						.Where(row => chosen.Contains(Convert.ToHexString(row.Key)))
						.ToList();
					foreach (var (key, value) in stillPurgeable)
					{
						tx.Delete(scenes._log, key);
						deleted++;
						freed += key.Length + value.Length;
					}
				}

				return (deleted, freed);
			}, ct);

		/// <summary>The key without its trailing sequence: one pose's log.</summary>
		private static ReadOnlyMemory<byte> StreamOf(byte[] key) => key.AsMemory(0, key.Length - sizeof(uint));

		private IEnumerable<(byte[] Key, byte[] Value)> Purgeable(ITx tx, IReadOnlyList<(byte[] Key, byte[] Value)> stream,
			HistoryRetentionRule rule, DateTimeOffset now)
		{
			if (stream.Count == 0) return [];

			// A log whose pose is gone is left alone: there is no pointer to say which version is current,
			// and the deleted-pose rule is what removes a pose's log with it.
			var poseId = Decode<ScenePoseEditRecord>(stream[0].Value).PoseId;
			if (scenes.ReadPose(tx, poseId) is not { } found) return [];

			var currentSeq = found.Pose.CurrentEditSeq;
			return HistoryScan.Purgeable(stream,
				row => (DateTimeOffset.FromUnixTimeMilliseconds(Decode<ScenePoseEditRecord>(row.Value).EditedAt),
					SeqOf(row.Key) >= currentSeq),
				rule, now);
		}
	}

	/// <summary>
	/// Hard purge of soft-deleted poses: the pose row, its id index entry and its whole edit log go in one
	/// transaction, so nothing is ever left pointing at a pose that is gone. Only <see
	/// cref="HistoryRetentionRule.MaxAge"/> applies — a deleted pose is purged once it has been deleted
	/// that long; <see cref="HistoryRetentionRule.KeepNewest"/> has no meaning for it and is ignored.
	/// </summary>
	/// <remarks>
	/// The scene's pose count already left the pose out when it was soft-deleted, and the poses around it
	/// keep their order, because order is each pose's own key. A pose deleted before deletion times were
	/// recorded is aged from its newest edit.
	/// </remarks>
	private sealed class DeletedPoseRetention(LightningSceneStorage scenes) : IHistoryStore
	{
		public string Kind => "scene.deleted";

		public string Description => "soft-deleted scene poses";

		public ValueTask<HistoryUsage> MeasureAsync(CancellationToken ct = default)
			=> ValueTask.FromResult(scenes._accessor.Read(tx =>
			{
				long poses = 0, records = 0, bytes = 0;
				foreach (var (key, value) in tx.Range(scenes._poses, []))
				{
					ct.ThrowIfCancellationRequested();
					var pose = Decode<ScenePoseRecord>(value);
					if (!pose.IsDeleted) continue;
					poses++;
					records++;
					bytes += key.Length + value.Length;
					foreach (var (logKey, logValue) in tx.Range(scenes._log, PoseLogPrefix(pose.Id)))
					{
						records++;
						bytes += logKey.Length + logValue.Length;
					}
				}

				return new HistoryUsage(Kind, Description, poses, records, bytes);
			}));

		public ValueTask<HistoryPurgeBatch> FindPurgeableAsync(HistoryRetentionRule rule, DateTimeOffset now,
			byte[] resumeFrom, int limit, CancellationToken ct = default)
			=> ValueTask.FromResult(scenes._accessor.Read(tx => HistoryScan.Collect(
				resumeFrom.Length == 0 ? tx.Range(scenes._poses, []) : tx.RangeFromKey(scenes._poses, resumeFrom),
				// Every pose is a stream of its own.
				key => key,
				stream => stream
					.Where(row => IsPurgeable(tx, Decode<ScenePoseRecord>(row.Value), rule, now))
					.Select(row => Candidate(tx, row.Key, row.Value)),
				limit,
				limit * RetentionScanBudgetPerCandidate)));

		public async ValueTask<(int Records, long Bytes)> PurgeAsync(HistoryPurgeBatch batch, HistoryRetentionRule rule,
			DateTimeOffset now, CancellationToken ct = default)
			=> await scenes._accessor.WriteAsync(tx =>
			{
				var deleted = 0;
				var freed = 0L;
				foreach (var candidate in batch.Candidates)
				{
					// Re-read: a move since the read renumbered the pose under another key, and this key may
					// now hold a different pose entirely. Only the pose that was archived, unchanged, goes.
					if (!tx.TryGet(scenes._poses, candidate.Key, out var value)) continue;
					var pose = Decode<ScenePoseRecord>(value);
					if (!IsPurgeable(tx, pose, rule, now)
						|| !Candidate(tx, candidate.Key, value).Archive.AsSpan().SequenceEqual(candidate.Archive)) continue;

					tx.Delete(scenes._poses, candidate.Key);
					freed += candidate.Key.Length + value.Length;
					deleted++;

					// Only the index entry that still points at this slot.
					var idxKey = Keys.Str(pose.Id);
					if (tx.TryGet(scenes._poseIdx, idxKey, out var indexed) && indexed.AsSpan().SequenceEqual(candidate.Key))
					{
						tx.Delete(scenes._poseIdx, idxKey);
						freed += idxKey.Length + indexed.Length;
					}

					foreach (var (logKey, logValue) in tx.Range(scenes._log, PoseLogPrefix(pose.Id)).ToList())
					{
						tx.Delete(scenes._log, logKey);
						freed += logKey.Length + logValue.Length;
						deleted++;
					}
				}

				return (deleted, freed);
			}, ct);

		private bool IsPurgeable(ITx tx, ScenePoseRecord pose, HistoryRetentionRule rule, DateTimeOffset now)
		{
			if (!pose.IsDeleted || rule.MaxAge is not { } age) return false;
			var deletedAt = pose.DeletedAt
				?? scenes.PoseEdits(tx, pose.Id).Select(e => (long?)e.Record.EditedAt).Max()
				?? pose.CreatedAt;
			return DateTimeOffset.FromUnixTimeMilliseconds(deletedAt) <= now - age;
		}

		/// <summary>The pose and its whole log, archived as one JSON object.</summary>
		private HistoryPurgeCandidate Candidate(ITx tx, byte[] key, byte[] value)
		{
			var pose = Decode<ScenePoseRecord>(value);
			var edits = tx.Range(scenes._log, PoseLogPrefix(pose.Id)).ToList();
			var archive = new StringBuilder("{\"pose\":")
				.Append(Encoding.UTF8.GetString(value))
				.Append(",\"edits\":[")
				.AppendJoin(',', edits.Select(e => Encoding.UTF8.GetString(e.Value)))
				.Append("]}");
			return new HistoryPurgeCandidate(key, key.Length + value.Length + edits.Sum(e => (long)e.Key.Length + e.Value.Length),
				Encoding.UTF8.GetBytes(archive.ToString()));
		}
	}
}
