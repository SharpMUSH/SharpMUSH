using System.Buffers.Binary;
using System.Text.Json;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>Where a rebuild is, for the hooks tests kill it at.</summary>
internal enum SemanticRebuildStage { BatchApplied, CheckpointCommitted, ReplayCommitted, Validated, FlipApplied, Flipped }

/// <param name="BatchSize">Subjects copied, or journal entries replayed, per writer job.</param>
/// <param name="JournalCapacity">Journal entries the staging may hold before it is abandoned.</param>
/// <param name="MaxReplayPasses">Replay passes that may each find new writes before the rebuild gives up.</param>
/// <param name="RetireBatchSize">Rows a retired or abandoned generation loses per writer job.</param>
/// <param name="ValidationWorkingBytes">Working memory the reference evaluation may hold for any one subject.</param>
internal sealed record SemanticRebuildOptions(int BatchSize = 64, ulong JournalCapacity = SemanticRebuildJournal.DefaultCapacity,
	int MaxReplayPasses = 16, int RetireBatchSize = 256, long ValidationWorkingBytes = 32L << 20);

internal abstract record SemanticRebuildOutcome
{
	private SemanticRebuildOutcome() { }
	/// <summary>The staged generation is active. The previous one has been cleared, or, if the world closed first, the next rebuild clears it.</summary>
	internal sealed record Flipped(ulong Generation) : SemanticRebuildOutcome;
	/// <summary>The staged generation was abandoned and the active one never changed. Its rows are cleared now, or when the world reopens if it closed first.</summary>
	internal sealed record Aborted(string Reason) : SemanticRebuildOutcome;
	/// <summary>Another rebuild of this world is running; this one did nothing.</summary>
	internal sealed record AlreadyRunning : SemanticRebuildOutcome { internal static readonly AlreadyRunning Value = new(); }
}

/// <summary>
/// Rebuilds an active projection's secondary state into the other generation slot (spec §125): the staging
/// starts a journal in the same job as its record, subjects are copied in short ID batches with a durable
/// checkpoint, the journal is replayed in sequence, the staged generation is checked against a slow reference
/// evaluation, and one short writer job verifies nothing is unconsumed and moves the active generation. Writes
/// are never held back or dropped: a full journal or writes that keep outrunning replay abandon the rebuild.
/// An unfinished staging is discarded on reopen; resuming one is not implemented.
/// </summary>
public partial class LightningDatabase
{
	/// <summary>Tests only: runs inside a rebuild's own job, before it commits.</summary>
	internal Action<SemanticRebuildStage>? SemanticRebuildJobHook { get; set; }
	/// <summary>Tests only: runs after a rebuild step has committed.</summary>
	internal Func<SemanticRebuildStage, ValueTask>? SemanticRebuildHook { get; set; }

	/// <summary>1 while a rebuild runs: rebuilds have one admission of their own (spec §9), and a second would discard the first's staging.</summary>
	private int _semanticRebuildRunning;

	internal async ValueTask<SemanticRebuildOutcome> RebuildSemanticProjectionAsync(SemanticRebuildOptions? options, CancellationToken ct)
	{
		if (Interlocked.Exchange(ref _semanticRebuildRunning, 1) == 1) return SemanticRebuildOutcome.AlreadyRunning.Value;
		// A world that closes under a rebuild keeps whatever it last committed: an unfinished staging is discarded when
		// the world reopens, and a retired generation left uncleared is cleared by the next rebuild before it stages.
		try { return await RebuildCoreAsync(options, ct); }
		catch (Exception) when (SemanticStorageClosed)
		{
			return new SemanticRebuildOutcome.Aborted("The world closed during the rebuild; its staging is discarded when the world reopens.");
		}
		finally { Volatile.Write(ref _semanticRebuildRunning, 0); }
	}

	private async ValueTask<SemanticRebuildOutcome> RebuildCoreAsync(SemanticRebuildOptions? options, CancellationToken ct)
	{
		options ??= new();
		ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(options.RetireBatchSize, 1);
		using var admission = EnterFixtureMutation(SemanticFixtureMutation.LegacyWiki);
		if (admission is not { Phase: SemanticFixturePhase.Active }) throw SemanticFixtureCapability.Unavailable();
		await DiscardIncompleteSemanticRebuildAsync(admission, options, ct);
		var active = ActiveSemanticGeneration();
		var staged = checked(active + 1);
		// The slot may still hold a generation an interrupted retirement did not finish clearing.
		await ClearGenerationAsync(admission, AtomicWikiLayout.ForGeneration(staged), options.RetireBatchSize, ct);
		await Store.WriteStagedSemanticAsync(tx =>
		{
			RequireFixtureMutation(tx, admission);
			using var work = StagedWork(tx, AtomicWikiLayout.ForGeneration(active), ct);
			if (ActiveGeneration(tx) != active || SemanticRebuildJournal.Read(work) is not null) throw SemanticFixtureCapability.Unavailable();
			// From this commit on, every projection write journals in its own job.
			work.Delta.Put(work.Tables.Metadata, work.Key(SemanticRebuildJournal.StagingKey),
				SemanticRebuildJournal.Encode(new(staged, SemanticRebuildStagingState.Staging, options.JournalCapacity)));
			foreach (var key in new[] { SemanticRebuildJournal.CountKey, SemanticRebuildJournal.CheckpointKey, SemanticRebuildJournal.ConsumedKey })
				work.Delta.Put(work.Tables.Metadata, work.Key(key), work.Number(0));
			work.Delta.Apply(tx, ct, null, null);
			return true;
		}, ct);
		try
		{
			byte[] after = [];
			while (true)
			{
				var (copied, last) = await Store.WriteStagedSemanticAsync(tx => CopyBatch(tx, admission, active, staged, after, options.BatchSize, ct), ct);
				if (SemanticRebuildHook is { } checkpoint) await checkpoint(SemanticRebuildStage.CheckpointCommitted);
				if (StagingOverflowed()) return await AbandonAsync(admission, staged, options, "The journal filled before the rebuild caught up.", ct);
				if (copied == 0) break;
				after = last;
				await Task.Yield();
			}
			for (var pass = 0; ; pass++)
			{
				int replayed;
				do
				{
					replayed = await Store.WriteStagedSemanticAsync(tx => ReplayBatch(tx, admission, active, staged, options.BatchSize, ct), ct);
					if (SemanticRebuildHook is { } replay) await replay(SemanticRebuildStage.ReplayCommitted);
					if (StagingOverflowed()) return await AbandonAsync(admission, staged, options, "The journal filled before the rebuild caught up.", ct);
				} while (replayed == options.BatchSize);
				// A write journaled after the last replay can explain a difference; only an empty journal proves one.
				var (pending, mismatch) = Store.Read(tx => (JournalCount(tx), ValidateStagedGeneration(tx, active, staged, options.ValidationWorkingBytes)));
				if (pending == 0 && mismatch is not null)
					return await AbandonAsync(admission, staged, options, "The staged generation disagrees with the reference evaluation: " + mismatch, ct);
				if (pending == 0)
				{
					if (SemanticRebuildHook is { } validated) await validated(SemanticRebuildStage.Validated);
					if (await Store.WriteAsync(tx => FlipGeneration(tx, admission, active, staged), ct)) break;
				}
				if (pass >= options.MaxReplayPasses) return await AbandonAsync(admission, staged, options, "Writes kept outrunning replay.", ct);
				// Back off before the next pass, so writes are never held while the rebuild catches up.
				await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(250, 5 << Math.Min(pass, 6))), ct);
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			await AbandonAsync(admission, staged, options, "Cancelled.", CancellationToken.None);
			throw;
		}
		try
		{
			if (SemanticRebuildHook is { } flipped) await flipped(SemanticRebuildStage.Flipped);
			await ClearGenerationAsync(admission, AtomicWikiLayout.ForGeneration(active), options.RetireBatchSize, ct);
		}
		// The flip committed; only the retired rows are left, and the next rebuild clears that slot first.
		catch (Exception) when (SemanticStorageClosed) { }
		return new SemanticRebuildOutcome.Flipped(staged);
	}

	/// <summary>Discards a staging an earlier process or rebuild left unfinished, with its journal and staged rows.</summary>
	internal async ValueTask DiscardIncompleteSemanticRebuildAsync(CancellationToken ct)
	{
		using var admission = EnterFixtureMutation(SemanticFixtureMutation.LegacyWiki);
		if (admission is not { Phase: SemanticFixturePhase.Active }) return;
		await DiscardIncompleteSemanticRebuildAsync(admission, new(), ct);
	}

	private async ValueTask DiscardIncompleteSemanticRebuildAsync(SemanticFixtureMutationLease admission, SemanticRebuildOptions options, CancellationToken ct)
	{
		if (Store.Read(tx => StagingRecord(tx)) is { } staging)
			await AbandonAsync(admission, staging.Generation, options, "An unfinished rebuild was discarded.", ct);
	}

	/// <summary>The generation readers use: the activation marker's, or the first before activation.</summary>
	internal ulong ActiveSemanticGeneration()
		=> Store.Read(tx => ReadActivation(tx, _fixtureLease?.FixtureId ?? throw SemanticFixtureCapability.Unavailable())?.Generation ?? 1);

	private ulong ActiveGeneration(ITx tx)
		=> ReadActivation(tx, _fixtureLease?.FixtureId ?? throw SemanticFixtureCapability.Unavailable()) is { Active: true } marker
			? marker.Generation : throw SemanticFixtureCapability.Unavailable();

	private static SemanticRebuildStaging? StagingRecord(ITx tx)
		=> SemanticRebuildJournal.Decode(tx.TryGet(AtomicWikiLayout.Metadata, [SemanticRebuildJournal.StagingKey], out var bytes) ? bytes : null);

	private bool StagingOverflowed() => Store.Read(tx => StagingRecord(tx)) is { State: SemanticRebuildStagingState.Overflowed };

	private static ulong JournalCount(ITx tx)
		=> tx.TryGet(AtomicWikiLayout.Metadata, [SemanticRebuildJournal.CountKey], out var counted) ? BinaryPrimitives.ReadUInt64BigEndian(counted) : 0;

	private static WikiSemanticWork StagedWork(ITx tx, WikiSemanticTables tables, CancellationToken ct)
		=> WikiSemanticWork.Create(tx, tables, new NativeReadBudget(8192, 8L << 20, 32L << 20, ct, maxRecords: 8192));

	private void RequireStaging(ITx tx, ulong active, ulong staged)
	{
		if (ActiveGeneration(tx) != active || StagingRecord(tx) is not { Generation: var generation } || generation != staged)
			throw SemanticFixtureCapability.Unavailable();
	}

	private (int Copied, byte[] Last) CopyBatch(ITx tx, SemanticFixtureMutationLease admission, ulong active, ulong staged,
		byte[] after, int size, CancellationToken ct)
	{
		RequireFixtureMutation(tx, admission);
		RequireStaging(tx, active, staged);
		var activeTables = AtomicWikiLayout.ForGeneration(active);
		var budget = new NativeReadBudget(4096, 8L << 20, 32L << 20, ct, maxRecords: NativeReadBudget.MaxBatchRecords);
		using var batch = ((IBoundedReadTx)tx).ReadRange(activeTables.Summaries, [], [], [], after, budget);
		var copied = 0;
		var last = after;
		foreach (var row in batch.Entries.Take(size))
		{
			CopySubject(tx, active, staged, row.Key, WikiSemanticStateCodec.ProjectionState(row.Value), ct);
			if (copied++ == 0) SemanticRebuildJobHook?.Invoke(SemanticRebuildStage.BatchApplied);
			last = row.Key;
		}
		if (copied > 0)
			tx.Put(AtomicWikiLayout.Metadata, [SemanticRebuildJournal.CheckpointKey], last.Length == 8 ? last : throw new InvalidDataException("Invalid subject key."));
		return (copied, last);
	}

	private int ReplayBatch(ITx tx, SemanticFixtureMutationLease admission, ulong active, ulong staged, int size, CancellationToken ct)
	{
		RequireFixtureMutation(tx, admission);
		RequireStaging(tx, active, staged);
		var activeTables = AtomicWikiLayout.ForGeneration(active);
		var budget = new NativeReadBudget(4096, 8L << 20, 32L << 20, ct, maxRecords: NativeReadBudget.MaxBatchRecords);
		using var batch = ((IBoundedReadTx)tx).ReadRange(AtomicWikiLayout.Journal, [], [], [], [], budget);
		var entries = batch.Entries.Take(size).ToArray();
		foreach (var entry in entries)
		{
			// The journal names the subject; its current state is at least the journaled write's after-image.
			var subjectKey = entry.Value;
			if (!tx.TryGet(activeTables.Summaries, subjectKey, out var current)) throw new InvalidDataException("A journaled subject has no active summary.");
			CopySubject(tx, active, staged, subjectKey, WikiSemanticStateCodec.ProjectionState(current), ct);
			tx.Delete(AtomicWikiLayout.Journal, entry.Key);
		}
		if (entries.Length > 0)
		{
			var count = tx.TryGet(AtomicWikiLayout.Metadata, [SemanticRebuildJournal.CountKey], out var counted) ? BinaryPrimitives.ReadUInt64BigEndian(counted) : 0;
			var remaining = new byte[8];
			BinaryPrimitives.WriteUInt64BigEndian(remaining, checked(count - (ulong)entries.Length));
			tx.Put(AtomicWikiLayout.Metadata, [SemanticRebuildJournal.CountKey], remaining);
			tx.Put(AtomicWikiLayout.Metadata, [SemanticRebuildJournal.ConsumedKey], entries[^1].Key);
		}
		return entries.Length;
	}

	/// <summary>
	/// Copies one subject's active state into the staged generation, replacing any earlier copy: its index rows
	/// there are cleared by subject, then its canonical projection is planned there with the same version and
	/// sequence. The descriptor is derived again from the live page and requirements, not copied.
	/// </summary>
	private void CopySubject(ITx tx, ulong active, ulong staged, byte[] subjectKey, WikiSemanticProjectionState state, CancellationToken ct)
	{
		var stagedTables = AtomicWikiLayout.ForGeneration(staged);
		using var work = StagedWork(tx, stagedTables, ct);
		var subject = BinaryPrimitives.ReadUInt64BigEndian(subjectKey);
		if (work.Read(stagedTables.Summaries, subjectKey, WikiSemanticStateCodec.ProjectionState) is { } previous)
		{
			using var rows = work.Reader.ReadRange(stagedTables.Forward, subjectKey, [], [], [], work.Budget);
			if (!rows.Exhausted) throw new NativeReadLimitException("records");
			foreach (var row in rows.Entries)
			{
				work.Delta.Delete(stagedTables.Forward, row.Key);
				var posting = new byte[41];
				BinaryPrimitives.WriteUInt64BigEndian(posting, previous.State.CollectionId);
				BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(8), previous.State.ClassId);
				row.Key.AsSpan(8, 17).CopyTo(posting.AsSpan(16));
				BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(33), subject);
				work.Delta.Delete(stagedTables.Postings, posting);
			}
		}
		var summary = state.State.Summary;
		var facts = work.Read(stagedTables.Facts, subjectKey, bytes => WikiSemanticCodec.DecodeFacts(bytes), 8, 1024)
			?? throw new InvalidDataException("A subject has no canonical facts.");
		WikiPageProjection projection;
		if (TryParseWikiPageId(summary.Reference.Id, out var key)
			&& work.Read(Tables.WikiPage, work.Bytes(8, () => WikiPageKey(key)), bytes => DecodeLegacyPage(bytes), 32, 2048) is { } record)
		{
			if (summary.Deleted) throw new InvalidDataException("A deleted subject has a live page.");
			var rules = ReadLegacyProjectionRules(work, summary.Reference.Id, record);
			projection = new(summary.Reference, state.Source, record.Title, summary.Collection,
				new(record.Namespace, [.. record.Categories ?? []], rules, record.Published ?? true, record.Published == false ? record.AuthorDbref : null));
		}
		else
		{
			if (!summary.Deleted) throw new InvalidDataException("A live subject has no page.");
			projection = new(summary.Reference, state.Source, summary.Title, summary.Collection, summary.Visibility);
		}
		if (PreparedWikiProjection.PrepareProjection(AtomicWikiLayout.Schema, projection, facts, summary.Deleted, ct) is not PreparedWikiProjection prepared)
			throw new InvalidDataException("A subject's canonical projection is invalid.");
		if (WikiSemanticProjectionPlanner.PlanProjection(work, prepared, 0, new CopiedProjection(summary.StateVersion, state.State.CommitSequence))
			is not WikiProjectionPlanned)
			throw new InvalidDataException("A subject could not be copied.");
		work.Delta.Apply(tx, ct, null, null);
	}

	/// <summary>
	/// The slow reference evaluation: from the active summaries' identities, the live pages and requirements and
	/// the canonical facts, every staged row the staged generation must hold, compared exactly. Null when it agrees.
	/// </summary>
	private string? ValidateStagedGeneration(ITx tx, ulong active, ulong staged, long workingBytes)
	{
		var activeTables = AtomicWikiLayout.ForGeneration(active);
		var stagedTables = AtomicWikiLayout.ForGeneration(staged);
		var forward = new HashSet<string>(StringComparer.Ordinal);
		var postings = new HashSet<string>(StringComparer.Ordinal);
		var references = new Dictionary<string, long>(StringComparer.Ordinal);
		var subjects = 0;
		foreach (var (key, value) in tx.Range(activeTables.Summaries, []))
		{
			subjects++;
			// Each subject is evaluated under its own bounded work, so the reservations never accumulate with the world's size.
			using var work = WikiSemanticWork.Create(tx, stagedTables, new NativeReadBudget(4096, 16L << 20, workingBytes, CancellationToken.None));
			var expected = WikiSemanticStateCodec.ProjectionState(value);
			if (!tx.TryGet(stagedTables.Summaries, key, out var stagedBytes)) return "a subject is missing";
			var actual = WikiSemanticStateCodec.ProjectionState(stagedBytes);
			var (want, have) = (expected.State.Summary, actual.State.Summary);
			if (want.Reference != have.Reference || want.NativeId != have.NativeId || want.Collection != have.Collection
				|| want.Deleted != have.Deleted || want.StateVersion != have.StateVersion || expected.State.CommitSequence != actual.State.CommitSequence
				|| expected.State.CollectionId != actual.State.CollectionId || !SameSource(expected.Source, actual.Source))
				return "a subject's identity differs";
			var descriptor = WikiSemanticStateCodec.Descriptor(want.Visibility);
			if (TryParseWikiPageId(want.Reference.Id, out var page)
				&& work.Read(Tables.WikiPage, work.Bytes(8, () => WikiPageKey(page)), bytes => DecodeLegacyPage(bytes), 32, 2048) is { } record)
			{
				if (have.Title != record.Title) return "a subject's title differs from its page";
				descriptor = WikiSemanticStateCodec.Descriptor(PreparedWikiProjection.FreezeVisibility(new(record.Namespace, [.. record.Categories ?? []],
					ReadLegacyProjectionRules(work, want.Reference.Id, record), record.Published ?? true, record.Published == false ? record.AuthorDbref : null),
					CancellationToken.None));
			}
			if (!descriptor.AsSpan().SequenceEqual(WikiSemanticStateCodec.Descriptor(have.Visibility))) return "a subject's visibility differs from its page";
			var facts = WikiSemanticCodec.DecodeFacts(tx.TryGet(stagedTables.Facts, key, out var factBytes) ? factBytes : throw new InvalidDataException("No facts."));
			if (have.Deleted || facts.Facts.IsEmpty)
			{
				if (actual.State.ClassId != 0) return "a subject without live facts holds a class";
				continue;
			}
			var pair = work.Pair(actual.State.CollectionId, actual.State.ClassId);
			if (!tx.TryGet(stagedTables.Catalog, pair, out var catalog)
				|| !WikiSemanticStateCodec.Catalog(catalog).Descriptor.AsSpan().SequenceEqual(descriptor))
				return "a subject's class does not match its visibility";
			var name = Convert.ToHexString(pair);
			references[name] = references.GetValueOrDefault(name) + 1;
			foreach (var fact in facts.Facts)
			{
				var (f, p) = WikiSemanticProjectionPlanner.IndexKeys(work, have.NativeId, actual.State.CollectionId, actual.State.ClassId, fact, create: false);
				forward.Add(Convert.ToHexString(f));
				postings.Add(Convert.ToHexString(p));
			}
		}
		if (tx.Range(stagedTables.Summaries, []).Count() != subjects) return "the staged generation holds an extra subject";
		if (!tx.Range(stagedTables.Forward, []).Select(row => Convert.ToHexString(row.Key)).ToHashSet(StringComparer.Ordinal).SetEquals(forward))
			return "forward rows differ";
		if (!tx.Range(stagedTables.Postings, []).Select(row => Convert.ToHexString(row.Key)).ToHashSet(StringComparer.Ordinal).SetEquals(postings))
			return "postings differ";
		var catalogRows = tx.Range(stagedTables.Catalog, []).ToDictionary(row => Convert.ToHexString(row.Key),
			row => WikiSemanticStateCodec.Catalog(row.Value).References, StringComparer.Ordinal);
		if (catalogRows.Count != references.Count || references.Any(pair => catalogRows.GetValueOrDefault(pair.Key) != pair.Value))
			return "class references differ";
		return null;
	}

	/// <summary>
	/// The flip: one short job that refuses while any journaled write is unconsumed, then moves the active
	/// generation in the activation marker and both its metadata copies, and ends the staging.
	/// </summary>
	private bool FlipGeneration(ITx tx, SemanticFixtureMutationLease admission, ulong active, ulong staged)
	{
		RequireFixtureMutation(tx, admission);
		RequireStaging(tx, active, staged);
		if (StagingRecord(tx) is not { State: SemanticRebuildStagingState.Staging }) return false;
		if (JournalCount(tx) != 0) return false;
		var marker = ReadActivation(tx, admission.Owner.FixtureId) ?? throw SemanticFixtureCapability.Unavailable();
		var flipped = AtomicWikiLayout.Encode(marker with { Generation = staged });
		tx.Put(Tables.Meta, AtomicWikiLayout.MarkerKey, flipped);
		tx.Put(AtomicWikiLayout.Metadata, AtomicWikiLayout.RootKey, flipped);
		tx.Put(AtomicWikiLayout.Metadata, AtomicWikiLayout.CertificateKey, flipped);
		foreach (var key in new[] { SemanticRebuildJournal.StagingKey, SemanticRebuildJournal.CountKey, SemanticRebuildJournal.CheckpointKey, SemanticRebuildJournal.ConsumedKey })
			tx.Delete(AtomicWikiLayout.Metadata, [key]);
		SemanticRebuildJobHook?.Invoke(SemanticRebuildStage.FlipApplied);
		return true;
	}

	/// <summary>Ends the staging, so writes stop journaling, then clears its journal and staged rows in bounded jobs.</summary>
	private async ValueTask<SemanticRebuildOutcome> AbandonAsync(SemanticFixtureMutationLease admission, ulong staged, SemanticRebuildOptions options,
		string reason, CancellationToken ct)
	{
		await Store.WriteStagedSemanticAsync(tx =>
		{
			RequireFixtureMutation(tx, admission);
			foreach (var key in new[] { SemanticRebuildJournal.StagingKey, SemanticRebuildJournal.CountKey, SemanticRebuildJournal.CheckpointKey, SemanticRebuildJournal.ConsumedKey })
				tx.Delete(AtomicWikiLayout.Metadata, [key]);
			return true;
		}, ct);
		await ClearRowsAsync(admission, AtomicWikiLayout.Journal, [], options.RetireBatchSize, ct);
		await ClearGenerationAsync(admission, AtomicWikiLayout.ForGeneration(staged), options.RetireBatchSize, ct);
		return new SemanticRebuildOutcome.Aborted(reason);
	}

	/// <summary>Clears every row <paramref name="tables"/>' generation owns, in jobs of at most <paramref name="batch"/> rows.</summary>
	private async ValueTask ClearGenerationAsync(SemanticFixtureMutationLease admission, WikiSemanticTables tables, int batch, CancellationToken ct)
	{
		if (ReferenceEquals(tables.Summaries, AtomicWikiLayout.ForGeneration(ActiveSemanticGeneration()).Summaries))
			throw new InvalidOperationException("The active generation is never cleared.");
		foreach (var (table, prefix) in tables.Owned) await ClearRowsAsync(admission, table, prefix, batch, ct);
	}

	private async ValueTask ClearRowsAsync(SemanticFixtureMutationLease admission, TableDef table, byte[] prefix, int batch, CancellationToken ct)
	{
		while (await Store.WriteStagedSemanticAsync(tx =>
		{
			RequireFixtureMutation(tx, admission);
			var budget = new NativeReadBudget(4096, 8L << 20, 32L << 20, ct, maxRecords: NativeReadBudget.MaxBatchRecords);
			using var rows = ((IBoundedReadTx)tx).ReadRange(table, prefix, [], [], [], budget);
			var keys = rows.Entries.Take(batch).Select(row => row.Key).ToArray();
			foreach (var key in keys) tx.Delete(table, key);
			return keys.Length;
		}, ct) > 0)
			await Task.Yield();
	}
}
