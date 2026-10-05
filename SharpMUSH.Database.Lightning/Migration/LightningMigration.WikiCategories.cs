using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Moves the wiki from category-as-identity to MediaWiki-style categories, once. A page was keyed
/// <c>(namespace, category, slug)</c> and carried a separate tag list; it is now keyed <c>(namespace, slug)</c>
/// and holds a list of categories.
/// </summary>
/// <remarks>
/// For every page, the old category (unless it was the default <c>general</c>) and every tag become its
/// category list; the text is not touched. Every page and translation body is rendered again, because the
/// stored HTML holds links in the old <c>/wiki/{ns}/{category}/{slug}</c> shape. Two pages that shared a slug in one namespace under
/// different categories cannot both keep it: the later one is renamed <c>{slug}_{category}</c>. The slug index
/// and the list indexes are rebuilt from the result, all in one write job with the marker.
/// </remarks>
public partial class LightningDatabase
{
	internal const string WikiCategoriesMigrationId = "0012_wiki_categories";

	private const string RetiredWikiTagTable = "wiki.tag";

	private async ValueTask MoveWikiCategoriesIntoTextAsync(CancellationToken cancellationToken)
	{
		var marker = Keys.Str("mig:" + WikiCategoriesMigrationId);
		if (Store.Read(tx => tx.TryGet(Tables.Meta, marker, out _))) return;

		// The tag index's table is no longer declared; a world that has wiki pages may still hold one.
		var hasPages = Store.Read(tx => tx.Range(Tables.WikiPage, []).Any());
		var retiredTags = hasPages ? Store.OpenTable(RetiredWikiTagTable, duplicates: false) : null;
		var renderer = new WikiMarkdigPipeline();

		await Store.WriteAsync(tx =>
		{
			if (tx.TryGet(Tables.Meta, marker, out _)) return;

			var pages = tx.Range(Tables.WikiPage, [])
				.Select(entry => (Key: Keys.ReadDbref(entry.Key), Record: Codec.Deserialize<WikiPageRecord>(entry.Value),
					Filed: Codec.Deserialize<WikiPageFiledRecord>(entry.Value)))
				.ToList();

			tx.DeletePrefix(Tables.WikiSlug, []);
			tx.DeletePrefix(Tables.WikiRecent, []);
			tx.DeletePrefix(Tables.WikiByNamespace, []);
			tx.DeletePrefix(Tables.WikiByCategory, []);
			if (retiredTags is not null) tx.DeletePrefix(retiredTags, []);

			var now = DateTimeOffset.UtcNow;
			foreach (var (key, record, filed) in pages)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var pageId = WikiPageId(key);
				var filedUnder = WikiHelpers.CategoryKey(filed.Category);
				var moved = WikiHelpers.NormalizeCategories(
					[.. filedUnder == "general" ? [] : new[] { filedUnder }, .. filed.Tags ?? []]);

				var markdown = record.MarkdownSource;
				var slug = record.Slug;
				for (var suffix = filedUnder.Length > 0 ? filedUnder : "page"; tx.TryGet(Tables.WikiSlug, WikiSlugKey(record.Namespace, slug), out _);)
				{
					slug = $"{slug}_{suffix}";
				}

				var updated = record with
				{
					Slug = slug,
					RenderedHtml = renderer.RenderToHtml(markdown),
					Categories = [.. moved],
				};

				tx.Put(Tables.WikiPage, WikiPageKey(key), Codec.Serialize(updated));
				tx.Put(Tables.WikiSlug, WikiSlugKey(updated.Namespace, updated.Slug), Keys.Dbref(key));
				WikiListIndexes(tx, key, updated, add: true);

				foreach (var (trKey, trBytes) in tx.Range(Tables.WikiTr, WikiPagePrefix(pageId)).ToList())
				{
					var translation = Codec.Deserialize<WikiTranslationRecord>(trBytes);
					var body = translation.MarkdownSource ?? string.Empty;
					tx.Put(Tables.WikiTr, trKey, Codec.Serialize(translation with
					{
						RenderedHtml = renderer.RenderToHtml(body),
						PlainText = renderer.ExtractPlainText(body),
					}));
				}
			}

			tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
			{
				Id = WikiCategoriesMigrationId, AppliedUnixMs = now.ToUnixTimeMilliseconds()
			}));
		}, cancellationToken);
	}
}
