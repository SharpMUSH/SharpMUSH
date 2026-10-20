using System.Text.Json;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>One page's canonical facts, as a fixture-only import commits them under <see cref="AtomicWikiLayout.Schema"/>.</summary>
internal sealed record SemanticFixtureFactImport(string PageId, CanonicalFactSet Facts);

public partial class LightningDatabase
{
	/// <summary>
	/// Commits canonical facts for existing pages of an active, settled issued fixture: one page per writer job,
	/// through the same projection planner as page edits, so its postings, forward rows, visibility class and
	/// partition move atomically with the page's summary. This is not author-visible: there is no authoring
	/// syntax and no package path. A later edit of the page drops its facts again, in the edit's own job.
	/// Returns how many pages it committed; a missing page is skipped and not counted.
	/// </summary>
	internal async ValueTask<int> ImportSemanticFixtureFactsAsync(IReadOnlyList<SemanticFixtureFactImport> pages, CancellationToken ct)
	{
		if (_fixtureLease is null || _fixtureClosed) throw SemanticFixtureCapability.Unavailable();
		var committed = 0;
		foreach (var page in pages)
		{
			ct.ThrowIfCancellationRequested();
			using var admission = EnterFixtureMutation(SemanticFixtureMutation.LegacyWiki);
			if (admission is not { Phase: SemanticFixturePhase.Active }) throw SemanticFixtureCapability.Unavailable();
			using var scope = new LegacyWikiPreparationScope(admission, ct);
			if (await Store.WriteAsync(tx => ImportPageFacts(tx, admission, scope, page), ct)) committed++;
		}
		return committed;
	}

	private bool ImportPageFacts(ITx tx, SemanticFixtureMutationLease admission, LegacyWikiPreparationScope scope, SemanticFixtureFactImport page)
	{
		try
		{
			RequireWikiCategoryReady(tx, scope.Budget);
			using var work = CreateLegacyWork(tx, scope);
			RequireFixtureMutation(tx, admission, scope.Budget, scope.Lifetime);
			if (!tx.TryGet(Tables.Meta, LightningStore.FixturePackageStateKeyBytes, out var marker)
				|| !LightningStore.IsSettledFixturePackageState(marker, admission.Owner.FixtureId))
				throw SemanticFixtureCapability.Unavailable();
			var schema = work.ReadBytes(AtomicWikiLayout.Metadata, work.Key(1));
			if (schema is null || !schema.AsSpan().SequenceEqual(AtomicWikiLayout.SchemaBytes)) throw new InvalidDataException("Incompatible projection schema.");
			if (!TryParseWikiPageId(page.PageId, out var key)) return false;
			var record = work.Read(Tables.WikiPage, LegacyBytes(work, 8, () => WikiPageKey(key)), bytes => DecodeLegacyPage(bytes), 32, 2048);
			if (record is null) return false;
			var pageId = WikiPageId(key);
			var rules = ReadLegacyProjectionRules(work, pageId, record);
			var projection = new WikiPageProjection(new(pageId), new LocalWikiSource(), record.Title, "game",
				new(record.Namespace, [.. record.Categories ?? []], rules, record.Published ?? true, record.Published == false ? record.AuthorDbref : null));
			if (PreparedWikiProjection.PrepareProjection(AtomicWikiLayout.Schema, projection, page.Facts, false, scope.Budget.CancellationToken)
				is not PreparedWikiProjection prepared)
				throw new WikiFixtureMutationException(WikiFixtureMutationFailure.Quota);
			var nativeId = work.Registry(WikiSemanticRegistryKind.Page, pageId, false);
			var state = nativeId == 0 ? null : work.Read(AtomicWikiLayout.Summaries,
				work.Bytes(8, () => WikiSemanticKeys.Subject(nativeId)), WikiSemanticStateCodec.ProjectionState);
			if (state is null) throw new InvalidDataException("Imported page has no projection.");
			if (WikiSemanticProjectionPlanner.PlanProjection(work, prepared, state.State.Summary.StateVersion, new AllocateNextSequence())
				is not WikiProjectionPlanned)
				throw SemanticFixtureCapability.Unavailable();
			work.Delta.Apply(tx, scope.Budget.CancellationToken, AtomicLegacyPutApplied, AtomicLegacyDeleteApplied);
			return true;
		}
		catch (NativeReadLimitException) { throw new WikiFixtureMutationException(WikiFixtureMutationFailure.WorkLimit); }
		catch (WikiSemanticCapacityException) { throw new WikiFixtureMutationException(WikiFixtureMutationFailure.Quota); }
		catch (OverflowException) { throw new WikiFixtureMutationException(WikiFixtureMutationFailure.Quota); }
		catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or KeyNotFoundException)
		{ throw SemanticFixtureCapability.Unavailable(); }
	}
}
