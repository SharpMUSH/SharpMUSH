namespace SharpMUSH.Tools.WikiSemantics.Native;

public static class NativeFixtureLimits
{
	public const int MaxFacts = 128;
	public const int MaxFactBytes = 64 * 1024;
	public const int MaxReadBytes = 128 * 1024;
	public const int MaxDeltaBytes = 256 * 1024;
	public const int MaxSummaryBytes = 16 * 1024;
	public const int MaxDescriptorBytes = 8 * 1024;
}

public enum NativeScalarTag : byte { PageRef = 1, Integer = 2, Boolean = 3, Keyword = 4 }
public enum NativeRegistryKind : byte { Page = 1, Property = 2, Keyword = 3, Collection = 4, Class = 5 }
public union NativePreparationResult(PreparedNativePage, ValidationFailure);
public sealed class PreparedNativePage
{
	internal PreparedNativePage(PageSnapshot page, CanonicalFactSet facts, long expectedStateVersion,
		Guid mutationId, bool deleted, string requestDigest, string schemaIdentityDigest)
		=> (Page, Facts, ExpectedStateVersion, MutationId, Deleted, RequestDigest, SchemaIdentityDigest) = (page, facts, expectedStateVersion, mutationId, deleted, requestDigest, schemaIdentityDigest);
	public PageSnapshot Page { get; }
	public CanonicalFactSet Facts { get; }
	public long ExpectedStateVersion { get; }
	public Guid MutationId { get; }
	public bool Deleted { get; }
	public string RequestDigest { get; }
	internal string SchemaIdentityDigest { get; }
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
