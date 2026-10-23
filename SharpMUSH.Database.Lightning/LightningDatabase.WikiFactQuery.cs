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
/// each page they name is read and checked against the rest of the query. The driver is the condition with the
/// fewest entries, counted up to <see cref="WikiFactQuery.MaxCandidates"/>, and a continuation keeps it.
/// </summary>
public partial class LightningDatabase
{
	private const string FactQueryToken = "facts:v1:";

	/// <summary>The entries of one condition: those from <paramref name="Start"/> while <paramref name="Inside"/> holds.</summary>
	private sealed record FactConditionSpan(byte[] Start, Func<byte[], bool> Inside);

	public Task<WikiFactQueryResult> QueryFactsAsync(WikiFactQuery query, WikiVisibility visibility)
		=> Task.FromResult(WikiFactQueries.Check(WikiFactSchema.Schema, query) switch
		{
			IReadOnlyList<PropertyDefinition> definitions => Store.Read(tx => QueryFacts(tx, Canonical(query, definitions), definitions, visibility)),
			Error<string> error => (WikiFactQueryResult)error
		});

	/// <summary>The query with each page reference it names as its canonical id, the form the index holds.</summary>
	private static WikiFactQuery Canonical(WikiFactQuery query, IReadOnlyList<PropertyDefinition> definitions)
		=> query with
		{
			Conditions = [.. query.Conditions.Select((condition, i) => condition is WikiFactEquals equals && definitions[i].Type == SemanticType.PageRef
				? (WikiFactCondition)(equals with { Value = CanonicalWikiPageId(equals.Value) })
				: condition)]
		};

	private static WikiFactQueryResult QueryFacts(ITx tx, WikiFactQuery query, IReadOnlyList<PropertyDefinition> definitions,
		WikiVisibility visibility)
	{
		if (FactIndexStatus(tx, ReadFactIndexState(tx)) is { Ready: false } status) return new WikiFactIndexBuilding(status);
		var spans = query.Conditions.Select(FactSpan).ToList();
		if (FactQueryResume(query.After, spans) is not { } resume) return new Error<string>("That query's continuation is not valid here.");
		var (driver, start) = resume.Driver < 0 ? (FactDriver(tx, spans), (byte[]?)null) : resume;
		var conditions = query.Conditions;

		var pages = new List<WikiPage>();
		var read = 0;
		foreach (var (key, _) in tx.RangeFromKey(Tables.WikiFact, start ?? spans[driver].Start))
		{
			if (!spans[driver].Inside(key)) break;
			if (read++ == WikiFactQuery.MaxCandidates || pages.Count == query.Take)
				return new WikiFactQueryPage(pages, FactQueryNext(driver, key));
			var pageKey = Keys.ReadDbref(key.AsSpan(key.Length - 8));
			if (ReadWikiPage(tx, pageKey) is not { } page
				|| !WikiFactQueries.InScope(query, page) || !visibility.Admits(page)
				|| !conditions.Select((condition, i) => WikiFactQueries.Meets(condition, definitions[i], page.Facts)).All(met => met)
				|| !FirstEntryOf(page, conditions[driver], definitions[driver], key))
				continue;
			pages.Add(page);
		}
		return new WikiFactQueryPage(pages, null);
	}

	/// <summary>
	/// Whether <paramref name="key"/> is the first entry of the driving condition that names <paramref name="page"/>,
	/// so a page stating several values in a range is returned once, at its lowest.
	/// </summary>
	private static bool FirstEntryOf(WikiPage page, WikiFactCondition condition, PropertyDefinition definition, byte[] key)
		=> condition is not WikiFactRange
			|| page.Facts.Where(f => f.Property == definition.Id && WikiFactQueries.Meets(condition, definition, f.Value))
				.Min(f => WikiFactQueries.Integer(f.Value)) == FactKeyInteger(definition.Id, key);

	private static long FactKeyInteger(string property, byte[] key)
		=> unchecked((long)(BinaryPrimitives.ReadUInt64BigEndian(key.AsSpan(FactPropertyPrefix(property).Length + 1)) ^ 0x8000_0000_0000_0000UL));

	private static FactConditionSpan FactSpan(WikiFactCondition condition) => condition switch
	{
		WikiFactEquals equals => FactPrefixSpan(FactValuePrefix(equals.Property, equals.Value)!),
		WikiFactRange range => FactRangeSpan(Keys.Concat(FactPropertyPrefix(range.Property), [FactIntegerTag]), range)
	};

	// Every entry is its value's prefix and an eight-byte page key; a continuation's key is held to that shape too.
	private static FactConditionSpan FactPrefixSpan(byte[] prefix)
		=> new(prefix, key => key.Length == prefix.Length + 8 && key.AsSpan().StartsWith(prefix));

	/// <summary>Every integer entry of the property from the range's lowest value through its highest.</summary>
	private static FactConditionSpan FactRangeSpan(byte[] integers, WikiFactRange range)
	{
		var last = Keys.Concat(integers, FactOrderedInteger(range.Max ?? long.MaxValue));
		return new(Keys.Concat(integers, FactOrderedInteger(range.Min ?? long.MinValue)),
			key => key.Length == last.Length + 8 && key.AsSpan().StartsWith(integers) && key.AsSpan(0, last.Length).SequenceCompareTo(last) <= 0);
	}

	/// <summary>The condition with the fewest entries, counting each up to one past a call's budget; the first on a tie.</summary>
	private static int FactDriver(ITx tx, IReadOnlyList<FactConditionSpan> spans)
		=> spans.Select((span, i) => (Count: tx.RangeFromKey(Tables.WikiFact, span.Start).TakeWhile(e => span.Inside(e.Key))
				.Take(WikiFactQuery.MaxCandidates + 1).Count(), Index: i))
			.MinBy(c => c.Count).Index;

	/// <summary>The driver and first key a continuation names; driver -1 for a fresh query, null for a token that is not valid.</summary>
	private static (int Driver, byte[]? Start)? FactQueryResume(string? after, IReadOnlyList<FactConditionSpan> spans)
	{
		if (after is null) return (-1, null);
		byte[] bytes;
		try { bytes = WikiPageScan.Decode(after, FactQueryToken); }
		catch (Exception e) when (e is ArgumentException or FormatException) { return null; }
		if (bytes.Length < 2 || bytes[0] >= spans.Count) return null;
		var start = bytes[1..];
		return spans[bytes[0]].Inside(start) ? (bytes[0], start) : null;
	}

	private static string FactQueryNext(int driver, byte[] key) => WikiPageScan.Encode([(byte)driver, .. key], FactQueryToken);
}
