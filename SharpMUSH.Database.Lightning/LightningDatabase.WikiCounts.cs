using System.Buffers.Binary;
using System.Text;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Page counts kept as the pages are written, so counting a category or a collection reads the pages a reader may not
/// see rather than every page the wiki holds.
/// </summary>
/// <remarks>
/// Three tables, each moved by <see cref="WikiListIndexes"/> in the same job as the page:
/// <see cref="Tables.WikiCount"/> holds how many published and how many draft pages there are in all and in each
/// category (keyed as <see cref="Tables.WikiByCategory"/> keys its labels); <see cref="Tables.WikiSummary"/> holds each
/// page's visibility, namespace and categories, the fields a count needs, without its body; <see cref="Tables.WikiDraft"/>
/// lists each author's drafts. A count for a reader starts from the stored totals, adds the drafts that reader may see,
/// then takes away each page the reader may not see that the totals included: those of another collection, and those
/// in a namespace, category or page whose read requirements the reader does not meet. A count over one collection
/// reads that collection's pages alone. A search, which needs each page's title, counts by scanning as before.
/// <para>
/// The totals hold from <see cref="WikiCountMigrationId"/> on, which builds them from the page rows once; until then a
/// count scans.
/// </para>
/// </remarks>
public partial class LightningDatabase
{
	internal const string WikiCountMigrationId = "0005_wiki_counts";

	private static readonly byte[] WikiTotalCountKey = [1];

	private static byte[] WikiCategoryCountKey(byte[] labelPrefix) => Keys.Concat([2], labelPrefix);

	private static byte[] WikiDraftPrefix(string author) => Keys.Concat(Keys.Str(author), Keys.Sep);

	/// <summary>Writes (<paramref name="add"/>) or removes <paramref name="r"/>'s share of the counts, its summary and its draft entry.</summary>
	private static void WikiCountEntries(ITx tx, long pageKey, WikiPageRecord r, bool add)
	{
		var published = r.Published ?? true;
		var delta = add ? 1 : -1;
		Bump(WikiTotalCountKey);
		foreach (var category in WikiIndexCategories(r)) Bump(WikiCategoryCountKey(WikiLabelPrefix(category)));

		var key = WikiPageKey(pageKey);
		if (add)
			tx.Put(Tables.WikiSummary, key, new WikiPageSummary(published, r.AuthorDbref, r.Namespace, r.Categories ?? []).Encode());
		else tx.Delete(Tables.WikiSummary, key);

		if (published) return;
		var draft = Keys.Concat(WikiDraftPrefix(r.AuthorDbref), key);
		if (add) tx.Put(Tables.WikiDraft, draft, []);
		else tx.Delete(Tables.WikiDraft, draft);

		void Bump(byte[] countKey)
		{
			var (publishedCount, draftCount) = tx.TryGet(Tables.WikiCount, countKey, out var value) ? ReadWikiCount(value) : (0L, 0L);
			if (published) publishedCount += delta;
			else draftCount += delta;
			if (publishedCount <= 0 && draftCount <= 0) tx.Delete(Tables.WikiCount, countKey);
			else tx.Put(Tables.WikiCount, countKey, WikiCountValue(Math.Max(0, publishedCount), Math.Max(0, draftCount)));
		}
	}

	private static (long Published, long Drafts) ReadWikiCount(ReadOnlySpan<byte> value)
		=> (BinaryPrimitives.ReadInt64BigEndian(value), BinaryPrimitives.ReadInt64BigEndian(value[8..]));

	private static byte[] WikiCountValue(long published, long drafts)
	{
		var value = new byte[16];
		BinaryPrimitives.WriteInt64BigEndian(value, published);
		BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(8), drafts);
		return value;
	}

	/// <summary>Builds the counts from the page rows, replacing whatever was there.</summary>
	private static void MigrateWikiCounts(ITx tx)
	{
		var marker = Keys.Str("mig:" + WikiCountMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;
		tx.DeletePrefix(Tables.WikiCount, []);
		tx.DeletePrefix(Tables.WikiSummary, []);
		tx.DeletePrefix(Tables.WikiDraft, []);
		foreach (var (key, value) in tx.Range(Tables.WikiPage, []))
			WikiCountEntries(tx, Keys.ReadDbref(key), Codec.Deserialize<WikiPageRecord>(value), add: true);
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = WikiCountMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}

	/// <summary>How many pages a reader may see, in all and in each category (keyed by category key).</summary>
	private sealed record WikiTally(int Total, Dictionary<string, int> Categories);

	/// <summary>
	/// <paramref name="visibility"/>'s count from the stored totals, or false when it has to scan: a search, or a world the
	/// counts migration has not reached.
	/// </summary>
	private static bool TryTallyWikiPages(ITx tx, WikiVisibility visibility, out WikiTally tally)
	{
		tally = new WikiTally(0, new Dictionary<string, int>(StringComparer.Ordinal));
		if (!string.IsNullOrWhiteSpace(visibility.Search)
			|| !tx.TryGet(Tables.Meta, Keys.Str("mig:" + WikiCountMigrationId), out _))
			return false;
		if (visibility.Hidden?.Everything == true) return true;

		var total = 0L;
		var categories = new Dictionary<string, long>(StringComparer.Ordinal);
		if (visibility.IncludedPageIds is { } included)
		{
			var seen = new HashSet<long>();
			foreach (var id in included)
			{
				if (!TryParseWikiPageId(id, out var key) || !seen.Add(key)) continue;
				if (ReadWikiSummary(tx, key) is { } summary && WikiSummaryAdmitted(visibility, key, summary)) Add(summary, 1);
			}
		}
		else
		{
			if (tx.TryGet(Tables.WikiCount, WikiTotalCountKey, out var all)) total = Visible(ReadWikiCount(all));
			foreach (var (key, value) in tx.Range(Tables.WikiCount, [2]))
				if (Visible(ReadWikiCount(value)) is var count and > 0)
					categories[Keys.ReadStr(key.AsSpan(1, key.Length - 2)).ToLowerInvariant()] = count;

			// The totals count every draft only for a reader who sees every draft; anyone else sees their own.
			if (!visibility.IncludeDrafts && visibility.AuthorDbref is { Length: > 0 } author)
				foreach (var (key, _) in tx.Range(Tables.WikiDraft, WikiDraftPrefix(author)))
					if (ReadWikiSummary(tx, Keys.ReadDbref(key.AsSpan(key.Length - 8))) is { } summary) Add(summary, 1);

			// Every page the totals and drafts above counted that this reader may not see.
			foreach (var key in WikiPagesWithheld(tx, visibility))
				if (ReadWikiSummary(tx, key) is { } summary && visibility.Admits(summary.Published, summary.AuthorDbref)
					&& !WikiSummaryAdmitted(visibility, key, summary))
					Add(summary, -1);
		}

		tally = new WikiTally(checked((int)total), categories.Where(pair => pair.Value > 0)
			.ToDictionary(pair => pair.Key, pair => checked((int)pair.Value), StringComparer.Ordinal));
		return true;

		long Visible((long Published, long Drafts) count) => count.Published + (visibility.IncludeDrafts ? count.Drafts : 0);

		void Add(WikiPageSummary summary, int delta)
		{
			total += delta;
			foreach (var category in summary.Categories.Select(c => c.ToUpperInvariant()).Distinct(StringComparer.Ordinal))
			{
				var name = category.ToLowerInvariant();
				categories[name] = categories.GetValueOrDefault(name) + delta;
			}
		}
	}

	/// <summary>
	/// The keys of every page <paramref name="visibility"/> may keep out by collection or read requirement, each once:
	/// the pages of other collections, the pages of a hidden namespace or category, and each hidden page. Namespaces and
	/// labels match ignoring case, so this can hold pages the reader does see; the caller checks each one.
	/// </summary>
	private static HashSet<long> WikiPagesWithheld(ITx tx, WikiVisibility visibility)
	{
		var keys = new HashSet<long>();
		foreach (var id in (visibility.ExcludedPageIds ?? Enumerable.Empty<string>()).Concat(visibility.Hidden?.PageIds ?? Enumerable.Empty<string>()))
			if (TryParseWikiPageId(id, out var key) && WikiPageId(key) == id) keys.Add(key);
		if (visibility.Hidden is not { } hidden) return keys;
		foreach (var ns in hidden.Namespaces)
			foreach (var (key, _) in WikiNamespaceEntries(tx, ns))
				keys.Add(WikiIndexPageKey(key));
		foreach (var category in hidden.Categories)
		{
			foreach (var (key, _) in tx.Range(Tables.WikiByCategory, WikiLabelPrefix(category)))
				keys.Add(WikiIndexPageKey(key));
		}
		return keys;
	}

	/// <summary>Whether <paramref name="visibility"/> admits the page <paramref name="summary"/> describes, as <see cref="WikiVisibility.Admits(WikiPage)"/> would.</summary>
	private static bool WikiSummaryAdmitted(WikiVisibility visibility, long key, WikiPageSummary summary)
	{
		var id = WikiPageId(key);
		return visibility.Admits(summary.Published, summary.AuthorDbref) && visibility.AdmitsPageId(id)
			&& visibility.Hidden?.Hides(summary.Namespace, summary.Categories, id) != true;
	}

	private static WikiPageSummary? ReadWikiSummary(ITx tx, long key)
		=> tx.TryGet(Tables.WikiSummary, WikiPageKey(key), out var value) ? WikiPageSummary.Decode(value) : null;

	/// <summary>
	/// The fields of a page a count or a read requirement needs, kept apart from its body: a published byte, then the
	/// author, the namespace and each category, each as a big-endian length and its UTF-8 bytes.
	/// </summary>
	private sealed record WikiPageSummary(bool Published, string AuthorDbref, string Namespace, string[] Categories)
	{
		public byte[] Encode()
		{
			var parts = new[] { AuthorDbref, Namespace }.Concat(Categories).Select(Encoding.UTF8.GetBytes).ToArray();
			var value = new byte[1 + 4 + parts.Sum(part => 4 + part.Length)];
			value[0] = Published ? (byte)1 : (byte)0;
			BinaryPrimitives.WriteInt32BigEndian(value.AsSpan(1), Categories.Length);
			var at = 5;
			foreach (var part in parts)
			{
				BinaryPrimitives.WriteInt32BigEndian(value.AsSpan(at), part.Length);
				part.CopyTo(value, at + 4);
				at += 4 + part.Length;
			}
			return value;
		}

		public static WikiPageSummary Decode(ReadOnlySpan<byte> value)
		{
			if (value.Length < 13 || value[0] > 1) throw Invalid();
			var count = BinaryPrimitives.ReadInt32BigEndian(value[1..]);
			if (count < 0 || count > (value.Length - 13) / 4) throw Invalid();
			var rest = value[5..];
			var author = Next(ref rest);
			var ns = Next(ref rest);
			var categories = new string[count];
			for (var i = 0; i < count; i++) categories[i] = Next(ref rest);
			if (!rest.IsEmpty) throw Invalid();
			return new WikiPageSummary(value[0] == 1, author, ns, categories);

			static string Next(ref ReadOnlySpan<byte> rest)
			{
				if (rest.Length < 4) throw Invalid();
				var length = BinaryPrimitives.ReadInt32BigEndian(rest);
				if (length < 0 || length > rest.Length - 4) throw Invalid();
				var text = Encoding.UTF8.GetString(rest.Slice(4, length));
				rest = rest[(4 + length)..];
				return text;
			}
		}

		private static InvalidDataException Invalid() => new("A wiki page summary is not in the stored format.");
	}
}
