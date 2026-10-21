using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SharpMUSH.Database.Lightning.Migration;
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
	// Immutable prepared input observation outside native ownership; no callback can bypass planning.
	internal Action<PreparedLegacyWikiMutation>? AtomicLegacyPrepared { get; set; }
	// Test-friend diagnostics: scalars/table identity only, no transaction, keys or values escape.
	internal Action<string>? AtomicLegacyCapacityObserved { get; set; }
	internal Action<TableDef>? AtomicLegacyPutApplied { get; set; }
	internal Action<TableDef>? AtomicLegacyDeleteApplied { get; set; }
	private static T PrepareLegacyIntent<T>(Func<T> prepare)
	{
		try { return prepare(); }
		catch (Exception error) when (error is NativeReadLimitException or WikiSemanticCapacityException or OverflowException or ArgumentException)
		{ throw new WikiFixtureMutationException(WikiFixtureMutationFailure.Quota); }
	}
	private static Found<WikiPage> LegacyFreshPage(IntegratedWikiCommitResult result) => result switch
	{
		IntegratedWikiCommitted committed => committed.Page,
		NotFound => new NotFound(),
		_ => throw SemanticFixtureCapability.Unavailable()
	};
	// The internal reuse entry returns only the original compact receipt for a replay, never a current DTO.
	internal async ValueTask<IntegratedWikiCommitResult> CommitLegacyFixtureAsync(PreparedLegacyWikiMutation mutation, CancellationToken ct)
	{
		using var admission = EnterFixtureMutation(SemanticFixtureMutation.LegacyWiki);
		return await CommitLegacyFixtureAsync(mutation, admission, ct);
	}
	private async ValueTask<IntegratedWikiCommitResult> CommitLegacyFixtureAsync(PreparedLegacyWikiMutation mutation,
		SemanticFixtureMutationLease? admission, CancellationToken ct)
	{
		var scope = mutation.Scope;
		scope.RequireLive();
		if (admission is not { Phase: SemanticFixturePhase.Active } || !ReferenceEquals(scope.Owner, admission.Owner))
			throw SemanticFixtureCapability.Unavailable();
		using var cancellation = PrepareLegacyIntent(() => scope.ObserveParentCancellation(ct));
		AtomicLegacyPrepared?.Invoke(mutation);
		return await Store.WriteAsync(tx =>
		{
			// Authority failures must precede receipt replay and retain their readiness diagnostics.
			try { RequireWikiCategoryReady(tx, scope.Budget); }
			catch (NativeReadLimitException) { throw new WikiFixtureMutationException(WikiFixtureMutationFailure.WorkLimit); }
			using var work = CreateLegacyWork(tx, scope);
			IntegratedWikiCommitResult result;
			try
			{
				ct.ThrowIfCancellationRequested();
				RequireFixtureMutation(tx, admission, scope.Budget, scope.Lifetime);
				result = PlanLegacyFixtureMutation(tx, mutation, work);
			}
			catch (NativeReadLimitException) { throw new WikiFixtureMutationException(WikiFixtureMutationFailure.WorkLimit); }
			catch (WikiSemanticCapacityException error)
			{
				AtomicLegacyCapacityObserved?.Invoke(error.Dimension);
				throw new WikiFixtureMutationException(WikiFixtureMutationFailure.Quota);
			}
			catch (OverflowException) { throw new WikiFixtureMutationException(WikiFixtureMutationFailure.Quota); }
			catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or KeyNotFoundException)
			{ throw SemanticFixtureCapability.Unavailable(); }
			// No typed catch may turn a partially applied parent into a successful result.
			if (result is IntegratedWikiCommitted or IntegratedWikiDeleted)
				work.Delta.Apply(tx, scope.Budget.CancellationToken, AtomicLegacyPutApplied, AtomicLegacyDeleteApplied);
			return result;
		}, ct);
	}

	private IntegratedWikiCommitResult PlanLegacyFixtureMutation(ITx tx, PreparedLegacyWikiMutation mutation, WikiSemanticWork work)
	{
		work.Lifetime.ReserveAdditional(4096); // fixed planning objects, timestamp strings and bounded category-array overlap
		var receiptKey = work.Bytes(16, () => mutation.MutationId.ToByteArray());
		var receipt = work.Read(AtomicWikiLayout.Mutations, receiptKey, IntegratedWikiReceiptCodec.Decode);
		if (receipt is not null)
		{
			if (receipt.MutationId != mutation.MutationId) throw new InvalidDataException("Receipt identity differs from its key.");
			if (receipt.RequestDigest != mutation.RequestDigest || receipt.Kind != mutation.Kind)
				throw new WikiFixtureMutationException(WikiFixtureMutationFailure.MutationMismatch);
			if (mutation is PreparedLegacyWikiMutation.Delete delete && receipt.PageId != delete.Id)
				throw new InvalidDataException("Receipt names another delete target.");
			return new IntegratedWikiReplayed(receipt);
		}
		var schema = work.ReadBytes(AtomicWikiLayout.Metadata, work.Key(1));
		if (schema is null || !schema.AsSpan().SequenceEqual(AtomicWikiLayout.SchemaBytes)) throw new InvalidDataException("Incompatible projection schema.");
		if (mutation is PreparedLegacyWikiMutation.Delete deletion) return PlanLegacyFixtureDelete(tx, deletion, work);
		long key;
		WikiPageRecord? old = null;
		WikiPageRecord updated;
		WikiRevisionRecord? revision = null;
		if (mutation is PreparedLegacyWikiMutation.Create create)
		{
			var page = create.Page;
			var slugKey = LegacyBytes(work, 6L * (page.Namespace.Length + page.Slug.Length) + 32, () => WikiSlugKey(page.Namespace, page.Slug));
			if (work.ReadBytes(Tables.WikiSlug, slugKey) is not null) return new IntegratedWikiDuplicate(page.Namespace, page.Slug);
			var counterKey = LegacyBytes(work, 16, () => Keys.Str("next_wiki"));
			var counter = work.ReadBytes(Tables.Meta, counterKey);
			if (counter is not { Length: 8 }) throw new InvalidDataException("Invalid wiki counter.");
			key = Keys.ReadDbref(counter);
			if (key < 0) throw new InvalidDataException("Invalid wiki counter.");
			if (key == long.MaxValue) throw new WikiSemanticCapacityException("wiki-id");
			if (work.ReadBytes(Tables.WikiPage, LegacyBytes(work, 8, () => WikiPageKey(key))) is not null)
				throw new InvalidDataException("Allocated wiki identity already exists.");
			updated = new()
			{
				Slug = page.Slug, Title = page.Title, Namespace = page.Namespace, MarkdownSource = page.MarkdownSource,
				RenderedHtml = page.RenderedHtml, PlainText = page.PlainText, AuthorDbref = page.AuthorDbref,
				LastEditorDbref = page.LastEditorDbref, CreatedAt = WikiTimestamp(page.CreatedAt), UpdatedAt = WikiTimestamp(page.UpdatedAt),
				RevisionNumber = 1, Categories = create.Categories.ToArray(), Published = page.Published, SourceLocale = page.SourceLocale
			};
			work.Delta.Put(Tables.Meta, counterKey, LegacyBytes(work, 8, () => Keys.Dbref(key + 1)));
			work.Delta.Put(Tables.WikiSlug, slugKey, LegacyBytes(work, 8, () => Keys.Dbref(key)));
			revision = LegacyRevision(WikiPageId(key), updated, page.AuthorDbref, create.EditSummary, page.CreatedAt);
		}
		else
		{
			var id = mutation switch
			{
				PreparedLegacyWikiMutation.Body body => body.Id,
				PreparedLegacyWikiMutation.Title title => title.Id,
				PreparedLegacyWikiMutation.Metadata metadata => metadata.Id,
				_ => throw new InvalidDataException("Unknown legacy mutation.")
			};
			if (!TryParseWikiPageId(id, out key)) return new NotFound();
			old = work.Read(Tables.WikiPage, LegacyBytes(work, 8, () => WikiPageKey(key)), bytes => DecodeLegacyPage(bytes), 32, 2048);
			if (old is null) return new NotFound();
			updated = mutation switch
			{
				PreparedLegacyWikiMutation.Body body => old with
				{
					MarkdownSource = body.Value.Markdown,
					RenderedHtml = body.Value.Html,
					PlainText = body.Value.PlainText,
					LastEditorDbref = body.Editor,
					UpdatedAt = WikiTimestamp(body.At),
					RevisionNumber = checked(old.RevisionNumber + 1)
				},
				PreparedLegacyWikiMutation.Title title => old with { Title = title.Value },
				PreparedLegacyWikiMutation.Metadata metadata => old with { Categories = metadata.Categories.ToArray(), Published = metadata.Published },
				_ => throw new InvalidDataException("Unknown legacy mutation.")
			};
			if (mutation is PreparedLegacyWikiMutation.Body edit)
				revision = LegacyRevision(WikiPageId(key), updated, edit.Editor, edit.EditSummary, edit.At);
		}
		ValidateLegacyPage(updated);
		var pageId = WikiPageId(key);
		work.Lifetime.ReserveAdditional(checked(4096 + 128L * (updated.Categories?.Length ?? 0)));
		var rules = ReadLegacyProjectionRules(work, pageId, updated);
		var nativeId = work.Registry(WikiSemanticRegistryKind.Page, pageId, false);
		var state = nativeId == 0 ? null : work.Read(AtomicWikiLayout.Summaries,
			work.Bytes(8, () => WikiSemanticKeys.Subject(nativeId)), WikiSemanticStateCodec.ProjectionState);
		if ((old is null && state is not null) || (old is not null && state is null)) throw new InvalidDataException("Legacy/projection identity disagreement.");
		// An edit keeps the page's source: a package page stays the package's until its ownership row moves.
		var projection = new WikiPageProjection(new(pageId), state?.Source ?? new LocalWikiSource(), updated.Title, "game",
			new(updated.Namespace, [.. updated.Categories ?? []], rules, updated.Published ?? true, updated.Published == false ? updated.AuthorDbref : null));
		try { work.Lifetime.ReserveAdditional(checked(4096 + 32L * WikiSemanticCodec.MeasureProjection(projection))); }
		catch (InvalidDataException) { throw new WikiSemanticCapacityException("projection"); }
		var prepared = PreparedWikiProjection.PrepareProjection(AtomicWikiLayout.Schema, projection, new(AtomicWikiLayout.Schema.Version, []), false, work.Budget.CancellationToken);
		if (prepared is not PreparedWikiProjection editProjection) throw new WikiSemanticCapacityException("projection");
		if (WikiSemanticProjectionPlanner.PlanProjection(work, editProjection, state?.State.Summary.StateVersion ?? 0, new AllocateNextSequence()) is not WikiProjectionPlanned planned)
			throw SemanticFixtureCapability.Unavailable();
		PlanLegacyPage(work, key, old, updated);
		if (revision is not null)
		{
			var encoded = LegacyJson(work, revision);
			work.Delta.Put(Tables.WikiRev, LegacyBytes(work, 6L * pageId.Length + 32,
				() => WikiRevKey(pageId, string.Empty, revision.RevisionNumber)), encoded);
		}
		var committed = new IntegratedWikiReceipt(mutation.MutationId, mutation.RequestDigest, pageId,
			planned.StateVersion, planned.CommitSequence, updated.RevisionNumber, mutation.Kind);
		work.Delta.Put(AtomicWikiLayout.Mutations, receiptKey, work.Bytes(512, () => IntegratedWikiReceiptCodec.Encode(committed)));
		return new IntegratedWikiCommitted(committed, MapWikiPage(key, updated));
	}
	private static byte[] LegacyBytes(WikiSemanticWork work, long estimate, Func<byte[]> encode) => work.Bytes(estimate, encode);
	private static WikiSemanticWork CreateLegacyWork(ITx tx, LegacyWikiPreparationScope scope)
	{
		try { return WikiSemanticWork.Create(tx, AtomicWikiLayout.Tables, scope.Budget, scope.Lifetime); }
		catch (NativeReadLimitException) { throw new WikiFixtureMutationException(WikiFixtureMutationFailure.WorkLimit); }
	}
	private static WikiRevisionRecord LegacyRevision(string pageId, WikiPageRecord page, string editor, string? summary, DateTimeOffset at)
		=> new()
		{
			PageId = pageId, Locale = string.Empty, RevisionNumber = page.RevisionNumber, MarkdownSource = page.MarkdownSource,
			EditorDbref = editor, Timestamp = WikiTimestamp(at), EditSummary = summary
		};
	private static WikiPageRecord DecodeLegacyPage(byte[] bytes)
	{
		var page = Codec.Deserialize<WikiPageRecord>(bytes) ?? throw new InvalidDataException("Invalid wiki row.");
		ValidateLegacyPage(page);
		return page;
	}
	private static void ValidateLegacyPage(WikiPageRecord page)
	{
		if (string.IsNullOrWhiteSpace(page.Slug) || string.IsNullOrWhiteSpace(page.Namespace) || string.IsNullOrWhiteSpace(page.Title)
			|| page.MarkdownSource is null || page.RenderedHtml is null || page.PlainText is null || page.AuthorDbref is null
			|| page.LastEditorDbref is null || page.CreatedAt is null || page.UpdatedAt is null || page.RevisionNumber < 1
			|| page.Categories is { Length: > 128 } || (page.Categories ?? []).Any(string.IsNullOrWhiteSpace))
			throw new InvalidDataException("Malformed wiki row.");
	}
	private static ImmutableArray<string> ReadLegacyProjectionRules(WikiSemanticWork work, string id, WikiPageRecord page)
	{
		using var rows = work.Reader.ReadRange(Tables.WikiRequirement, [], [], [], [], work.Budget);
		if (!rows.Exhausted) throw new NativeReadLimitException("records");
		work.Lifetime.ReserveAdditional(checked(256 + 128L * rows.Entries.Length));
		var rules = ImmutableArray.CreateBuilder<string>();
		foreach (var row in rows.Entries)
		{
			work.Budget.CancellationToken.ThrowIfCancellationRequested();
			work.Lifetime.ReserveAdditional(checked(2048 + 32L * row.Value.Length));
			var record = Codec.Deserialize<WikiRequirementRecord>(row.Value) ?? throw new InvalidDataException("Invalid requirements.");
			if (!Enum.TryParse<WikiRuleScope>(record.Scope, true, out var kind) || !Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(record.Key)
				|| record.Required is null || record.Required.Count > 4 || record.Required.Any(pair => pair.Value is null || pair.Value.Length > 128
					|| pair.Value.Any(string.IsNullOrWhiteSpace) || !Enum.TryParse<WikiAction>(pair.Key, true, out var action) || !Enum.IsDefined(action)))
				throw new InvalidDataException("Invalid requirements.");
			var requirement = MapWikiRequirement(record) ?? throw new InvalidDataException("Invalid requirements.");
			if (AppliesToBootstrapPage(requirement, id, page)) rules.Add(requirement.Target.ToString());
		}
		return rules.Order(StringComparer.Ordinal).ToImmutableArray();
	}
	private static byte[] LegacyJson(WikiSemanticWork work, object record)
	{
		work.Lifetime.ReserveAdditional(1024); // fixed measurement arrays/enumerators before constructing them
		var (strings, scalars) = record switch
		{
			WikiPageRecord page => (new[] { page.Slug, page.Title, page.Namespace, page.MarkdownSource, page.RenderedHtml, page.PlainText,
				page.AuthorDbref, page.LastEditorDbref, page.CreatedAt, page.UpdatedAt, page.SourceLocale }.Concat(page.Categories ?? []), 14 + (page.Categories?.Length ?? 0)),
			WikiRevisionRecord revision => (new[] { revision.PageId, revision.Locale, revision.MarkdownSource, revision.EditorDbref, revision.Timestamp, revision.EditSummary }.AsEnumerable(), 7),
			_ => throw new InvalidDataException("Unsupported legacy record.")
		};
		long characters = 0;
		foreach (var value in strings) { work.Budget.CancellationToken.ThrowIfCancellationRequested(); characters = checked(characters + (value?.Length ?? 0)); }
		var upper = checked(1024 + 6 * characters + 64L * scalars);
		try { work.Lifetime.ReserveAdditional(checked(4096 + 16 * upper)); }
		catch (NativeReadLimitException) { throw new WikiSemanticCapacityException("json-working"); }
		var bytes = record switch { WikiPageRecord page => Codec.Serialize(page), WikiRevisionRecord revision => Codec.Serialize(revision), _ => throw new InvalidDataException() };
		if (bytes.LongLength > upper) throw new InvalidDataException("JSON encoder exceeded its reserved bound.");
		return bytes;
	}
	private static void PlanLegacyPage(WikiSemanticWork work, long key, WikiPageRecord? old, WikiPageRecord updated)
	{
		if (old is not null) PlanLegacyIndexes(work, key, old, false);
		work.Delta.Put(Tables.WikiPage, LegacyBytes(work, 8, () => WikiPageKey(key)), LegacyJson(work, updated));
		PlanLegacyIndexes(work, key, updated, true);
	}
	private static void PlanLegacyIndexes(WikiSemanticWork work, long key, WikiPageRecord page, bool add)
	{
		work.Lifetime.ReserveAdditional(checked(1024 + 128L * (page.Categories?.Length ?? 0)
			+ (page.Categories ?? []).Sum(category => 6L * category.Length)));
		var value = LegacyBytes(work, 1 + 3L * page.AuthorDbref.Length, () => WikiVisibilityValue(page));
		Emit(Tables.WikiRecent, LegacyBytes(work, 16, () => WikiRecentKey(key, page)));
		Emit(Tables.WikiByNamespace, LegacyBytes(work, 12 + 2L * (page.Namespace.Length + page.Slug.Length), () => WikiNamespaceKey(key, page)));
		var titleBytes = Encoding.BigEndianUnicode.GetByteCount(page.Title);
		var categoryPrefixLength = -1;
		byte[]? categoryValue = null;
		foreach (var category in WikiIndexCategories(page))
		{
			work.Budget.CancellationToken.ThrowIfCancellationRequested();
			// One encoder reservation covers both the temporary prefix and the retained key.
			var categoryKey = LegacyBytes(work, checked(16 + 6L * category.Length + 2L * page.Title.Length),
				() => WikiCategoryValueCodec.Key(WikiCategoryValueCodec.LabelPrefix(category), page.Title, key));
			if (add)
			{
				var prefixLength = categoryKey.Length - titleBytes - 10;
				// Value stores only prefix length, publication and author; equal lengths share bytes.
				if (categoryValue is null || prefixLength != categoryPrefixLength)
				{
					categoryValue = LegacyBytes(work, checked(9 + 3L * page.AuthorDbref.Length),
						() => WikiCategoryValueCodec.Value(categoryKey.AsSpan(0, prefixLength), page.Published, page.AuthorDbref));
					categoryPrefixLength = prefixLength;
				}
				work.Delta.Put(Tables.WikiByCategory, categoryKey, categoryValue);
			}
			else work.Delta.Delete(Tables.WikiByCategory, categoryKey);
		}
		void Emit(TableDef table, byte[] indexKey)
		{
			if (add) work.Delta.Put(table, indexKey, value); else work.Delta.Delete(table, indexKey);
		}
	}
}
