using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// A read-requirement change on an active projection. The pages its target applies to are found through the
/// legacy namespace and category indexes and counted before any write; more than <see cref="ReadRewritePageCap"/>
/// is refused as a work limit. Otherwise the requirement row and every one of those pages' projections move in
/// one writer job: each page whose descriptor changed is planned again under its new visibility class.
/// </summary>
public partial class LightningDatabase
{
	/// <summary>The most pages one read-requirement change may move between visibility classes.</summary>
	internal const int ReadRewritePageCap = 256;

	/// <summary>Tests only: a lower cap than <see cref="ReadRewritePageCap"/>.</summary>
	internal int ReadRewritePageLimit { get; set; } = ReadRewritePageCap;

	/// <summary>Tests only: runs inside a rewrite's job, after its first page was planned again and before it commits.</summary>
	internal Action? ReadRewriteJobHook { get; set; }

	/// <summary>The keys of the pages a read rule on (<paramref name="scope"/>, <paramref name="key"/>) applies to.</summary>
	private IReadOnlyList<long> ReadRewriteTargets(ITx tx, WikiRuleScope scope, string key)
	{
		var limit = ReadRewritePageLimit;
		if (scope == WikiRuleScope.Page) return TryParseWikiPageId(key, out var page) ? [page] : [];
		var (table, prefix) = scope switch
		{
			WikiRuleScope.Namespace => (Tables.WikiByNamespace, Keys.Concat(OrdinalBytes(key), OrdinalSep)),
			WikiRuleScope.Category => (Tables.WikiByCategory, WikiLabelPrefix(key)),
			_ => throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation)
		};
		var budget = new NativeReadBudget(4096, 4L << 20, 16L << 20, CancellationToken.None, maxRecords: NativeReadBudget.MaxBatchRecords);
		var pages = new HashSet<long>();
		byte[] after = [];
		while (true)
		{
			using var rows = ((IBoundedReadTx)tx).ReadRange(table, prefix, [], [], after, budget);
			foreach (var row in rows.Entries)
			{
				pages.Add(WikiIndexPageKey(row.Key));
				if (pages.Count > limit) throw new WikiFixtureMutationException(WikiFixtureMutationFailure.WorkLimit);
			}
			if (rows.Exhausted || rows.Entries.Length == 0) break;
			after = rows.Entries[^1].Key;
		}
		return [.. pages.Order()];
	}

	private void RewriteReadVisibility(ITx tx, SemanticFixtureMutationLease? admission, IReadOnlyList<long> pages)
	{
		if (pages.Count == 0 || admission is null) return;
		var moved = 0;
		foreach (var page in pages)
		{
			if (!ReprojectPage(tx, admission, WikiPageId(page), null, null, CancellationToken.None)) continue;
			if (moved++ == 0) ReadRewriteJobHook?.Invoke();
		}
	}
}
