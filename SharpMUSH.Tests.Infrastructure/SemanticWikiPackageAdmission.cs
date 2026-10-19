using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

internal sealed class SemanticWikiPackageAdmission : IPackageInstallService
{
	private const string Refusal = "Package mutations are unavailable in this atomic wiki fixture slice.";
	private readonly PackageInstallService _inner;
	internal SemanticFixtureCapability Owner { get; }
	internal SemanticWikiPackageAdmission(PackageInstallService inner, SemanticFixtureCapability owner)
	{
		_inner = inner;
		Owner = owner;
		owner.RegisterPackageFence(runner: false);
	}
	public Task<PackageChangeset> PlanAsync(PackageManifest manifest, IReadOnlyDictionary<string, string>? configureAnswers = null, CancellationToken cancellationToken = default)
		=> _inner.PlanAsync(manifest, configureAnswers, cancellationToken);
	public Task<Found<string>> GetReadmeAsync(string packageId) => _inner.GetReadmeAsync(packageId);
	public Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, PackageApplyRequest request,
		CancellationToken cancellationToken = default, IPackageFileSource? fileSource = null)
		=> Owner.AdmitPackageMutation() ? _inner.ApplyAsync(manifest, request, cancellationToken, fileSource)
			: Task.FromResult<Result<PackageApplyResult>>(new Error<string>(Refusal));
	public Task<Result<Success>> UninstallAsync(string packageId, bool force = false, CancellationToken cancellationToken = default)
		=> Owner.AdmitPackageMutation() ? _inner.UninstallAsync(packageId, force, cancellationToken)
			: Task.FromResult<Result<Success>>(new Error<string>(Refusal));
	public Task<Result<PackageRollbackResult>> RollbackAsync(string packageId, int revision, CancellationToken cancellationToken = default)
		=> Owner.AdmitPackageMutation() ? _inner.RollbackAsync(packageId, revision, cancellationToken)
			: Task.FromResult<Result<PackageRollbackResult>>(new Error<string>(Refusal));
}
