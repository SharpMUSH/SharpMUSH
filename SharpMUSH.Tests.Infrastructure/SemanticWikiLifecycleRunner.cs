using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Both lifecycle entry points, held from before any attribute is read until the actual inner runner
/// returns. Inside a coordinated operation the hold joins it, so a deferred hook stays owned by that
/// same coordinator; outside one it is an attempt of its own, which invalidates and never settles.
/// Observed outcomes come from the same inner runner, unchanged.
/// </summary>
internal sealed class SemanticWikiLifecycleRunner(IPackageLifecycleRunner inner, LightningDatabase provider)
	: IPackageLifecycleRunner, IPackageLifecycleObserver
{
	public Task RunLifecycleAsync(PackageChangeset changeset, IReadOnlyDictionary<string, string> createdObjects,
		CancellationToken cancellationToken = default)
		=> HeldAsync(async () => { await inner.RunLifecycleAsync(changeset, createdObjects, cancellationToken); return 0; }, cancellationToken);

	public Task RunLifecycleAsync(string objId, string attribute, CancellationToken cancellationToken = default)
		=> HeldAsync(async () => { await inner.RunLifecycleAsync(objId, attribute, cancellationToken); return 0; }, cancellationToken);

	public Task<PackageLifecycleOutcome> RunObservedLifecycleAsync(string objId, string attribute, CancellationToken cancellationToken = default)
		=> HeldAsync(() => inner is IPackageLifecycleObserver observer
			? observer.RunObservedLifecycleAsync(objId, attribute, cancellationToken)
			: RunUnobservedAsync(objId, attribute, cancellationToken), cancellationToken);

	private async Task<PackageLifecycleOutcome> RunUnobservedAsync(string objId, string attribute, CancellationToken cancellationToken)
	{
		await inner.RunLifecycleAsync(objId, attribute, cancellationToken);
		return PackageLifecycleOutcome.Unknown;
	}

	private async Task<T> HeldAsync<T>(Func<Task<T>> run, CancellationToken cancellationToken)
	{
		await using var hold = await provider.BeginSemanticFixturePackageHoldAsync(cancellationToken);
		try
		{
			var result = await run();
			hold.Report(PackageWriteVerdict.Committed);
			return result;
		}
		catch
		{
			hold.Report(PackageWriteVerdict.Abandoned);
			throw;
		}
	}
}
