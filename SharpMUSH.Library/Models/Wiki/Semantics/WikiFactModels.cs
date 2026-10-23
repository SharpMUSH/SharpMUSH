using System.Collections.Immutable;

namespace SharpMUSH.Library.Models.Wiki.Semantics;

/// <summary>Opaque incarnation ID. A recreated page receives a new ID even if its title or source key matches.</summary>
public readonly record struct PageRef(string Id);
public readonly record struct Keyword(string Value);
public union SemanticValue(PageRef, long, bool, Keyword);
public enum SemanticType { PageRef, Integer, Boolean, Keyword }
public enum Cardinality { Single, Multiple }
/// <summary>
/// A property a page's facts may state, as the world defines it. Id grammar is ASCII
/// [A-Za-z0-9][A-Za-z0-9._-]*:[A-Za-z0-9][A-Za-z0-9._-]*, case-sensitive, with exactly one colon.
/// <paramref name="Label"/> and <paramref name="Description"/> are what a reader is shown; empty when unset.
/// </summary>
public sealed record PropertyDefinition(string Id, long SchemaVersion, SemanticType Type, Cardinality Cardinality,
	ImmutableArray<Keyword> AllowedKeywords, string Label = "", string Description = "");
public sealed record FactProvenance(string Package, string SourceCommit, string SourcePath, string AuthoredField,
	bool LocalOverride, string PackageVersion = "", string SourceBlobHash = "",
	string ExtractionRuleVersion = "", string EvidenceLocation = "");
public sealed record SemanticFact(PageRef Subject, string Property, SemanticValue Value, FactProvenance Provenance);
public sealed record SemanticSchema(long Version, ImmutableArray<PropertyDefinition> Properties);
public sealed record CanonicalFactSet(long SchemaVersion, ImmutableArray<SemanticFact> Facts);
public enum ValidationCode { InvalidSchema, InvalidKeyword, UnknownProperty, WrongType, InvalidEnum, Cardinality, Quota, MissingTarget }
public sealed record ValidationFailure(ValidationCode Code, string Detail);
public union ValidationResult(CanonicalFactSet, ValidationFailure);
/// <summary>How many facts a page may state, and how large they may be.</summary>
public static class WikiSemanticPolicy
{
	public const int MaxFacts = 128;
	public const int MaxValuesPerProperty = 32;
	public const int MaxFactBytes = 64 * 1024;
	public const int MaxKeywordBytes = 256;
}
