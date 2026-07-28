using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds <c>node_wiki_translations</c> — per-locale overlay rows hanging off a wiki page — backfills
/// <c>WikiPage.SourceLocale</c> and <c>WikiRevision.Locale</c>, and replaces the revision index with a
/// unique one over <c>(PageId, Locale, RevisionNumber)</c>.
/// </summary>
/// <remarks>
/// A separate migration rather than an edit to <see cref="Migration_AddWiki"/>: that migration guards
/// its entire index block behind a collection-existence check, so on any database created before today
/// an edit there would silently never run.
/// <para>
/// The backfill must run <b>before</b> the unique index is created, or creation fails on rows whose
/// <c>Locale</c> is null.
/// </para>
/// </remarks>
public class Migration_AddWikiTranslations : IArangoMigration
{
	public long Id => 20260726_001;

	public string Name => "add_wiki_translations";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.WikiTranslations))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.WikiTranslations,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							PageId = new { type = DatabaseConstants.TypeString },
							Locale = new { type = DatabaseConstants.TypeString },
							Title = new { type = DatabaseConstants.TypeString },
							MarkdownSource = new { type = DatabaseConstants.TypeString },
							RenderedHtml = new { type = DatabaseConstants.TypeString },
							PlainText = new { type = DatabaseConstants.TypeString },
							LastEditorDbref = new { type = DatabaseConstants.TypeString },
							Published = new { type = DatabaseConstants.TypeBoolean },
							RevisionNumber = new { type = DatabaseConstants.TypeNumber }
						},
						required = (string[])["PageId", "Locale", "Title", "MarkdownSource"],
						additionalProperties = true
					}
				}
			});

			// One translation per (page, locale). This is the constraint that makes
			// UpsertTranslationAsync an upsert rather than an append.
			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiTranslations, new ArangoIndex
			{
				Fields = ["PageId", "Locale"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});

			// Non-unique, for "which pages have a French translation?" listings and admin coverage.
			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiTranslations, new ArangoIndex
			{
				Fields = ["Locale"],
				Type = ArangoIndexType.Persistent
			});
		}

		// ---- Backfill, BEFORE the unique index ------------------------------
		//
		// Order matters: a unique index over (PageId, Locale, RevisionNumber) cannot be created while
		// pre-existing revision rows have no Locale at all.

		// Every page that predates the field is stamped once. After this the value is authoritative and
		// immutable per page; nothing re-derives it on read, because an admin later changing
		// wiki_default_locale must not relabel the authored locale of pages that already exist.
		var stampedPages = await migrator.Context.Query.ExecuteAsync<string>(handle,
			"""
			FOR p IN @@c
				FILTER p.SourceLocale == null OR p.SourceLocale == ""
				UPDATE p WITH { SourceLocale: @locale } IN @@c
				RETURN NEW._key
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiPages },
				{ "locale", WikiOptions.DefaultLocaleFallback }
			});

		// Pre-existing revisions are all source-locale revisions, and the source stream's marker is the
		// empty string (Task 5, convention 1) — NOT the default locale. Stamping it explicitly rather than
		// leaving null is what lets the unique index cover the column.
		var stampedRevisions = await migrator.Context.Query.ExecuteAsync<string>(handle,
			"""
			FOR r IN @@c
				FILTER r.Locale == null
				UPDATE r WITH { Locale: "" } IN @@c
				RETURN NEW._key
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions }
			});

		// The migration logs the locale it stamped and the row counts. That is the whole mitigation: there
		// is deliberately no rollback path, no language detection and no per-page override, because
		// SharpMUSH is pre-production and wiping + reseeding is acceptable recovery. Revisit only if a live
		// game with existing wiki content ever adopts SharpMUSH.
		Console.WriteLine(
			$"[{Name}] stamped SourceLocale='{WikiOptions.DefaultLocaleFallback}' on "
			+ $"{stampedPages.Count} page(s); stamped Locale='' on "
			+ $"{stampedRevisions.Count} revision(s).");

		// ---- Revision constraint --------------------------------------------
		//
		// The deployed index is Fields = ["PageId", "RevisionNumber"], Persistent, NOT unique
		// (Migration_AddWiki.cs:101-105). Translation revisions restart numbering at 1, so that pair is no
		// longer unique — but a *non*-unique index means a numbering bug passes silently here while failing
		// loudly on SurrealDB. Both halves matter: add the unique three-field index, then drop the old pair
		// so nothing keeps writing against a constraint-free lookup.
		await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiRevisions, new ArangoIndex
		{
			Name = "wiki_revision_page_locale_rev",
			Fields = ["PageId", "Locale", "RevisionNumber"],
			Unique = true,
			Type = ArangoIndexType.Persistent
		});

		// Cleanup, not load-bearing: the new unique index is what enforces correctness. A redundant
		// non-unique lookup index would cost write throughput, not correctness.
		var existingIndexes = await migrator.Context.Index.ListAsync(handle, DatabaseConstants.WikiRevisions);
		foreach (var stale in existingIndexes.Where(i =>
			i.Type == ArangoIndexType.Persistent
			&& i.Fields is ["PageId", "RevisionNumber"]))
		{
			await migrator.Context.Index.DropAsync(handle, stale.Id!);
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
