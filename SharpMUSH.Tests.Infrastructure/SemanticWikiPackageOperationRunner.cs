using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

internal sealed class SemanticWikiPackageOperationRunner : IPackageOperationRunner
{
	private readonly PackageOperationRunner _inner;
	internal SemanticFixtureCapability Owner { get; }
	internal SemanticWikiPackageOperationRunner(PackageOperationRunner inner, SemanticFixtureCapability owner)
	{
		_inner = inner;
		Owner = owner;
		owner.RegisterPackageFence(runner: true);
	}
	public Task<PackageOperationOutcome<T>> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null)
		=> Owner.AdmitPackageMutation() ? _inner.RunAsync(operation, body, cancellationToken, provenance)
			: Task.FromResult<PackageOperationOutcome<T>>(new PackageOperationRefused("Package mutations are unavailable in this atomic wiki fixture slice."));
}
