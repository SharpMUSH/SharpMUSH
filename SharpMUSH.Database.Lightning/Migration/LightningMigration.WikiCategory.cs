using System.Buffers.Binary;
using System.Security.Cryptography;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	private long _wikiCategoryReadyEpoch = -1;

	private bool HasCompleteWikiCategoryAuthority(WikiCategoryMigrationOptions options, CancellationToken ct)
	{
		var budget = options.CreateBudget(ct);
		using var fixedWork = budget.ReserveWorking(32768);
		return Store.Read(tx =>
		{
			ValidateWikiCategoryEnvironment();
			var reader = (IWikiCategoryMigrationReadTx)tx;
			using var state = reader.ReadWikiCategoryState(budget) is NativeReadValue found ? found : null;
			using var marker = reader.ReadWikiCategoryCompletion(budget) is NativeReadValue completion ? completion : null;
			return WikiCategoryAuthorityCodec.Validate(state is not null, StateBytes(state), marker is not null,
				StateBytes(marker), Store.MaxKeySize, ct);
		});
	}

	internal async ValueTask MigrateWikiCategoryValuesAsync(WikiCategoryMigrationOptions options, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(options);
		var preflightBudget = options.CreateBudget(ct);
		using var preflightWork = preflightBudget.ReserveWorking(32768);
		var initial = Store.Read(tx =>
		{
			ValidateWikiCategoryEnvironment();
			var epoch = Store.Epoch;
			var reader = (IWikiCategoryMigrationReadTx)tx;
			var state = reader.ReadWikiCategoryState(preflightBudget) is NativeReadValue owned ? owned : null;
			try
			{
				using var marker = reader.ReadWikiCategoryCompletion(preflightBudget) is NativeReadValue found ? found : null;
				var complete = WikiCategoryAuthorityCodec.Validate(state is not null, StateBytes(state), marker is not null,
					StateBytes(marker), Store.MaxKeySize, ct);
				return (State: state, Epoch: epoch, Complete: complete);
			}
			catch { state?.Dispose(); throw; }
		});
		NativeReadValue? expected = initial.State;
		try
		{
			if (initial.Complete)
			{
				Volatile.Write(ref _wikiCategoryReadyEpoch, initial.Epoch);
				return;
			}
			using var admission = EnterFixtureMutation(SemanticFixtureMutation.Unsupported);
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				var budget = options.CreateBudget(ct);
				NativeReadValue? candidate = null;
				try
				{
					// Capture inside the callback: a writer can lose its result when native commit fails.
					await Store.WriteAsync(tx =>
					{
						candidate = ApplyWikiCategoryMigrationBatch(tx, options, expected is not null,
							StateBytes(expected), initial.Epoch, admission, budget);
					}, ct);
					expected?.Dispose();
					expected = candidate;
					candidate = null;
					if (WikiCategoryCheckpointCodec.Decode(expected!.Value, Store.MaxKeySize, ct).Phase == WikiCategoryMigrationPhase.Complete)
					{
						Volatile.Write(ref _wikiCategoryReadyEpoch, initial.Epoch);
						return;
					}
				}
				finally { candidate?.Dispose(); }
			}
		}
		finally { expected?.Dispose(); }
	}

	private static ReadOnlySpan<byte> StateBytes(NativeReadValue? state) => state is null ? ReadOnlySpan<byte>.Empty : state.Value;
	private static long CategoryArrayCharge(long length) => checked(32 + ((length + 7) & ~7L));

	private void ValidateWikiCategoryEnvironment()
	{
		WikiCategoryCheckpointCodec.ValidateEnvironmentMaxKeySize(Store.MaxKeySize);
		if (WikiCategoryCheckpointCodec.StateKey.Length > Store.MaxKeySize
			|| WikiCategoryCheckpointCodec.CompletionKey.Length > Store.MaxKeySize)
			throw new NotSupportedException("The native maximum key size cannot hold wiki category migration authority keys.");
	}

	internal NativeReadValue ApplyWikiCategoryMigrationBatch(ITx tx, WikiCategoryMigrationOptions options,
		bool hasExpectedState, ReadOnlySpan<byte> expectedState, long expectedEpoch,
		SemanticFixtureMutationLease? admission, NativeReadBudget budget)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(budget);
		// The old owner remains physically owned through commit. This budget admits that borrowed
		// owner, array and old reservation, plus the new baseline reservation's own overhead.
		using var baseline = budget.ReserveWorking(hasExpectedState ? checked(128 + CategoryArrayCharge(expectedState.Length)) : 0);
		using var fixedWork = budget.ReserveWorking(32768);
		ValidateWikiCategoryEnvironment();
		if (Store.Epoch != expectedEpoch) throw CategoryCheckpointConflict();
		var reader = (IWikiCategoryMigrationReadTx)tx;
		using var current = reader.ReadWikiCategoryState(budget) is NativeReadValue state ? state : null;
		using var marker = reader.ReadWikiCategoryCompletion(budget) is NativeReadValue completion ? completion : null;
		var complete = WikiCategoryAuthorityCodec.Validate(current is not null, StateBytes(current), marker is not null,
			StateBytes(marker), Store.MaxKeySize, budget.CancellationToken);
		if (hasExpectedState != (current is not null) || !expectedState.SequenceEqual(StateBytes(current)))
			throw CategoryCheckpointConflict();
		if (complete) return OwnCategoryCheckpoint(WikiCategoryCheckpointCodec.Decode(current!.Value, Store.MaxKeySize, budget.CancellationToken), budget);
		if (_fixtureLease is not null && admission?.Phase != SemanticFixturePhase.Inactive)
			throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
		RequireFixtureMutation(tx, admission, budget, fixedWork);
		if (current is null)
		{
			var begin = new WikiCategoryMigrationCheckpoint(WikiCategoryMigrationPhase.Clearing, 1, 0, 0, 0, [], [], [], [], 0, 0);
			return WriteCategoryCheckpoint(tx, begin, budget);
		}
		var prior = WikiCategoryCheckpointCodec.Decode(current.Value, Store.MaxKeySize, budget.CancellationToken);
		return prior.Phase == WikiCategoryMigrationPhase.Clearing
			? ClearCategoryBatch(tx, reader, options, prior, budget)
			: BuildCategoryBatch(tx, reader, options, prior, budget);
	}

	private NativeReadValue ClearCategoryBatch(ITx tx, IWikiCategoryMigrationReadTx reader,
		WikiCategoryMigrationOptions options, WikiCategoryMigrationCheckpoint prior, NativeReadBudget budget)
	{
		using var keys = ReadClearingCategoryKeys(reader, options, prior.LastClearedKey, budget);
		var next = new WikiCategoryMigrationCheckpoint(keys.Exhausted ? WikiCategoryMigrationPhase.Building : WikiCategoryMigrationPhase.Clearing,
			checked(prior.Revision + 1), checked(prior.ClearedRows + (ulong)keys.Count), 0, 0,
			keys.Exhausted ? [] : keys.Count == 0 ? prior.LastClearedKey : keys.KeyAt(keys.Count - 1), [], [], [], 0, 0);
		var owned = OwnCategoryCheckpoint(next, budget);
		try
		{
			for (var index = 0; index < keys.Count; index++)
			{
				budget.CancellationToken.ThrowIfCancellationRequested();
				tx.Delete(Tables.WikiByCategory, keys.KeyAt(index));
			}
			tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, owned.Value);
			return owned;
		}
		catch { owned.Dispose(); throw; }
	}

	private static WikiCategoryKeyBatch ReadClearingCategoryKeys(IWikiCategoryMigrationReadTx reader,
		WikiCategoryMigrationOptions options, ReadOnlySpan<byte> afterKey, NativeReadBudget budget)
	{
		try
		{
			return reader.ReadWikiCategoryKeys(Tables.WikiByCategory, afterKey,
				Math.Min(128, options.MaxIndexOps), Math.Min(options.MaxKeyBatchBytes, options.MaxDeltaBytes), budget);
		}
		catch (WikiCategoryMigrationCapacityException error) when (error.Dimension == "key-bytes"
			&& !error.RequiresEnvelopeReview && options.MaxDeltaBytes < options.MaxKeyBatchBytes
			&& error.ConfiguredBytes == options.MaxDeltaBytes && error.RequiredBytes > options.MaxDeltaBytes)
		{
			// Only the first-key local allowance refusal is attributable to the stricter delta cap.
			// Native hard-key-envelope failures retain their original setting and review requirement.
			throw new WikiCategoryMigrationCapacityException("delta-bytes", error.RequiredBytes, options.MaxDeltaBytes);
		}
	}

	private NativeReadValue BuildCategoryBatch(ITx tx, IWikiCategoryMigrationReadTx reader,
		WikiCategoryMigrationOptions options, WikiCategoryMigrationCheckpoint prior, NativeReadBudget budget)
	{
		using var keys = prior.CurrentSourceKey.IsEmpty
			? reader.ReadWikiCategoryKeys(Tables.WikiPage, prior.LastCompletedSourceKey, 1, options.MaxKeyBatchBytes, budget) : null;
		if (keys is { Count: 0, Exhausted: true })
		{
			var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var done = new WikiCategoryMigrationCheckpoint(WikiCategoryMigrationPhase.Complete, checked(prior.Revision + 1), prior.ClearedRows,
				prior.CompletedPages, prior.CategoryPuts, [], prior.LastCompletedSourceKey, [], [], 0, timestamp);
			using var markerWork = budget.ReserveWorking(8192);
			var marker = Codec.Serialize(new MigrationRecord { Id = WikiCategoryCheckpointCodec.MigrationId, AppliedUnixMs = timestamp });
			var owner = OwnCategoryCheckpoint(done, budget);
			try
			{
				tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, owner.Value);
				tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey, marker);
				return owner;
			}
			catch { owner.Dispose(); throw; }
		}
		var sourceKey = prior.CurrentSourceKey.IsEmpty ? keys!.KeyAt(0) : prior.CurrentSourceKey;
		using var source = reader.ReadWikiCategoryPage(sourceKey, options.MaxSourceBytes, budget) is NativeReadValue page
			? page : throw new InvalidDataException("Invalid wiki category migration source.");
		// Fixed cryptographic context and wrappers are covered by Apply's fixed working allowance.
		Span<byte> hash = stackalloc byte[32];
		using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
		{
			for (var offset = 0; offset < source.Value.Length;)
			{
				budget.CancellationToken.ThrowIfCancellationRequested();
				var count = Math.Min(4096, source.Value.Length - offset);
				hasher.AppendData(source.Value.AsSpan(offset, count));
				offset = checked(offset + count);
			}
			hasher.GetHashAndReset(hash);
		}
		if (!prior.CurrentSourceKey.IsEmpty && !hash.SequenceEqual(prior.CurrentSourceHash)) throw CategoryCheckpointConflict();
		using var projection = WikiCategoryProjectionDecoder.Decode(source.Value, budget)
			?? throw new InvalidDataException("Invalid wiki category migration projection.");
		if (projection.Title is null || projection.AuthorDbref is null)
			throw new InvalidDataException("Invalid wiki category migration projection.");
		for (var index = 0; index < projection.CategoryCount; index++)
		{
			budget.CancellationToken.ThrowIfCancellationRequested();
			if (projection.IsNullCategory(index)) throw new InvalidDataException("Invalid wiki category migration projection.");
		}
		var total = projection.DistinctCategoryCount;
		if (prior.NextCategoryOrdinal > int.MaxValue) throw new InvalidDataException("Invalid wiki category migration ordinal.");
		var ordinal = checked((int)prior.NextCategoryOrdinal);
		if (!prior.CurrentSourceKey.IsEmpty && ordinal >= total) throw new InvalidDataException("Invalid wiki category migration ordinal.");
		using var outputsWork = budget.ReserveWorking(checked(256 + CategoryArrayCharge(16L * options.MaxIndexOps)));
		var outputs = new (byte[] Key, byte[] Value)[options.MaxIndexOps];
		var countOutputs = 0;
		var delta = 0L;
		var pageId = unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(sourceKey));
		while (ordinal < total && countOutputs < options.MaxIndexOps)
		{
			budget.CancellationToken.ThrowIfCancellationRequested();
			var category = projection.CategoryAt(projection.DistinctCategoryOrdinalAt(ordinal));
			// Covers string, Upper's folded chars and geometrically grown byte buffers, copied
			// prefix, final key/value and tuple retention before invoking any allocating helper.
			outputsWork.ReserveAdditional(checked(4096 + 64L * category.Length + 8L * projection.Title.Length + 12L * projection.AuthorDbref.Length));
			var label = new string(category);
			var prefix = WikiCategoryValueCodec.LabelPrefix(label);
			var key = WikiCategoryValueCodec.Key(prefix, projection.Title, pageId);
			var value = WikiCategoryValueCodec.Value(prefix, projection.Published, projection.AuthorDbref);
			if (key.Length > Store.MaxKeySize) throw new NotSupportedException("A wiki category index key exceeds the native maximum key size.");
			var required = checked((long)key.Length + value.Length);
			if (required > options.MaxDeltaBytes - delta)
			{
				if (countOutputs == 0) throw new WikiCategoryMigrationCapacityException("delta-bytes", required, options.MaxDeltaBytes, required > 262144);
				break;
			}
			outputs[countOutputs++] = (key, value);
			delta = checked(delta + required);
			ordinal = checked(ordinal + 1);
		}
		var finished = ordinal == total;
		var next = new WikiCategoryMigrationCheckpoint(WikiCategoryMigrationPhase.Building, checked(prior.Revision + 1), prior.ClearedRows,
			checked(prior.CompletedPages + (finished ? 1UL : 0UL)), checked(prior.CategoryPuts + (ulong)countOutputs), [],
			finished ? sourceKey : prior.LastCompletedSourceKey, finished ? [] : sourceKey, finished ? [] : hash,
			finished ? 0 : checked((ulong)ordinal), 0);
		var owned = OwnCategoryCheckpoint(next, budget);
		try
		{
			for (var index = 0; index < countOutputs; index++)
			{
				budget.CancellationToken.ThrowIfCancellationRequested();
				tx.Put(Tables.WikiByCategory, outputs[index].Key, outputs[index].Value);
			}
			tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, owned.Value);
			return owned;
		}
		catch { owned.Dispose(); throw; }
	}

	private NativeReadValue OwnCategoryCheckpoint(WikiCategoryMigrationCheckpoint checkpoint, NativeReadBudget budget)
	{
		var length = WikiCategoryCheckpointCodec.EncodedLength(checkpoint, Store.MaxKeySize);
		var reservation = budget.ReserveWorking(checked(64 + CategoryArrayCharge(length)));
		try { return new NativeReadValue(WikiCategoryCheckpointCodec.Encode(checkpoint, Store.MaxKeySize, budget.CancellationToken), reservation); }
		catch { reservation.Dispose(); throw; }
	}

	private NativeReadValue WriteCategoryCheckpoint(ITx tx, WikiCategoryMigrationCheckpoint checkpoint, NativeReadBudget budget)
	{
		var owner = OwnCategoryCheckpoint(checkpoint, budget);
		try { tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, owner.Value); return owner; }
		catch { owner.Dispose(); throw; }
	}

	private static InvalidDataException CategoryCheckpointConflict() => new("Wiki category migration checkpoint conflict.");

	private void RequireWikiCategoryReady(ITx tx, NativeReadBudget? sharedBudget = null)
	{
		var budget = sharedBudget ?? new NativeReadBudget(16, 262144, 1048576, CancellationToken.None);
		var reader = (IWikiCategoryMigrationReadTx)tx;
		using var state = reader.ReadWikiCategoryState(budget) is NativeReadValue found ? found : null;
		using var marker = reader.ReadWikiCategoryCompletion(budget) is NativeReadValue completion ? completion : null;
		var complete = WikiCategoryAuthorityCodec.Validate(state is not null, StateBytes(state), marker is not null,
			StateBytes(marker), Store.MaxKeySize, budget.CancellationToken);
		if (!complete || Volatile.Read(ref _wikiCategoryReadyEpoch) != Store.Epoch)
			throw new InvalidOperationException("Wiki category migration is not ready.");
	}
}
