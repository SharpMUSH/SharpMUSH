using System.Buffers.Binary;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAuditStore"/> in <see cref="Tables.Audit"/>: one row per staff action, keyed by the
/// big-endian millisecond it was taken and a big-endian sequence within that millisecond, so the table is
/// in time order and a date range is one key range. The entry's id is the key in hex.
/// </summary>
public partial class LightningDatabase : IAuditStore
{
	private const int AuditKeyLength = 12;

	/// <summary>Rows one listing reads before it stops and hands back where to continue, however selective its filters.</summary>
	private const int AuditScanBudget = 20_000;

	/// <summary>The audit log's retention, as an <see cref="IHistoryStore"/> of kind <c>audit</c>.</summary>
	public IHistoryStore AuditHistory => field ??= new AuditRetention(Store);

	public ValueTask<AuditEntry> AppendAuditAsync(AuditDraft draft, CancellationToken cancellationToken = default)
	{
		var atMs = draft.At.ToUnixTimeMilliseconds();
		var record = new AuditRecord
		{
			AtMs = atMs,
			Action = draft.Action,
			Source = draft.Source.ToString(),
			ActorAccountId = draft.Actor.AccountId,
			ActorAccountName = draft.Actor.AccountName,
			ActorObjid = draft.Actor.Objid,
			ActorName = draft.Actor.Name,
			TargetKind = draft.Target?.Kind,
			TargetId = draft.Target?.Id,
			TargetName = draft.Target?.Name,
			Details = draft.Details
		};

		return Store.WriteAsync(tx =>
		{
			// The next sequence in this millisecond: one past the last row under it, if any.
			var millisecond = AuditKey(atMs, 0)[..8];
			var sequence = tx.RangeReverse(Tables.Audit, millisecond)
				.Select(entry => BinaryPrimitives.ReadUInt32BigEndian(entry.Key.AsSpan(8)) + 1)
				.FirstOrDefault();
			var key = AuditKey(atMs, sequence);
			tx.Put(Tables.Audit, key, Codec.Serialize(record));
			return ToEntry(key, record);
		}, cancellationToken);
	}

	public ValueTask<AuditPage> GetAuditEntriesAsync(AuditFilter filter, CancellationToken cancellationToken = default)
	{
		var limit = Math.Clamp(filter.Limit, 1, AuditFilter.MaxLimit);

		// Start below whichever is earlier: the end of the range, or the cursor.
		byte[]? before = null;
		if (filter.To is { } to) before = AuditKey(to.ToUnixTimeMilliseconds() + 1, 0);
		if (filter.Before is { } cursor && TryParseAuditId(cursor, out var cursorKey)
			&& (before is null || cursorKey.AsSpan().SequenceCompareTo(before) < 0))
		{
			before = cursorKey;
		}

		var fromMs = filter.From?.ToUnixTimeMilliseconds();

		return ValueTask.FromResult(Store.Read(tx =>
		{
			var rows = before is null
				? tx.RangeReverse(Tables.Audit, [])
				: tx.RangeReverseBefore(Tables.Audit, [], before);
			var entries = new List<AuditEntry>(limit);
			var scanned = 0;
			byte[]? lastKey = null;
			foreach (var (key, value) in rows)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (fromMs is { } from && ReadAuditMs(key) < from) return new AuditPage(entries, null);
				if (scanned++ >= AuditScanBudget) return new AuditPage(entries, Convert.ToHexString(lastKey!));
				lastKey = key;

				var record = Codec.Deserialize<AuditRecord>(value);
				if (!Matches(record, filter)) continue;
				entries.Add(ToEntry(key, record));
				if (entries.Count == limit) return new AuditPage(entries, Convert.ToHexString(key));
			}

			return new AuditPage(entries, null);
		}));
	}

	private static bool Matches(AuditRecord record, AuditFilter filter)
	{
		if (filter.Action is { Length: > 0 } action
			&& !(action.EndsWith('.')
				? record.Action.StartsWith(action, StringComparison.OrdinalIgnoreCase)
				: record.Action.Equals(action, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		if (filter.Actor is { Length: > 0 } actor
			&& !Contains(record.ActorAccountName, actor) && !Contains(record.ActorName, actor)
			&& !Contains(record.ActorObjid, actor))
		{
			return false;
		}

		return filter.Text is not { Length: > 0 } text
			|| Contains(record.TargetName, text) || Contains(record.TargetId, text) || Contains(record.Details, text);

		static bool Contains(string? field, string value)
			=> field?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false;
	}

	private static AuditEntry ToEntry(byte[] key, AuditRecord record)
		=> new(
			Convert.ToHexString(key),
			DateTimeOffset.FromUnixTimeMilliseconds(record.AtMs),
			record.Action,
			Enum.TryParse<AuditSource>(record.Source, out var source) ? source : AuditSource.Game,
			new AuditActor(record.ActorAccountId, record.ActorAccountName, record.ActorObjid, record.ActorName),
			record.TargetKind is { } kind ? new AuditTarget(kind, record.TargetId ?? "", record.TargetName ?? "") : null,
			record.Details);

	private static byte[] AuditKey(long atMs, uint sequence)
	{
		var key = new byte[AuditKeyLength];
		BinaryPrimitives.WriteInt64BigEndian(key, Math.Max(atMs, 0));
		BinaryPrimitives.WriteUInt32BigEndian(key.AsSpan(8), sequence);
		return key;
	}

	private static long ReadAuditMs(ReadOnlySpan<byte> key) => BinaryPrimitives.ReadInt64BigEndian(key);

	private static bool TryParseAuditId(string id, out byte[] key)
	{
		key = [];
		if (id.Length != AuditKeyLength * 2) return false;
		try
		{
			key = Convert.FromHexString(id);
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	/// <summary>
	/// Retention over <see cref="Tables.Audit"/>: by age only. Every entry is a stream of its own, and
	/// <see cref="HistoryRetentionRule.KeepNewest"/> has no meaning for it and is ignored.
	/// </summary>
	private sealed class AuditRetention(LightningStore store) : IHistoryStore
	{
		public string Kind => "audit";

		public string Description => "audit log entries (staff actions)";

		public ValueTask<HistoryUsage> MeasureAsync(CancellationToken ct = default)
			=> ValueTask.FromResult(store.Read(tx =>
			{
				long records = 0, bytes = 0;
				foreach (var (key, value) in tx.Range(Tables.Audit, []))
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

			// Oldest first: everything before the first entry young enough to keep is purgeable.
			return ValueTask.FromResult(store.Read(tx =>
			{
				var candidates = new List<HistoryPurgeCandidate>();
				var rows = resumeFrom.Length == 0 ? tx.Range(Tables.Audit, []) : tx.RangeFromKey(Tables.Audit, resumeFrom);
				foreach (var (key, value) in rows)
				{
					if (ReadAuditMs(key) > cutoff) break;
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
				// An entry's time is its key, so it is still as old as when it was chosen; only its presence can change.
				foreach (var candidate in batch.Candidates.Where(candidate => ReadAuditMs(candidate.Key) <= cutoff))
				{
					if (!tx.TryGet(Tables.Audit, candidate.Key, out var value)) continue;
					tx.Delete(Tables.Audit, candidate.Key);
					freed += candidate.Key.Length + value.Length;
					deleted++;
				}

				return (deleted, freed);
			}, ct);
		}
	}
}
