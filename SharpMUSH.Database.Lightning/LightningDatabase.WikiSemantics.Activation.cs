using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

internal enum AtomicWikiActivationStage { PreflightComplete, LayoutMutationStarting, IncompleteCommitted, FirstTableOpened, BeforeFinalCommit }
internal sealed record AtomicWikiReadSectionObservation(int Records, int Probes, long Bytes, long WorkingBytes, bool Completed, string? LimitDimension);

public partial class LightningDatabase
{
	// Managed fixture-only barriers/faults; never malformed native data or a production activation API.
	internal Func<AtomicWikiActivationStage, Task>? AtomicActivationHook { get; set; }
	private AtomicWikiReadSectionObservation? _atomicWikiPreflightRead;
	private AtomicWikiReadSectionObservation? _atomicWikiFinalValidationRead;
	internal AtomicWikiReadSectionObservation? AtomicWikiPreflightRead => Volatile.Read(ref _atomicWikiPreflightRead);
	internal AtomicWikiReadSectionObservation? AtomicWikiFinalValidationRead => Volatile.Read(ref _atomicWikiFinalValidationRead);

	internal async ValueTask ActivateAtomicWikiProjectionAsync(SemanticFixtureCapability capability, CancellationToken ct)
	{
		if (_fixtureLease is not { } lease || !ReferenceEquals(lease.Owner, capability) || _fixtureClosed)
			throw SemanticFixtureCapability.Unavailable();
		capability.BeginActivation(lease);
		Volatile.Write(ref _atomicWikiPreflightRead, null);
		Volatile.Write(ref _atomicWikiFinalValidationRead, null);
		var layoutStarted = false;
		try
		{
			ct.ThrowIfCancellationRequested();
			try { Store.RequireNoAtomicLayout(capability.FixtureId); }
			catch (InvalidOperationException) { throw SemanticFixtureCapability.Unavailable(); }
			var budget = ActivationBudget(ct);
			using var lifetime = budget.ReserveWorking(2048 + 576 * 1024);
			// 512 KiB owns the final raw read section; 64 KiB owns fixed marker reads/decoding.
			var delta = new WikiSemanticPlannedDelta(lifetime, ct);
			try
			{
				AtomicBootstrapPlan plan;
				var preflightCompleted = false;
				string? preflightLimit = null;
				try
				{
					plan = Store.Read(tx => PlanAtomicBootstrap(tx, lease.FixtureId, budget, lifetime, delta, ct));
					preflightCompleted = true;
				}
				catch (NativeReadLimitException error) { preflightLimit = error.Dimension; throw; }
				finally
				{
					// Store.Read has released its transaction/gate; publish only immutable scalar evidence.
					Volatile.Write(ref _atomicWikiPreflightRead, ObserveReadSection(budget, preflightCompleted, preflightLimit));
				}
				if (AtomicActivationHook is { } preflightHook) await preflightHook(AtomicWikiActivationStage.PreflightComplete);
				ct.ThrowIfCancellationRequested();
				capability.LayoutMutationStarting();
				layoutStarted = true;
				if (AtomicActivationHook is { } startingHook) await startingHook(AtomicWikiActivationStage.LayoutMutationStarting);
				await Store.WriteAsync(tx =>
				{
					if (ReadActivation(tx, lease.FixtureId) is not null) throw SemanticFixtureCapability.Unavailable();
					tx.Put(Tables.Meta, AtomicWikiLayout.MarkerKey, plan.Incomplete);
				}, ct);
				if (AtomicActivationHook is { } markerHook) await markerHook(AtomicWikiActivationStage.IncompleteCommitted);
				for (var i = 0; i < AtomicWikiLayout.All.Length; i++)
				{
					ct.ThrowIfCancellationRequested();
					Store.OpenTableOwned(AtomicWikiLayout.All[i].Name, false, null);
					if (i == 0 && AtomicActivationHook is { } tableHook) await tableHook(AtomicWikiActivationStage.FirstTableOpened);
				}
				if (AtomicActivationHook is { } finalHook) await finalHook(AtomicWikiActivationStage.BeforeFinalCommit);
				var checkBudget = ActivationBudget(ct, 512 * 1024);
				var finalCompleted = false;
				string? finalLimit = null;
				try
				{
					await Store.WriteAsync(tx =>
					{
						var current = ReadActivation(tx, lease.FixtureId);
						if (current is null || current.Active || current != (plan.Certificate with { Active = false }))
							throw SemanticFixtureCapability.Unavailable();
						var snapshot = ReadBootstrapSnapshot(tx, checkBudget, null, decode: false);
						if (snapshot.Digest != plan.SnapshotDigest) throw SemanticFixtureCapability.Unavailable();
						delta.Apply(tx, ct);
					}, ct);
					finalCompleted = true;
				}
				catch (NativeReadLimitException error) { finalLimit = error.Dimension; throw; }
				finally
				{
					// Awaited actual parent completion/failure, never a detached waiter's observation.
					Volatile.Write(ref _atomicWikiFinalValidationRead, ObserveReadSection(checkBudget, finalCompleted, finalLimit));
				}
				capability.ActivationCommitted();
			}
			finally { delta.Clear(); }
		}
		catch (Exception error)
		{
			capability.ActivationFailed();
			if (layoutStarted) throw;
			if (error is NativeReadLimitException limit)
				throw new WikiFixtureMutationException(limit.Dimension == "working-bytes" ? WikiFixtureMutationFailure.Quota : WikiFixtureMutationFailure.WorkLimit);
			if (error is WikiSemanticCapacityException or OverflowException) throw new WikiFixtureMutationException(WikiFixtureMutationFailure.Quota);
			if (error is InvalidDataException or JsonException or ArgumentException) throw SemanticFixtureCapability.Unavailable();
			throw;
		}
	}

	private sealed record AtomicBootstrapPlan(AtomicWikiActivation Certificate, byte[] Incomplete, string SnapshotDigest);
	private sealed record BootstrapPage(long Key, WikiPageRecord Record);
	private sealed record BootstrapSnapshot(List<BootstrapPage> Pages, List<WikiRequirementSet> Requirements, string Digest, string RequirementsDigest);
	private static NativeReadBudget ActivationBudget(CancellationToken ct, long working = 4 * 1024 * 1024)
		=> new(1024, 128 * 1024, working, ct, maxRecords: 128);
	private static AtomicWikiReadSectionObservation ObserveReadSection(NativeReadBudget budget, bool completed, string? limitDimension)
		=> new(budget.Records, budget.Probes, budget.Bytes, budget.WorkingBytes, completed, limitDimension);

	private static AtomicBootstrapPlan PlanAtomicBootstrap(ITx tx, Guid fixtureId, NativeReadBudget budget,
		NativeReadReservation lifetime, WikiSemanticPlannedDelta delta, CancellationToken ct)
	{
		var snapshot = ReadBootstrapSnapshot(tx, budget, lifetime, decode: true);
		using var work = WikiSemanticWork.CreateEmptyBase(tx, AtomicWikiLayout.Tables, budget, lifetime, delta);
		var sequence = ExistingBootstrapSequence.AllocateForActivation(work);
		using var identities = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (var page in snapshot.Pages)
		{
			ct.ThrowIfCancellationRequested();
			var record = page.Record;
			var id = WikiPageId(page.Key);
			var categories = record.Categories ?? [];
			lifetime.ReserveAdditional(checked(4096 + 128L * (categories.Length + snapshot.Requirements.Count)
				+ categories.Sum(category => 32L + 2L * category.Length)));
			var rules = snapshot.Requirements.Where(set => AppliesToBootstrapPage(set, id, record))
				.Select(set => set.Target.ToString()).Order(StringComparer.Ordinal).ToImmutableArray();
			var projection = new WikiPageProjection(new(id), new LocalWikiSource(), record.Title, "game",
				new(record.Namespace, [.. categories], rules, record.Published ?? true, record.Published == false ? record.AuthorDbref : null));
			try { lifetime.ReserveAdditional(checked(4096 + 32L * WikiSemanticCodec.MeasureProjection(projection))); }
			catch (InvalidDataException) { throw new WikiSemanticCapacityException("bootstrap-projection"); }
			var prepared = PreparedWikiProjection.PrepareProjection(AtomicWikiLayout.Schema, projection, new(1, []), false, ct);
			if (prepared is ValidationFailure) throw new WikiSemanticCapacityException("bootstrap-projection");
			if (prepared is not PreparedWikiProjection edit) throw new InvalidDataException("Invalid bootstrap preparation.");
			if (WikiSemanticProjectionPlanner.PlanProjection(work, edit, 0, sequence) is not WikiProjectionPlanned planned)
				throw new InvalidDataException("Bootstrap identity conflict.");
			HashField(identities, work.Bytes(3L * id.Length, () => Encoding.UTF8.GetBytes(id)));
			HashField(identities, work.Number(planned.NativeId));
		}
		var certificate = new AtomicWikiActivation(true, fixtureId, AtomicWikiLayout.SchemaDigest, sequence.Sequence,
			snapshot.Pages.Count, Convert.ToHexStringLower(identities.GetHashAndReset()), snapshot.RequirementsDigest);
		var active = work.Bytes(512, () => AtomicWikiLayout.Encode(certificate));
		var incomplete = work.Bytes(512, () => AtomicWikiLayout.Encode(certificate with { Active = false }));
		delta.Put(AtomicWikiLayout.Metadata, AtomicWikiLayout.RootKey, active);
		delta.Put(AtomicWikiLayout.Metadata, [1], AtomicWikiLayout.SchemaBytes);
		delta.Put(AtomicWikiLayout.Metadata, AtomicWikiLayout.CertificateKey, active);
		delta.Put(Tables.Meta, AtomicWikiLayout.MarkerKey, active);
		return new(certificate, incomplete, snapshot.Digest);
	}

	private static bool AppliesToBootstrapPage(WikiRequirementSet set, string id, WikiPageRecord record)
		=> set.Required.TryGetValue(WikiAction.Read, out var scopes) && scopes.Count > 0 && (set.Target.Scope switch
		{
			WikiRuleScope.Namespace => set.Target.Key == record.Namespace,
			WikiRuleScope.Category => (record.Categories ?? []).Contains(set.Target.Key, StringComparer.Ordinal),
			WikiRuleScope.Page => set.Target.Key == id,
			_ => false
		});

	private static BootstrapSnapshot ReadBootstrapSnapshot(ITx tx, NativeReadBudget budget, NativeReadReservation? lifetime, bool decode)
	{
		var reader = (IBoundedReadTx)tx;
		var pages = new List<BootstrapPage>();
		var requirements = new List<WikiRequirementSet>();
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		using var requirementHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (var table in new[] { Tables.WikiPage, Tables.WikiSlug, Tables.WikiRequirement, Tables.WikiPin, Tables.WikiTr, Tables.PkgWiki })
		{
			budget.CancellationToken.ThrowIfCancellationRequested();
			HashField(hash, Encoding.UTF8.GetBytes(table.Name));
			using var rows = reader.ReadRange(table, [], [], [], [], budget);
			if (!rows.Exhausted) throw new NativeReadLimitException("records");
			foreach (var row in rows.Entries)
			{
				budget.CancellationToken.ThrowIfCancellationRequested();
				HashField(hash, row.Key); HashField(hash, row.Value);
				if (table == Tables.WikiTr || table == Tables.PkgWiki)
					throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
				if (table == Tables.WikiRequirement) { HashField(requirementHash, row.Key); HashField(requirementHash, row.Value); }
				if (!decode || table == Tables.WikiPin || table == Tables.WikiSlug) continue;
				lifetime!.ReserveAdditional(checked(2048 + 32L * row.Value.Length));
				if (table == Tables.WikiPage)
				{
					if (row.Key.Length != 8) throw new InvalidDataException("Invalid wiki page identity.");
					var record = Codec.Deserialize<WikiPageRecord>(row.Value) ?? throw new InvalidDataException("Invalid wiki row.");
					if (string.IsNullOrWhiteSpace(record.Title) || string.IsNullOrWhiteSpace(record.Namespace)
						|| string.IsNullOrWhiteSpace(record.Slug) || record.MarkdownSource is null || record.RenderedHtml is null || record.PlainText is null
						|| record.Categories is { Length: > 128 } || (record.Categories ?? []).Any(string.IsNullOrWhiteSpace))
						throw new InvalidDataException("Invalid wiki projection fields.");
					var key = Keys.ReadDbref(row.Key);
					if (key < 0 || record.RevisionNumber < 1) throw new InvalidDataException("Invalid wiki page identity or revision.");
					if (reader.ReadValue(Tables.WikiSlug, WikiSlugKey(record.Namespace, record.Slug), budget) is not NativeReadValue slug)
						throw new InvalidDataException("Missing wiki slug mapping.");
					using (slug)
						if (!slug.Value.AsSpan().SequenceEqual(row.Key)) throw new InvalidDataException("Inconsistent wiki slug mapping.");
					pages.Add(new(key, record));
				}
				else if (table == Tables.WikiRequirement)
				{
					var record = Codec.Deserialize<WikiRequirementRecord>(row.Value) ?? throw new InvalidDataException("Invalid requirement row.");
					if (!Enum.TryParse<WikiRuleScope>(record.Scope, true, out var scope) || !Enum.IsDefined(scope) || string.IsNullOrWhiteSpace(record.Key)
						|| record.Required is null || record.Required.Count > 4 || record.Required.Any(pair => pair.Value is null || pair.Value.Length > 128
						|| pair.Value.Any(string.IsNullOrWhiteSpace) || !Enum.TryParse<WikiAction>(pair.Key, true, out var action) || !Enum.IsDefined(action)))
						throw new InvalidDataException("Invalid requirements.");
					requirements.Add(MapWikiRequirement(record) ?? throw new InvalidDataException("Unknown requirement target."));
				}
			}
		}
		if (reader.ReadValue(Tables.Meta, Keys.Str("next_wiki"), budget) is NativeReadValue next)
		{
			using (next) { HashField(hash, next.Value); }
		}
		return new(pages, requirements, Convert.ToHexStringLower(hash.GetHashAndReset()), Convert.ToHexStringLower(requirementHash.GetHashAndReset()));
	}

	private static void HashField(IncrementalHash hash, ReadOnlySpan<byte> bytes)
	{
		Span<byte> length = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
		hash.AppendData(length); hash.AppendData(bytes);
	}

	private static AtomicWikiActivation? ReadActivation(ITx tx, Guid fixtureId, NativeReadBudget? sharedBudget = null, NativeReadReservation? lifetime = null)
	{
		var budget = sharedBudget ?? new NativeReadBudget(8, 2048, 32 * 1024, CancellationToken.None);
		if (((IBoundedReadTx)tx).ReadValue(Tables.Meta, AtomicWikiLayout.MarkerKey, budget) is not NativeReadValue marker) return null;
		using (marker)
		{
			lifetime?.ReserveAdditional(checked(2048 + 24L * marker.Value.Length));
			return AtomicWikiLayout.Decode(marker.Value, fixtureId);
		}
	}
}
