using System.Collections.Immutable;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Tools.WikiSemantics;

/// <summary>Planned invalidation dimensions, NOT live transaction/revocation guarantees.</summary>
public sealed record ExperimentalStamp(string SemanticCommit, long IndexGeneration, long SchemaVersion,
	long RequirementsVersion, long CollectionVersion, long PartitionVersion, long PageVersion, long TranslationVersion);
public sealed record PageSnapshot(PageRef Reference, PortableKey Key, string Title, string Body,
	ImmutableArray<string> Collections, VisibilityDescriptor Visibility, ExperimentalStamp Stamp);
public sealed record SemanticSnapshot(SemanticSchema Schema, ImmutableArray<PageSnapshot> Pages,
	CanonicalFactSet Facts, ExperimentalStamp Stamp);
public sealed record EqualPredicate(string Property, SemanticValue Value);
public sealed record IntegerRangePredicate(string Property, long Minimum, long Maximum);
public union QueryPredicate(EqualPredicate, IntegerRangePredicate);
public sealed record IdSort;
public sealed record IntegerSort(string Property, bool Descending = false);
public union QuerySort(IdSort, IntegerSort);
/// <summary>Offset is an offline oracle convenience, never a production cursor contract.</summary>
public sealed record SemanticQuery(string Collection, ImmutableArray<QueryPredicate> Predicates,
	ImmutableArray<string> Columns, QuerySort Sort, int PageSize = 25, int Offset = 0,
	bool IncludeCount = false, ImmutableArray<string> Facets = default);
public sealed record QueryCell(string Property, ImmutableArray<SemanticValue> Values);
public sealed record QueryRow(PageRef Subject, ImmutableArray<QueryCell> Cells);
public sealed record FacetBucket(SemanticValue Value, int Count);
public sealed record QueryFacet(string Property, ImmutableArray<FacetBucket> Buckets);
public sealed record QuerySuccess(ImmutableArray<QueryRow> Rows, bool HasMore, long? Count,
	ImmutableArray<QueryFacet> Facets);
public enum QueryFailureCode { InvalidQuery, UnavailableReference, InvalidSnapshot, UnsupportedVisibility }
public sealed record QueryFailure(QueryFailureCode Code, string Detail);
public union QueryResult(QuerySuccess, QueryFailure);
