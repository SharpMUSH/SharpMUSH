using System.Collections.Immutable;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Tools.WikiSemantics;

/// <summary>
/// Independent full-scan offline oracle. Visibility is a server-resolved test snapshot, not a
/// revocation-safe authorization stamp. Offsets and complete aggregates provide no runtime guarantees.
/// </summary>
public static class ReferenceEvaluator
{
	public static QueryResult Evaluate(SemanticSnapshot snapshot, SemanticQuery query, WikiVisibility visibility,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!string.IsNullOrWhiteSpace(visibility.Search))
			return Failure(QueryFailureCode.UnsupportedVisibility);
		if (snapshot.Schema is null || snapshot.Facts is null || snapshot.Pages.IsDefault || snapshot.Facts.Facts.IsDefault || snapshot.Facts.SchemaVersion != snapshot.Schema.Version
			|| CanonicalFacts.Validate(snapshot.Schema, snapshot.Facts.Facts, cancellationToken) is not CanonicalFactSet canonical)
			return Failure(QueryFailureCode.InvalidSnapshot);
		var definitions = snapshot.Schema.Properties.ToDictionary(p => p.Id, StringComparer.Ordinal);
		var facets = query.Facets.IsDefault ? ImmutableArray<string>.Empty : query.Facets;
		if (string.IsNullOrWhiteSpace(query.Collection) || query.Predicates.IsDefault || query.Columns.IsDefault
			|| query.Predicates.Length > 4 || query.Columns.Length > 6 || query.PageSize is < 1 or > 100 || query.Offset < 0
			|| query.Columns.Any(p => string.IsNullOrWhiteSpace(p) || !definitions.ContainsKey(p)) || facets.Any(p => string.IsNullOrWhiteSpace(p) || !definitions.ContainsKey(p))
			|| query.Columns.Distinct(StringComparer.Ordinal).Count() != query.Columns.Length
			|| facets.Distinct(StringComparer.Ordinal).Count() != facets.Length)
			return Failure(QueryFailureCode.InvalidQuery);
		if (facets.Any(property => definitions[property].Type != SemanticType.Keyword
			|| definitions[property].AllowedKeywords.Length is < 1 or > 16))
			return Failure(QueryFailureCode.InvalidQuery);
		if (query.Sort is not IdSort && query.Sort is not IntegerSort)
			return Failure(QueryFailureCode.InvalidQuery);
		if (query.Sort is IntegerSort sort && (string.IsNullOrWhiteSpace(sort.Property) || !definitions.TryGetValue(sort.Property, out var order)
			|| order.Type != SemanticType.Integer || order.Cardinality != Cardinality.Single))
			return Failure(QueryFailureCode.InvalidQuery);

		var pages = new Dictionary<PageRef, PageSnapshot>();
		var admitted = new HashSet<PageRef>();
		foreach (var page in snapshot.Pages)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (page is null || string.IsNullOrWhiteSpace(page.Reference.Id) || page.Collections.IsDefault
				|| page.Visibility is null || page.Visibility.Categories.IsDefault || !pages.TryAdd(page.Reference, page))
				return Failure(QueryFailureCode.InvalidSnapshot);
			if (page.Collections.Contains(query.Collection, StringComparer.Ordinal) && visibility.Admits(AsWikiPage(page)))
				admitted.Add(page.Reference);
		}
		var predicates = ImmutableArray.CreateBuilder<QueryPredicate>();
		foreach (var predicate in query.Predicates)
		{
			cancellationToken.ThrowIfCancellationRequested();
			switch (predicate)
			{
				case EqualPredicate equal:
					var fact = new SemanticFact(new("query"), equal.Property, equal.Value, new("", "", "", "", false));
					if (CanonicalFacts.Validate(snapshot.Schema, [fact], cancellationToken) is not CanonicalFactSet valid)
						return Failure(QueryFailureCode.InvalidQuery);
					var value = valid.Facts.Single().Value;
					if (value is PageRef target && !admitted.Contains(target))
						return Failure(QueryFailureCode.UnavailableReference);
					predicates.Add(equal with { Value = value });
					break;
				case IntegerRangePredicate range when !string.IsNullOrWhiteSpace(range.Property) && range.Minimum <= range.Maximum
					&& definitions.TryGetValue(range.Property, out var definition) && definition.Type == SemanticType.Integer:
					predicates.Add(range);
					break;
				default:
					return Failure(QueryFailureCode.InvalidQuery);
			}
		}
		var visibleFacts = new Dictionary<PageRef, List<SemanticFact>>();
		foreach (var fact in canonical.Facts)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!admitted.Contains(fact.Subject) || fact.Value is PageRef target && !admitted.Contains(target))
				continue;
			if (!visibleFacts.TryGetValue(fact.Subject, out var subjectFacts))
				visibleFacts[fact.Subject] = subjectFacts = [];
			subjectFacts.Add(fact);
		}
		IReadOnlyList<SemanticFact> Facts(PageRef subject) => visibleFacts.TryGetValue(subject, out var facts) ? facts : [];
		var matches = new List<PageRef>();
		foreach (var subject in admitted)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (predicates.All(predicate => Facts(subject).Any(fact => Matches(fact, predicate))))
				matches.Add(subject);
		}
		long? Integer(PageRef subject, string property) => Facts(subject).Where(f => f.Property == property).Select(f => f.Value is long n ? (long?)n : null).SingleOrDefault();
		matches.Sort((left, right) =>
		{
			if (query.Sort is IntegerSort integerSort)
			{
				var a = Integer(left, integerSort.Property);
				var b = Integer(right, integerSort.Property);
				var comparison = a.HasValue.CompareTo(b.HasValue);
				if (comparison != 0) return -comparison;
				if (a.HasValue && b.HasValue)
				{
					comparison = integerSort.Descending ? b.Value.CompareTo(a.Value) : a.Value.CompareTo(b.Value);
					if (comparison != 0) return comparison;
				}
			}
			return string.CompareOrdinal(left.Id, right.Id);
		});
		cancellationToken.ThrowIfCancellationRequested();
		ImmutableArray<SemanticValue> Values(PageRef subject, string property) => [.. Facts(subject)
			.Where(f => f.Property == property).Select(f => f.Value).Distinct().OrderBy(CanonicalFacts.ValueKey, StringComparer.Ordinal)];
		var resultFacets = ImmutableArray.CreateBuilder<QueryFacet>();
		foreach (var property in facets)
		{
			var buckets = new Dictionary<string, (SemanticValue Value, int Count)>(StringComparer.Ordinal);
			foreach (var subject in matches)
			{
				cancellationToken.ThrowIfCancellationRequested();
				foreach (var value in Values(subject, property))
				{
					var key = CanonicalFacts.ValueKey(value);
					buckets[key] = (value, buckets.TryGetValue(key, out var bucket) ? bucket.Count + 1 : 1);
				}
			}
			resultFacets.Add(new(property, [.. buckets.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new FacetBucket(p.Value.Value, p.Value.Count))]));
		}
		var rows = ImmutableArray.CreateBuilder<QueryRow>();
		foreach (var subject in matches.Skip(query.Offset).Take(query.PageSize))
		{
			cancellationToken.ThrowIfCancellationRequested();
			rows.Add(new(subject, [.. query.Columns.Select(property => new QueryCell(property, Values(subject, property)))]));
		}
		return new QuerySuccess(rows.ToImmutable(), (long)query.Offset + rows.Count < matches.Count,
			query.IncludeCount ? matches.Count : null, resultFacets.ToImmutable());
	}

	private static bool Matches(SemanticFact fact, QueryPredicate predicate) => predicate switch
	{
		EqualPredicate equal => fact.Property == equal.Property && CanonicalFacts.ValueKey(fact.Value) == CanonicalFacts.ValueKey(equal.Value),
		IntegerRangePredicate range => fact.Property == range.Property && fact.Value is long n && n >= range.Minimum && n <= range.Maximum,
		_ => false
	};

	private static QueryFailure Failure(QueryFailureCode code) => new(code, "Semantic request unavailable.");

	private static WikiPage AsWikiPage(PageSnapshot page) => new(page.Reference.Id, "", page.Title,
		page.Visibility.Namespace, "", "", "", page.Visibility.DraftAuthor ?? "", "", default, default, 1)
	{
		Published = page.Visibility.Published,
		Categories = page.Visibility.Categories
	};
}
