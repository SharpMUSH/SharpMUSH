namespace SharpMUSH.Library.Services;

/// <summary>
/// An uncommitted package operation could not restore every write. Disposal can throw this in
/// place of the exception being unwound; <see cref="PackageWriteTransaction.RevertAsync"/> instead
/// includes the original error and these steps in its result. Recovery may need the pre-operation
/// backup. A recovery deadline never detaches an inverse which has already started.
/// </summary>
public sealed class PackageCompensationException : Exception
{
	public PackageCompensationException(IReadOnlyList<string> unrestoredSteps)
		: base($"Package compensation could not restore: {string.Join("; ", unrestoredSteps)}.")
	{
		UnrestoredSteps = Array.AsReadOnly(unrestoredSteps.ToArray());
	}

	/// <summary>The failed or deadline-skipped inverse steps, in recovery order.</summary>
	public IReadOnlyList<string> UnrestoredSteps { get; }
}
