using System.Collections.Immutable;
using System.Security.Cryptography;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Tools.WikiSemantics.Native;

/// <summary>Tool-owned shadow aggregate. Construct only over an explicitly disposable fixture world.</summary>
public sealed class NativeFixtureStore
{
	private readonly ILightningStorageAccessor _storage;
	private readonly NativeTables _tables;
	private readonly string _schemaDigest;
	internal IReadOnlyList<TableDef> Tables => _tables.All;
	private static NativeReadBudget Budget(CancellationToken ct = default) => new(8192, 2 << 20, 4 << 20, ct);
	public NativeFixtureStore(ILightningStorageAccessor storage, SemanticSchema schema)
	{
		_storage = storage;
		var budget = Budget();
		using var reservation = budget.ReserveWorking(0);
		var size = NativeCodec.MeasureSchema(schema);
		reservation.ReserveAdditional(checked(2048 + size * 16L));
		// Own outer and nested backing arrays before producing the durable schema certificate.
		var frozen = schema with { Properties = schema.Properties.Select(p => p with { AllowedKeywords = ImmutableArray.Create(p.AllowedKeywords.AsSpan()) }).ToImmutableArray() };
		if (CanonicalFacts.Validate(frozen, []) is ValidationFailure failure) throw new ArgumentException(failure.Detail, nameof(schema));
		var schemaBytes = NativeCodec.SchemaIdentity(frozen);
		_schemaDigest = Convert.ToHexStringLower(SHA256.HashData(schemaBytes));
		if (schemaBytes.Length + 1 > NativeFixtureLimits.MaxReadBytes) throw new InvalidDataException("Schema is not readable in one bounded row.");
		_tables = new(storage);
		storage.WriteAsync(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, _tables.Projection, budget);
			var stored = work.ReadBytes(_tables.Metadata, work.Key(1));
			if (stored is not null)
			{
				if (!stored.AsSpan().SequenceEqual(schemaBytes)) throw new InvalidDataException("Exact fixture schema mismatch.");
				return true;
			}
			work.Delta.Put(_tables.Metadata, work.Key(1), schemaBytes);
			work.Delta.Put(_tables.Metadata, work.Key(2), work.Number(0)); // semantic commit sequence
			work.Delta.Put(_tables.Metadata, work.Key(3), work.Number(1)); // fixture generation
			work.Delta.Put(_tables.Metadata, work.Key(4), work.Number(1)); // fixture policy constant, not live auth
			work.Delta.Put(_tables.Metadata, work.Key(5), work.Number(1)); // fixture requirements constant
			foreach (var property in frozen.Properties)
			{
				work.Registry(WikiSemanticRegistryKind.Property, property.Id);
				foreach (var keyword in property.AllowedKeywords) work.Registry(WikiSemanticRegistryKind.Keyword, keyword.Value);
			}
			work.Delta.Apply(tx, default);
			return true;
		}).GetAwaiter().GetResult();
	}
	public async ValueTask<NativeCommitResult> CommitAsync(PreparedNativePage edit, CancellationToken ct = default)
	{
		// Exactly one submission. Await actual parent completion even if cancellation arrives late.
		return await _storage.WriteAsync(tx => Apply(tx, edit, ct), ct).ConfigureAwait(false);
	}
	public NativeCommitResult Apply(ITx tx, PreparedNativePage edit, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		if (edit.SchemaIdentityDigest != _schemaDigest) return new NativeUnavailable();
		if (tx is not IBoundedReadTx) return new NativeUnavailable();
		var budget = Budget(ct);
		using var lifetime = budget.ReserveWorking(0);
		lifetime.ReserveAdditional(256);
		var delta = new WikiSemanticPlannedDelta(lifetime, ct);
		using var work = WikiSemanticWork.Create(tx, _tables.Projection, budget, lifetime, delta);
		NativeCommitResult result;
		try { result = Compose(work, edit); }
		catch (WikiSemanticCapacityException e) { return new NativeCapacity(e.Dimension); }
		catch (OverflowException) { return new NativeCapacity("counter-overflow"); }
		// Every typed rejection happens above this boundary. Exceptions here must abort the writer.
		if (result is NativeCommitted) work.Delta.Apply(tx, ct);
		return result;
	}
	private NativeCommitResult Compose(WikiSemanticWork work, PreparedNativePage edit)
	{
		var mutationKey = work.Bytes(16, () => edit.MutationId.ToByteArray());
		var status = work.Read(_tables.Mutations, mutationKey, NativeStateCodec.Mutation);
		if (status is not null) return status.RequestDigest == edit.RequestDigest ? status.Committed : new NativeMutationMismatch();
		var result = WikiSemanticProjectionPlanner.PlanProjection(work, edit.Projection, edit.ExpectedStateVersion, new AllocateNextSequence());
		if (result is WikiProjectionConflict conflict) return new NativeConflict(conflict.CurrentStateVersion);
		if (result is not WikiProjectionPlanned planned) throw new InvalidOperationException("Unknown projection result.");
		var subjectKey = work.Bytes(8, () => NativeKeys.Subject(planned.NativeId));
		work.Delta.Put(_tables.Pages, subjectKey, work.Bytes(NativeCodec.MeasurePage(edit.Page), () => NativeCodec.EncodePage(edit.Page)));
		var committed = new NativeCommitted(edit.MutationId, planned.StateVersion, planned.CommitSequence);
		var receipt = new NativeMutationStatus(edit.MutationId, edit.RequestDigest, committed);
		work.Delta.Put(_tables.Mutations, mutationKey, work.Bytes(128, () => NativeStateCodec.Mutation(receipt)));
		return committed;
	}
	public Found<NativeStoredPage> ReadPage(PageRef page, NativeReadBudget budget, NativeReadReservation decodedLifetime)
		=> _storage.Read<Found<NativeStoredPage>>(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, _tables.Projection, budget, decodedLifetime);
			var id = work.Registry(WikiSemanticRegistryKind.Page, page.Id, false);
			if (id == 0) return new NotFound();
			var key = work.Bytes(8, () => NativeKeys.Subject(id));
			var state = work.Read(_tables.Summaries, key, NativeStateCodec.Summary);
			if (state is null) return new NotFound();
			var snapshot = work.Read(_tables.Pages, key, bytes => NativeCodec.DecodePage(bytes), 8, 1024) ?? throw new InvalidDataException("Missing fixture page.");
			var facts = work.Read(_tables.Facts, key, bytes => NativeCodec.DecodeFacts(bytes), 8, 1024) ?? throw new InvalidDataException("Missing fixture facts.");
			decodedLifetime.ReserveAdditional(128);
			return new NativeStoredPage(snapshot, facts, state.Summary, state.CommitSequence);
		});
	public Found<NativeMutationStatus> GetMutation(Guid mutationId, NativeReadBudget budget, NativeReadReservation decodedLifetime)
		=> _storage.Read<Found<NativeMutationStatus>>(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, _tables.Projection, budget, decodedLifetime);
			var status = work.Read(_tables.Mutations, work.Bytes(16, () => mutationId.ToByteArray()), NativeStateCodec.Mutation);
			return status is null ? new NotFound() : status;
		});
	internal NativeReadBatch Inspect(TableDef table, byte[] afterKey, NativeReadBudget budget)
	{
		if (!_tables.All.Contains(table)) throw new ArgumentException("Not a fixture-owned table.", nameof(table));
		return _storage.Read(tx => (tx as IBoundedReadTx ?? throw new NotSupportedException("Checked reads required.")).ReadRange(table, [], [], [], afterKey, budget));
	}
}
