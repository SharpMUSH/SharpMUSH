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
		await _provider.BeginSemanticFixturePackageAttemptAsync(Owner, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		return await _inner.RunAsync(operation, body, cancellationToken, provenance);
	}

	public async Task<PackageOperationExecution<T>> StartAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null)
	{
		if (!Owner.AdmitPackageMutation()) throw SemanticFixtureCapability.Unavailable();
		await _provider.BeginSemanticFixturePackageAttemptAsync(Owner, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		return await _inner.StartAsync(operation, body, cancellationToken, provenance);
	}
}
