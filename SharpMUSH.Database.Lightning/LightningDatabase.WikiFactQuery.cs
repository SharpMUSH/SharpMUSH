using System.Buffers.Binary;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Fact queries over <see cref="Tables.WikiFact"/>. One condition drives: its entries are read in key order, and
/// each page they name is read and checked against the rest of the query. The driver is the sort's condition when
/// the query has a sort, else the condition with the fewest entries, counted up to
/// <see cref="WikiFactQuery.MaxCandidates"/>; a negated condition never drives. A continuation keeps the driver.
/// </summary>
public partial class LightningDatabase
{
	private const string FactQueryToken = "facts:v1:";

	/// <summary>
	/// A run of entries: those with <paramref name="Prefix"/> from <paramref name="Start"/> while
	/// <paramref name="Inside"/> holds. Read backwards, it starts below <paramref name="Below"/>, or at the prefix's
	/// last entry when that is null.
	/// </summary>
	private sealed record FactSegment(byte[] Prefix, byte[] Start, byte[]? Below, Func<byte[], bool> Inside)
	{
		internal bool Holds(byte[] key) => key.AsSpan().SequenceCompareTo(Start) >= 0 && Inside(key);
	}

	/// <summary>A condition as runs of entries in key order; a value list holds one run per distinct value.</summary>
	private sealed record FactConditionSpan(IReadOnlyList<FactSegment> Segments);

	public Task<WikiFactQueryResult> QueryFactsAsync(WikiFactQuery query, WikiVisibility visibility)
		=> Task.FromResult(Store.Read(tx => QueryFacts(tx, ReadFactSchema(tx), query, visibility)));

	private static WikiFactQueryResult QueryFacts(ITx tx, SemanticSchema schema, WikiFactQuery query, WikiVisibility visibility)
		=> WikiFactQueries.Check(schema, query) switch
		{
			IReadOnlyList<PropertyDefinition> definitions => QueryFacts(tx, Driven(Canonical(query, definitions), definitions, schema), visibility),
			Error<string> error => error
		};

	/// <summary>The query with each page reference it names as its canonical id, the form the index holds.</summary>
	private static WikiFactQuery Canonical(WikiFactQuery query, IReadOnlyList<PropertyDefinition> definitions)
		=> query with { Conditions = [.. query.Conditions.Select((condition, i) => CanonicalCondition(condition, definitions[i]))] };

	private static WikiFactCondition CanonicalCondition(WikiFactCondition condition, PropertyDefinition definition) => condition switch
	{
		WikiFactEquals equals when definition.Type == SemanticType.PageRef => equals with { Value = CanonicalWikiPageId(equals.Value) },
		WikiFactOneOf oneOf when definition.Type == SemanticType.PageRef => oneOf with { Values = [.. oneOf.Values.Select(CanonicalWikiPageId)] },
		WikiFactNot not => not with { Condition = CanonicalCondition(not.Condition, definition) },
		_ => condition
	};

	/// <summary>
	/// The conditions a page must meet with their properties' definitions, and the one the sort drives: a condition
	/// over the sort's property that a page meets by stating a value, else a condition that it states any, added.
	/// Null when the query has no sort.
	/// </summary>
	private sealed record DrivenQuery(WikiFactQuery Query, IReadOnlyList<WikiFactCondition> Conditions,
		IReadOnlyList<PropertyDefinition> Definitions, int? SortDriver);

	private static DrivenQuery Driven(WikiFactQuery query, IReadOnlyList<PropertyDefinition> definitions, SemanticSchema schema)
	{
		if (query.Sort is not { } sort) return new(query, query.Conditions, definitions, null);
		var onSort = query.Conditions.Select((condition, i) => (condition, i))
			.FirstOrDefault(c => WikiFactQueries.Drives(c.condition) && definitions[c.i].Id == sort);
		if (onSort.condition is not null) return new(query, query.Conditions, definitions, onSort.i);
		return new(query, [.. query.Conditions, new WikiFactStated(sort)],
			[.. definitions, schema.Properties.First(p => p.Id == sort)], query.Conditions.Count);
	}

	private static WikiFactQueryResult QueryFacts(ITx tx, DrivenQuery driven, WikiVisibility visibility)
	{
		if (FactIndexStatus(tx, ReadFactIndexState(tx)) is { Ready: false } status) return new WikiFactIndexBuilding(status);
		var (query, conditions, definitions, sortDriver) = driven;
		var spans = conditions.Select((condition, i) => WikiFactQueries.Drives(condition) ? FactSpan(condition, definitions[i]) : null).ToList();
		var descending = query.Descending;
		if (FactQueryResume(query.After, spans) is not { } resume) return new Error<string>("That query's continuation is not valid here.");
		var (driver, start) = resume.Driver >= 0 ? resume : (sortDriver ?? FactDriver(tx, spans), (byte[]?)null);

		var pages = new List<WikiPage>();
		var read = 0;
		foreach (var key in FactEntries(tx, spans[driver]!, start, descending))
		{
			if (read++ == WikiFactQuery.MaxCandidates || pages.Count == query.Take)
				return new WikiFactQueryPage(pages, FactQueryNext(driver, key));
			var pageKey = Keys.ReadDbref(key.AsSpan(key.Length - 8));
			if (ReadWikiPage(tx, pageKey) is not { } page
				|| !WikiFactQueries.InScope(query, page) || !visibility.Admits(page)
				|| !conditions.Select((condition, i) => WikiFactQueries.Meets(condition, definitions[i], page.Facts)).All(met => met)
				|| !FirstEntryOf(page, pageKey, conditions[driver], definitions[driver], key, descending))
				continue;
			pages.Add(page);
		}
		return new WikiFactQueryPage(pages, null);
	}

	/// <summary>
	/// The driving span's entries in key order, or the reverse, from <paramref name="start"/> (a continuation's key,
	/// read again) or from the first.
	/// </summary>
	private static IEnumerable<byte[]> FactEntries(ITx tx, FactConditionSpan span, byte[]? start, bool descending)
	{
		var segments = descending ? span.Segments.Reverse().ToList() : [.. span.Segments];
		var first = start is null ? 0 : segments.FindIndex(s => s.Holds(start));
		for (var i = first; i < segments.Count; i++)
		{
			var segment = segments[i];
			var from = i == first ? start : null;
			var entries = descending
				? (from is not null ? tx.RangeReverseBefore(Tables.WikiFact, segment.Prefix, [.. from, 0])
					: segment.Below is { } below ? tx.RangeReverseBefore(Tables.WikiFact, segment.Prefix, below)
					: tx.RangeReverse(Tables.WikiFact, segment.Prefix))
				: tx.RangeFromKey(Tables.WikiFact, from ?? segment.Start);
			foreach (var (key, _) in entries)
			{
				if (!segment.Holds(key)) break;
				yield return key;
			}
		}
	}

	/// <summary>
	/// Whether <paramref name="key"/> is the first entry of the driving condition that names <paramref name="page"/>
	/// in the order read, so a page stating several of the values it reads is returned once.
	/// </summary>
	private static bool FirstEntryOf(WikiPage page, long pageKey, WikiFactCondition condition, PropertyDefinition definition,
		byte[] key, bool descending)
	{
		var keys = page.Facts.Where(f => f.Property == definition.Id && WikiFactQueries.Meets(condition, definition, f.Value))
			.Select(f => Keys.Concat(FactValuePrefix(f.Property, f.Value, definition.Type), Keys.Dbref(pageKey)));
		var first = descending ? keys.Max(ByteOrder) : keys.Min(ByteOrder);
		return first is not null && first.AsSpan().SequenceEqual(key);
	}

	private static readonly Comparer<byte[]> ByteOrder = Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b));

	private static FactConditionSpan FactSpan(WikiFactCondition condition, PropertyDefinition definition) => condition switch
	{
		WikiFactEquals equals => new([FactValueSegment(definition, equals.Value)]),
		WikiFactOneOf oneOf => new([.. oneOf.Values.Select(v => FactValueSegment(definition, v))
			.DistinctBy(s => Convert.ToHexString(s.Start)).OrderBy(s => s.Start, ByteOrder)]),
		WikiFactRange range => new([FactRangeSegment(Keys.Concat(FactPropertyPrefix(range.Property), [FactOrderedTag]), range)]),
		WikiFactStated stated => new([FactPrefixSegment(FactPropertyPrefix(stated.Property), exactLength: false)]),
		// Never asked for: a negation does not drive.
		WikiFactNot => new([]),
	};

	private static FactSegment FactValueSegment(PropertyDefinition definition, string value)
		=> FactPrefixSegment(FactValuePrefix(definition.Id, value, definition.Type), exactLength: true);

	// Every entry is its value's prefix and an eight-byte page key; a continuation's key is held to that shape too.
	private static FactSegment FactPrefixSegment(byte[] prefix, bool exactLength)
		=> new(prefix, prefix, null, key => key.AsSpan().StartsWith(prefix)
			&& (exactLength ? key.Length == prefix.Length + 8 : key.Length > prefix.Length + 8));

	/// <summary>Every ordered entry of the property from the range's lowest key through its highest.</summary>
	private static FactSegment FactRangeSegment(byte[] ordered, WikiFactRange range)
	{
		var last = Keys.Concat(ordered, FactOrderedKey(range.Max ?? long.MaxValue));
		return new(ordered, Keys.Concat(ordered, FactOrderedKey(range.Min ?? long.MinValue)), [.. last, .. Enumerable.Repeat((byte)0xFF, 9)],
			key => key.Length == last.Length + 8 && key.AsSpan().StartsWith(ordered) && key.AsSpan(0, last.Length).SequenceCompareTo(last) <= 0);
	}

	/// <summary>The driving condition with the fewest entries, counting each up to one past a call's budget; the first on a tie.</summary>
	private static int FactDriver(ITx tx, IReadOnlyList<FactConditionSpan?> spans)
		=> spans.Select((span, i) => (Count: span is null ? int.MaxValue : FactEntries(tx, span, null, descending: false)
				.Take(WikiFactQuery.MaxCandidates + 1).Count(), Index: i))
			.MinBy(c => c.Count).Index;

	/// <summary>The driver and first key a continuation names; driver -1 for a fresh query, null for a token that is not valid.</summary>
	private static (int Driver, byte[]? Start)? FactQueryResume(string? after, IReadOnlyList<FactConditionSpan?> spans)
	{
		if (after is null) return (-1, null);
		byte[] bytes;
		try { bytes = WikiPageScan.Decode(after, FactQueryToken); }
		catch (Exception e) when (e is ArgumentException or FormatException) { return null; }
		if (bytes.Length < 2 || bytes[0] >= spans.Count || spans[bytes[0]] is not { } span) return null;
		var start = bytes[1..];
		return span.Segments.Any(s => s.Holds(start)) ? (bytes[0], start) : null;
	}

	private static string FactQueryNext(int driver, byte[] key) => WikiPageScan.Encode([(byte)driver, .. key], FactQueryToken);
}
