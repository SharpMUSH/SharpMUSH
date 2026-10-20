using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

internal sealed class SemanticWikiPackageOperationRunner : IPackageOperationRunner, IPackageOperationLifetime
{
	private readonly PackageOperationRunner _inner;
	private readonly LightningDatabase _provider;
	private readonly HashSet<Task> _releasing = [];
	internal SemanticFixtureCapability Owner { get; }
	internal SemanticWikiPackageOperationRunner(PackageOperationRunner inner, SemanticFixtureCapability owner, LightningDatabase provider)
	{
		_inner = inner;
		_provider = provider;
		Owner = owner;
		owner.RegisterPackageFence(runner: true);
	}
	public async Task<PackageOperationOutcome<T>> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null)
	{
		if (!Owner.AdmitPackageMutation())
			return new PackageOperationRefused("Package mutations are unavailable in this atomic wiki fixture slice.");
		var execution = await StartRetainedAsync(operation, body, cancellationToken, provenance);
		return await execution.Completion.WaitAsync(cancellationToken);
	}

	public async Task<PackageOperationExecution<T>> StartAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null)
	{
		if (!Owner.AdmitPackageMutation()) throw SemanticFixtureCapability.Unavailable();
		return await StartRetainedAsync(operation, body, cancellationToken, provenance);
	}

	/// <summary>
	/// Retains the fixture from before the inner coordinator starts until its completion and observation have
	/// settled, deferred hook releases included, so a close cannot dispose the world under queued work. A
	/// caller whose observation is cancelled does not end the hold.
	/// </summary>
	private async Task<PackageOperationExecution<T>> StartRetainedAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken, Func<CancellationToken, Task<BackupProvenance>>? provenance)
	{
		var held = await _provider.BeginRetainedSemanticFixturePackageOperationAsync(Owner, cancellationToken);
		PackageOperationExecution<T> execution;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			execution = await _inner.StartAsync(operation, body, cancellationToken, provenance);
		}
		catch
		{
			await held.DisposeAsync();
			throw;
		}
		var released = ReleaseAfterAsync(held, execution.Completion, execution.Observation);
		lock (_releasing) _releasing.Add(released);
		_ = released.ContinueWith(done => { lock (_releasing) _releasing.Remove(done); }, System.Threading.Tasks.TaskScheduler.Default);
		return new PackageOperationExecution<T>(released, execution.Observation);
	}

	/// <summary>The inner completion, returned only after the hold it kept has been released.</summary>
	private static async Task<PackageOperationOutcome<T>> ReleaseAfterAsync<T>(SemanticFixturePackageOperation held,
		Task<PackageOperationOutcome<T>> completion, Task observation)
	{
		try
		{
			await ((Task)completion).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			await observation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
		finally { await held.DisposeAsync(); }
		return await completion;
	}

	/// <summary>
	/// Completes once every operation this coordinator started has joined and released its hold. A caller whose
	/// cancelled observation returned early waits here before closing the world.
	/// </summary>
	internal async Task WhenIdleAsync()
	{
		Task[] pending;
		lock (_releasing) pending = [.. _releasing];
		await Task.WhenAll(pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
	}
}
