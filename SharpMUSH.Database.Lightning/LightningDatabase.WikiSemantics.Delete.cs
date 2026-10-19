using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	private IntegratedWikiCommitResult PlanLegacyFixtureDelete(ITx tx, PreparedLegacyWikiMutation.Delete mutation, WikiSemanticWork work)
	{
		if (!TryParseWikiPageId(mutation.Id, out var key)) return new NotFound();
		var pageId = WikiPageId(key);
		var pageKey = LegacyBytes(work, 8, () => WikiPageKey(key));
		var old = work.Read(Tables.WikiPage, pageKey, DecodeLegacyPage, 32, 2048);
		// Activation forbids these entire tables; every later writer remains fenced. Never silently
		// turn an unexpected row into an unsupported locale/package cascade.
		foreach (var table in new[] { Tables.WikiTr, Tables.PkgWiki })
		{
			using var forbidden = work.Reader.ReadRange(table, [], [], [], [], work.Budget);
			if (!forbidden.Exhausted) throw new NativeReadLimitException("records");
			if (!forbidden.Entries.IsEmpty) throw new InvalidDataException("Unsupported wiki ownership or translations.");
		}
		var nativeId = work.Registry(WikiSemanticRegistryKind.Page, pageId, false);
		if (nativeId == 0)
		{
			if (old is not null) throw new InvalidDataException("Missing integrated page identity.");
			CorroborateAbsentDeleteIdentity(work, pageId);
			return new NotFound();
		}
		RequireDeleteRegistryIdentity(work, WikiSemanticRegistryKind.Page, nativeId, pageId);
		var subjectKey = work.Bytes(8, () => WikiSemanticKeys.Subject(nativeId));
		var state = work.Read(AtomicWikiLayout.Summaries, subjectKey, WikiSemanticStateCodec.ProjectionState)
			?? throw new InvalidDataException("Missing integrated summary.");
		var facts = work.Read(AtomicWikiLayout.Facts, subjectKey, bytes => WikiSemanticCodec.DecodeFacts(bytes), 8, 1024)
			?? throw new InvalidDataException("Missing integrated facts.");
		var summary = state.State.Summary;
		var collection = work.Registry(WikiSemanticRegistryKind.Collection, "game", false);
		if (collection == 0) throw new InvalidDataException("Missing integrated collection.");
		RequireDeleteRegistryIdentity(work, WikiSemanticRegistryKind.Collection, collection, "game");
		if (state.Source is not LocalWikiSource || summary.Reference.Id != pageId || summary.NativeId != nativeId
			|| summary.Collection != "game" || state.State.CollectionId != collection || state.State.ClassId != 0
			|| facts.SchemaVersion != AtomicWikiLayout.Schema.Version || facts.Facts.IsDefault || !facts.Facts.IsEmpty
			|| summary.StateVersion <= 0 || state.State.CommitSequence <= 0 || summary.StateVersion > state.State.CommitSequence
			|| (ulong)state.State.CommitSequence > work.Counter(work.Key(2)))
			throw new InvalidDataException("Incompatible integrated delete state.");
		var projection = new WikiPageProjection(summary.Reference, state.Source, summary.Title, summary.Collection, summary.Visibility);
		work.Lifetime.ReserveAdditional(checked(4096 + 32L * WikiSemanticCodec.MeasureProjection(projection)));
		var preparation = PreparedWikiProjection.PrepareProjection(AtomicWikiLayout.Schema, projection, new(AtomicWikiLayout.Schema.Version, []), true,
			work.Budget.CancellationToken);
		if (preparation is not PreparedWikiProjection prepared) throw new InvalidDataException("Invalid integrated tombstone descriptor.");
		if (old is null)
		{
			if (!summary.Deleted) throw new InvalidDataException("Live integrated target lacks its legacy row.");
			return new NotFound();
		}
		if (summary.Deleted) throw new InvalidDataException("Deleted integrated target has a legacy row.");
		work.Lifetime.ReserveAdditional(checked(1024 + 16L * (old.Categories?.Length ?? 0)));
		var expected = new VisibilityDescriptor(old.Namespace, [.. old.Categories ?? []], ReadLegacyProjectionRules(work, pageId, old),
			old.Published ?? true, old.Published == false ? old.AuthorDbref : null);
		work.Lifetime.ReserveAdditional(checked(2048 + 16L * WikiSemanticCodec.DescriptorSize(expected)));
		var expectedDescriptor = WikiSemanticStateCodec.Descriptor(PreparedWikiProjection.FreezeVisibility(expected, work.Budget.CancellationToken));
		var actualDescriptor = work.Bytes(WikiSemanticCodec.DescriptorSize(summary.Visibility) + 128L,
			() => WikiSemanticStateCodec.Descriptor(summary.Visibility));
		if (summary.Title != old.Title || !expectedDescriptor.AsSpan().SequenceEqual(actualDescriptor))
			throw new InvalidDataException("Legacy page and integrated descriptor disagree.");
		var slugKey = LegacyBytes(work, 6L * (old.Namespace.Length + old.Slug.Length) + 32, () => WikiSlugKey(old.Namespace, old.Slug));
		var slug = work.ReadBytes(Tables.WikiSlug, slugKey);
		if (slug is not { Length: 8 } || Keys.ReadDbref(slug) != key)
			throw new InvalidDataException("Slug names another immutable target.");
		// Read the complete prefix before contributing any mutation. Delta.Delete owns each copied
		// key before the borrowed native batch is released; history values count without decoding.
		var prefix = LegacyBytes(work, 6L * pageId.Length + 16, () => WikiPagePrefix(pageId));
		using (var history = work.Reader.ReadRange(Tables.WikiRev, prefix, [], [], [], work.Budget))
		{
			if (!history.Exhausted) throw new NativeReadLimitException("records");
			foreach (var row in history.Entries)
			{
				work.Budget.CancellationToken.ThrowIfCancellationRequested();
				work.Delta.Delete(Tables.WikiRev, row.Key);
			}
		}
		if (WikiSemanticProjectionPlanner.PlanProjection(work, prepared, summary.StateVersion, new AllocateNextSequence()) is not WikiProjectionPlanned planned)
			throw SemanticFixtureCapability.Unavailable();
		PlanLegacyIndexes(work, key, old, false);
		work.Delta.Delete(Tables.WikiSlug, slugKey);
		work.Delta.Delete(Tables.WikiPage, pageKey);
		work.Delta.Delete(Tables.WikiRequirement, LegacyBytes(work, 6L * pageId.Length + 32,
			() => WikiRequirementKey(WikiRuleScope.Page, pageId)));
		var receipt = new IntegratedWikiReceipt(mutation.MutationId, mutation.RequestDigest, pageId,
			planned.StateVersion, planned.CommitSequence, old.RevisionNumber, WikiLegacyMutationKind.Delete);
		work.Delta.Put(AtomicWikiLayout.Mutations, work.Bytes(16, () => mutation.MutationId.ToByteArray()),
			work.Bytes(512, () => IntegratedWikiReceiptCodec.Encode(receipt)));
		return new IntegratedWikiDeleted(receipt);
	}

	private static void RequireDeleteRegistryIdentity(WikiSemanticWork work, WikiSemanticRegistryKind kind, ulong id, string identity)
	{
		var reverse = work.Read(AtomicWikiLayout.Registry, work.Bytes(9, () => WikiSemanticKeys.RegistryReverse(kind, id)), WikiSemanticStateCodec.Registry);
		if (reverse is null || reverse.Id != id || reverse.Identity != identity) throw new InvalidDataException("Incompatible reverse identity.");
	}

	private static void CorroborateAbsentDeleteIdentity(WikiSemanticWork work, string pageId)
	{
		using (var identities = work.Reader.ReadRange(AtomicWikiLayout.Registry, [(byte)WikiSemanticRegistryKind.Page], [], [], [], work.Budget))
		{
			if (!identities.Exhausted) throw new NativeReadLimitException("records");
			foreach (var row in identities.Entries)
			{
				work.Lifetime.ReserveAdditional(checked(2048 + 24L * row.Value.Length));
				if (WikiSemanticStateCodec.Registry(row.Value).Identity == pageId) throw new InvalidDataException("Orphaned page identity.");
			}
		}
		using var summaries = work.Reader.ReadRange(AtomicWikiLayout.Summaries, [], [], [], [], work.Budget);
		if (!summaries.Exhausted) throw new NativeReadLimitException("records");
		foreach (var row in summaries.Entries)
		{
			work.Lifetime.ReserveAdditional(checked(2048 + 24L * row.Value.Length));
			if (WikiSemanticStateCodec.ProjectionState(row.Value).State.Summary.Reference.Id == pageId)
				throw new InvalidDataException("Orphaned integrated target.");
		}
	}
}
