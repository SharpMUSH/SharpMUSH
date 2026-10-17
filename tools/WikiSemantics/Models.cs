using System.Collections.Immutable;

namespace SharpMUSH.Tools.WikiSemantics;

public readonly record struct PortableKey(string Package, string Namespace, string SourceKey);
/// <summary>Opaque incarnation ID. A recreated page receives a new ID even if its title or source key matches.</summary>
public readonly record struct PageRef(string Id);
public readonly record struct Keyword(string Value);
public union SemanticValue(PageRef, long, bool, Keyword);
public enum SemanticType { PageRef, Integer, Boolean, Keyword }
public enum Cardinality { Single, Multiple }
/// <summary>Source package owns a qualified schema; changing it requires a new version and explicit migration.</summary>
public sealed record PropertyDefinition(string Id, long SchemaVersion, SemanticType Type, Cardinality Cardinality,
	ImmutableArray<Keyword> AllowedKeywords);
public sealed record FactProvenance(string Package, string SourceCommit, string SourcePath, string AuthoredField,
	bool LocalOverride, string PackageVersion = "", string SourceBlobHash = "",
	string ExtractionRuleVersion = "", string EvidenceLocation = "");
public sealed record SemanticFact(PageRef Subject, string Property, SemanticValue Value, FactProvenance Provenance);
public sealed record SemanticSchema(long Version, ImmutableArray<PropertyDefinition> Properties);
public sealed record CanonicalFactSet(long SchemaVersion, ImmutableArray<SemanticFact> Facts);
public enum ValidationCode { InvalidSchema, InvalidKeyword, UnknownProperty, WrongType, InvalidEnum, Cardinality, Quota, AmbiguousAlias, MissingTarget }
public sealed record ValidationFailure(ValidationCode Code, string Detail);
public union ValidationResult(CanonicalFactSet, ValidationFailure);
public union IdentityResult(PageRef, ValidationFailure);
public sealed record PortableAlias(PortableKey Key, PageRef Target);
/// <summary>Snapshot descriptors contain no scope grants. Resolve WikiVisibility externally.</summary>
public sealed record VisibilityDescriptor(string Namespace, ImmutableArray<string> Categories,
	ImmutableArray<string> RuleIds, bool Published, string? DraftAuthor);
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
