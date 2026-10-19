using System.Collections.Immutable;

namespace SharpMUSH.Library.Models.Wiki.Semantics;

public readonly record struct PortableKey(string Package, string Namespace, string SourceKey);
/// <summary>Opaque incarnation ID. A recreated page receives a new ID even if its title or source key matches.</summary>
public readonly record struct PageRef(string Id);
public readonly record struct Keyword(string Value);
public union SemanticValue(PageRef, long, bool, Keyword);
public enum SemanticType { PageRef, Integer, Boolean, Keyword }
public enum Cardinality { Single, Multiple }
/// <summary>
/// Source package owns a qualified schema; changing it requires a new version and explicit migration.
/// Id grammar is ASCII [A-Za-z0-9][A-Za-z0-9._-]*:[A-Za-z0-9][A-Za-z0-9._-]*,
/// case-sensitive, with exactly one colon. Validation checks syntax, not external package ownership.
/// </summary>
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

/// <summary>Local identity is the world-scoped immutable PageRef, with no portable fields.</summary>
public readonly record struct LocalWikiSource;
public readonly record struct PortableWikiSource(PortableKey Key);
public union WikiSemanticSource(LocalWikiSource, PortableWikiSource);
/// <summary>Projection metadata only. Live content and revision bodies remain owned by the wiki.</summary>
public sealed record WikiPageProjection(PageRef Reference, WikiSemanticSource Source, string Title,
	string Collection, VisibilityDescriptor Visibility);

/// <summary>Shared admission policy used by both the validator and native preparation.</summary>
public static class WikiSemanticPolicy
{
	public const int MaxFacts = 128;
	public const int MaxValuesPerProperty = 32;
	public const int MaxFactBytes = 64 * 1024;
	public const int MaxKeywordBytes = 256;
	public const int MaxSummaryBytes = 16 * 1024;
	public const int MaxDescriptorBytes = 8 * 1024;
}
