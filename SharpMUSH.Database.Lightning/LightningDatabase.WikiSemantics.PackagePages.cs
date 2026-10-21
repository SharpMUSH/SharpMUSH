using System.Text.Json;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// In an active issued fixture a package page's ownership row and its projection's source move together, in
/// the same job: a page a package owns projects as that package's portable source, and one it releases as the
/// game's own again. Facts, title and visibility are kept. A page already deleted only loses its row.
/// </summary>
public partial class LightningDatabase
{
	/// <summary>An active fixture takes a package's wiki ownership only from a live coordinated operation's flow.</summary>
	private SemanticFixtureMutationLease? EnterPackageWikiMutation()
	{
		var admission = EnterFixtureMutation(SemanticFixtureMutation.PackageWiki);
		if (admission is { Phase: SemanticFixturePhase.Active } && CurrentSemanticFixturePackageOperation is null)
		{
			admission.Dispose();
			throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
		}
		return admission;
	}

	private static PortableWikiSource PackagePageSource(string packageId, string ns, string slug) => new(new(packageId, ns, slug));

	/// <summary>
	/// Moves <paramref name="pageId"/>'s projection to <paramref name="source"/> when it is live and, if
	/// <paramref name="from"/> is given, currently projects from it. Returns whether it moved.
	/// </summary>
	private bool MovePackagePageSource(ITx tx, SemanticFixtureMutationLease admission, string pageId,
		WikiSemanticSource source, WikiSemanticSource? from, CancellationToken ct)
		=> ReprojectPage(tx, admission, pageId, source, from, ct);

	/// <summary>
	/// Plans <paramref name="pageId"/>'s projection again from its live page and the requirements now in this
	/// job, keeping its source unless <paramref name="source"/> names another. A page that is deleted, that does
	/// not project from <paramref name="from"/> when given, or whose source and visibility are unchanged is left
	/// alone. Facts and title are kept. Returns whether it was planned again.
	/// </summary>
	private bool ReprojectPage(ITx tx, SemanticFixtureMutationLease admission, string pageId,
		WikiSemanticSource? source, WikiSemanticSource? from, CancellationToken ct)
	{
		using var scope = new LegacyWikiPreparationScope(admission, ct);
		try
		{
			RequireWikiCategoryReady(tx, scope.Budget);
			using var work = CreateLegacyWork(tx, scope);
			RequireFixtureMutation(tx, admission, scope.Budget, scope.Lifetime);
			var schema = work.ReadBytes(AtomicWikiLayout.Metadata, work.Key(1));
			if (schema is null || !schema.AsSpan().SequenceEqual(AtomicWikiLayout.SchemaBytes)) throw new InvalidDataException("Incompatible projection schema.");
			if (!TryParseWikiPageId(pageId, out var key)) return false;
			var record = work.Read(Tables.WikiPage, LegacyBytes(work, 8, () => WikiPageKey(key)), bytes => DecodeLegacyPage(bytes), 32, 2048);
			if (record is null) return false;
			var id = WikiPageId(key);
			var nativeId = work.Registry(WikiSemanticRegistryKind.Page, id, false);
			var subjectKey = work.Bytes(8, () => WikiSemanticKeys.Subject(nativeId));
			var state = nativeId == 0 ? null : work.Read(work.Tables.Summaries, subjectKey, WikiSemanticStateCodec.ProjectionState);
			if (state is null) throw new InvalidDataException("Package page has no projection.");
			if (state.State.Summary.Deleted || from is { } expected && !SameSource(state.Source, expected)) return false;
			var target = source ?? state.Source;
			var rules = ReadLegacyProjectionRules(work, id, record);
			var visibility = new VisibilityDescriptor(record.Namespace, [.. record.Categories ?? []], rules, record.Published ?? true,
				record.Published == false ? record.AuthorDbref : null);
			if (SameSource(state.Source, target) && WikiSemanticStateCodec.Descriptor(PreparedWikiProjection.FreezeVisibility(visibility, ct))
				.AsSpan().SequenceEqual(WikiSemanticStateCodec.Descriptor(state.State.Summary.Visibility)))
				return false;
			var facts = work.Read(work.Tables.Facts, subjectKey, bytes => WikiSemanticCodec.DecodeFacts(bytes), 8, 1024)
				?? throw new InvalidDataException("Package page has no facts.");
			var projection = new WikiPageProjection(new(id), target, record.Title, "game", visibility);
			if (PreparedWikiProjection.PrepareProjection(AtomicWikiLayout.Schema, projection, facts, false, scope.Budget.CancellationToken)
				is not PreparedWikiProjection prepared)
				throw new WikiFixtureMutationException(WikiFixtureMutationFailure.Quota);
			if (WikiSemanticProjectionPlanner.PlanProjection(work, prepared, state.State.Summary.StateVersion, new AllocateNextSequence(), allowSourceMove: true)
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

	/// <summary>Releases every page a package row in <paramref name="rows"/> owns back to the game, in this job.</summary>
	private void ReleasePackagePages(ITx tx, SemanticFixtureMutationLease admission, IEnumerable<Records.PackageWikiPageRecord> rows, CancellationToken ct)
	{
		foreach (var row in rows)
		{
			ct.ThrowIfCancellationRequested();
			MovePackagePageSource(tx, admission, row.PageId, new LocalWikiSource(), PackagePageSource(row.PackageId, row.Namespace, row.Slug), ct);
		}
	}

	private static bool SameSource(WikiSemanticSource left, WikiSemanticSource right) => (left, right) switch
	{
		(LocalWikiSource, LocalWikiSource) => true,
		(PortableWikiSource a, PortableWikiSource b) => a == b,
		_ => false
	};
}
