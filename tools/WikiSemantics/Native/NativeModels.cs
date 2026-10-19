using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki.Semantics;
namespace SharpMUSH.Tools.WikiSemantics.Native;

public static class NativeFixtureLimits
{
	public const int MaxFacts = WikiSemanticLimits.MaxFacts;
	public const int MaxFactBytes = WikiSemanticLimits.MaxFactBytes;
	public const int MaxReadBytes = WikiSemanticLimits.MaxReadBytes;
	public const int MaxDeltaBytes = WikiSemanticLimits.MaxDeltaBytes;
	public const int MaxSummaryBytes = WikiSemanticLimits.MaxSummaryBytes;
	public const int MaxDescriptorBytes = WikiSemanticLimits.MaxDescriptorBytes;
}

public enum NativeScalarTag : byte
{
	PageRef = (byte)WikiSemanticScalarTag.PageRef, Integer = (byte)WikiSemanticScalarTag.Integer,
	Boolean = (byte)WikiSemanticScalarTag.Boolean, Keyword = (byte)WikiSemanticScalarTag.Keyword
}
public enum NativeRegistryKind : byte
{
	Page = (byte)WikiSemanticRegistryKind.Page, Property = (byte)WikiSemanticRegistryKind.Property,
	Keyword = (byte)WikiSemanticRegistryKind.Keyword, Collection = (byte)WikiSemanticRegistryKind.Collection,
	Class = (byte)WikiSemanticRegistryKind.Class
}
public union NativePreparationResult(PreparedNativePage, ValidationFailure);
public sealed class PreparedNativePage
{
	internal PreparedNativePage(PageSnapshot page, CanonicalFactSet facts, long expectedStateVersion,
		Guid mutationId, bool deleted, string requestDigest, string schemaIdentityDigest, PreparedWikiProjection projection)
		=> (Page, Facts, ExpectedStateVersion, MutationId, Deleted, RequestDigest, SchemaIdentityDigest, Projection) = (page, facts, expectedStateVersion, mutationId, deleted, requestDigest, schemaIdentityDigest, projection);
	public PageSnapshot Page { get; }
	public CanonicalFactSet Facts { get; }
	public long ExpectedStateVersion { get; }
	public Guid MutationId { get; }
	public bool Deleted { get; }
	public string RequestDigest { get; }
	internal string SchemaIdentityDigest { get; }
	internal PreparedWikiProjection Projection { get; }
}
public sealed record NativePageSummary(PageRef Reference, ulong NativeId, string Title,
	VisibilityDescriptor Visibility, string Collection, bool Deleted, long StateVersion);
public sealed record NativeStoredPage(PageSnapshot Page, CanonicalFactSet Facts,
	NativePageSummary Summary, long CommitSequence)
{
	public long StateVersion => Summary.StateVersion;
}
public union NativeCommitResult(NativeCommitted, NativeConflict, NativeCapacity, NativeMutationMismatch, NativeUnavailable);
public sealed record NativeCommitted(Guid MutationId, long StateVersion, long CommitSequence);
public sealed record NativeConflict(long CurrentStateVersion);
public sealed record NativeCapacity(string Dimension);
public sealed record NativeMutationMismatch;
public sealed record NativeUnavailable;
public sealed record NativeMutationStatus(Guid MutationId, string RequestDigest, NativeCommitted Committed);
