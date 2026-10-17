using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;

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
			using var work = NativeWork.Create(tx, _tables, budget);
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
				work.Registry(NativeRegistryKind.Property, property.Id);
				foreach (var keyword in property.AllowedKeywords) work.Registry(NativeRegistryKind.Keyword, keyword.Value);
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
		using var work = NativeWork.Create(tx, _tables, Budget(ct));
		NativeCommitResult result;
		try { result = Plan(work, edit, ct); }
		catch (NativeCapacityException e) { return new NativeCapacity(e.Dimension); }
		catch (OverflowException) { return new NativeCapacity("counter-overflow"); }
		// Every typed rejection happens above this boundary. Exceptions here must abort the writer.
		if (result is NativeCommitted) work.Delta.Apply(tx, ct);
		return result;
	}
	private NativeCommitResult Plan(NativeWork work, PreparedNativePage edit, CancellationToken ct)
	{
		var mutationKey = work.Bytes(16, () => edit.MutationId.ToByteArray());
		var status = work.Read(_tables.Mutations, mutationKey, NativeStateCodec.Mutation);
		if (status is not null) return status.RequestDigest == edit.RequestDigest ? status.Committed : new NativeMutationMismatch();
		var subject = work.Registry(NativeRegistryKind.Page, edit.Page.Reference.Id, false);
		NativeSummaryState? old = subject == 0 ? null : work.Read(_tables.Summaries, work.Bytes(8, () => NativeKeys.Subject(subject)), NativeStateCodec.Summary);
		var currentVersion = old?.Summary.StateVersion ?? 0;
		if (currentVersion != edit.ExpectedStateVersion) return new NativeConflict(currentVersion);
		if (old?.Summary.Deleted == true) return new NativeConflict(currentVersion);
		subject = subject == 0 ? work.Registry(NativeRegistryKind.Page, edit.Page.Reference.Id) : subject;
		var subjectKey = work.Bytes(8, () => NativeKeys.Subject(subject));
		var oldFacts = old is null ? null : work.Read(_tables.Facts, subjectKey, bytes => NativeCodec.DecodeFacts(bytes), 8, 1024);
		if (old is not null && oldFacts is null) throw new InvalidDataException("Missing canonical fact bundle.");
		var collection = work.Registry(NativeRegistryKind.Collection, edit.Page.Collections[0]);
		var classId = NativeVisibilityCatalog.Move(work, old, edit.Page, collection, !edit.Facts.Facts.IsEmpty && !edit.Deleted);
		if (oldFacts is not null)
			foreach (var fact in oldFacts.Facts) { ct.ThrowIfCancellationRequested(); Index(work, subject, old!.CollectionId, old.ClassId, fact, true); }
		foreach (var fact in edit.Facts.Facts) { ct.ThrowIfCancellationRequested(); Index(work, subject, collection, classId, fact, false); }
		var version = checked(currentVersion + 1);
		var sequenceValue = work.Counter(work.Key(2));
		if (sequenceValue >= long.MaxValue) throw new NativeCapacityException("commit-sequence");
		var sequence = checked((long)sequenceValue + 1);
		var summary = new NativePageSummary(edit.Page.Reference, subject, edit.Page.Title, edit.Page.Visibility, edit.Page.Collections[0], edit.Deleted, version);
		var state = new NativeSummaryState(summary, collection, classId, sequence);
		var pageBytes = work.Bytes(NativeCodec.MeasurePage(edit.Page), () => NativeCodec.EncodePage(edit.Page));
		var factBytes = work.Bytes(NativeCodec.MeasureFacts(edit.Facts, NativeFixtureLimits.MaxFactBytes), () => NativeCodec.EncodeFacts(edit.Facts));
		var summaryBytes = work.Bytes(NativeCodec.SummarySize(edit.Page) + 128L, () => NativeStateCodec.Summary(state));
		if (summaryBytes.Length > NativeFixtureLimits.MaxSummaryBytes) throw new NativeCapacityException("summary-bytes");
		work.Delta.Put(_tables.Pages, subjectKey, pageBytes);
		work.Delta.Put(_tables.Facts, subjectKey, factBytes);
		work.Delta.Put(_tables.Summaries, subjectKey, summaryBytes);
		if (old is { ClassId: > 0 }) BumpPartition(work, old.CollectionId, old.ClassId);
		if (classId > 0 && (old is null || old.ClassId != classId || old.CollectionId != collection)) BumpPartition(work, collection, classId);
		work.Delta.Put(_tables.Metadata, work.Key(2), work.Number((ulong)sequence));
		var committed = new NativeCommitted(edit.MutationId, version, sequence);
		var receipt = new NativeMutationStatus(edit.MutationId, edit.RequestDigest, committed);
		work.Delta.Put(_tables.Mutations, mutationKey, work.Bytes(128, () => NativeStateCodec.Mutation(receipt)));
		return committed;
	}
	private void Index(NativeWork work, ulong subject, ulong collection, ulong classId, SemanticFact fact, bool remove)
	{
		if (classId == 0) throw new InvalidDataException("Fact without source class.");
		var property = work.Registry(NativeRegistryKind.Property, fact.Property, !remove);
		if (property == 0) throw new InvalidDataException("Missing property identity.");
		NativeScalarTag tag;
		byte[] value;
		switch (fact.Value)
		{
			case PageRef page:
				tag = NativeScalarTag.PageRef;
				var target = work.Registry(NativeRegistryKind.Page, page.Id, !remove);
				if (target == 0) throw new InvalidDataException("Missing target identity.");
				value = work.Number(target); break;
			case long integer: tag = NativeScalarTag.Integer; value = work.Bytes(8, () => NativeKeys.EncodeInteger(integer)); break;
			case bool boolean: tag = NativeScalarTag.Boolean; value = work.Number(boolean ? 1UL : 0UL); break;
			case Keyword keyword:
				tag = NativeScalarTag.Keyword;
				var keywordId = work.Registry(NativeRegistryKind.Keyword, keyword.Value, !remove);
				if (keywordId == 0) throw new InvalidDataException("Missing keyword identity.");
				value = work.Number(keywordId); break;
			default: throw new InvalidDataException("Unknown semantic scalar.");
		}
		var forward = work.Bytes(25, () => NativeKeys.Forward(subject, property, tag, value));
		var posting = work.Bytes(41, () => NativeKeys.Posting(collection, classId, property, tag, value, subject));
		if (remove) { work.Delta.Delete(_tables.Forward, forward); work.Delta.Delete(_tables.Postings, posting); }
		else { work.Delta.Put(_tables.Forward, forward, []); work.Delta.Put(_tables.Postings, posting, []); }
	}
	private void BumpPartition(NativeWork work, ulong collection, ulong classId)
	{
		var key = work.Pair(collection, classId);
		var old = work.ReadBytes(_tables.Partitions, key);
		if (old is not null && old.Length != 8) throw new InvalidDataException("Malformed partition version.");
		var version = old is null ? 0 : BinaryPrimitives.ReadUInt64BigEndian(old);
		work.Delta.Put(_tables.Partitions, key, work.Number(checked(version + 1)));
	}
	public Found<NativeStoredPage> ReadPage(PageRef page, NativeReadBudget budget, NativeReadReservation decodedLifetime)
		=> _storage.Read<Found<NativeStoredPage>>(tx =>
		{
			using var work = NativeWork.Create(tx, _tables, budget, decodedLifetime);
			var id = work.Registry(NativeRegistryKind.Page, page.Id, false);
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
			using var work = NativeWork.Create(tx, _tables, budget, decodedLifetime);
			var status = work.Read(_tables.Mutations, work.Bytes(16, () => mutationId.ToByteArray()), NativeStateCodec.Mutation);
			return status is null ? new NotFound() : status;
		});
	internal NativeReadBatch Inspect(TableDef table, byte[] afterKey, NativeReadBudget budget)
	{
		if (!_tables.All.Contains(table)) throw new ArgumentException("Not a fixture-owned table.", nameof(table));
		return _storage.Read(tx => (tx as IBoundedReadTx ?? throw new NotSupportedException("Checked reads required.")).ReadRange(table, [], [], [], afterKey, budget));
	}
}
