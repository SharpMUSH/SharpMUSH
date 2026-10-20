using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

internal sealed class SemanticWikiPackageAdmission : IPackageInstallService
{
	private const string Refusal = "Package mutations are unavailable in this atomic wiki fixture slice.";
	private readonly PackageInstallService _inner;
	private readonly LightningDatabase _provider;
	internal SemanticFixtureCapability Owner { get; }
	internal SemanticWikiPackageAdmission(PackageInstallService inner, SemanticFixtureCapability owner, LightningDatabase provider)
	{
		_inner = inner;
		_provider = provider;
		Owner = owner;
		owner.RegisterPackageFence(runner: false);
	}
	public Task<PackageChangeset> PlanAsync(PackageManifest manifest, IReadOnlyDictionary<string, string>? configureAnswers = null, CancellationToken cancellationToken = default)
		=> _inner.PlanAsync(manifest, configureAnswers, cancellationToken);
	public Task<Found<string>> GetReadmeAsync(string packageId) => _inner.GetReadmeAsync(packageId);
	public async Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, PackageApplyRequest request,
		CancellationToken cancellationToken = default, IPackageFileSource? fileSource = null)
	{
		if (!Owner.AdmitPackageMutation()) return new Error<string>(Refusal);
		await _provider.BeginSemanticFixturePackageAttemptAsync(Owner, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		return await _inner.ApplyAsync(manifest, request, cancellationToken, fileSource);
	}
	public async Task<Result<Success>> UninstallAsync(string packageId, bool force = false, CancellationToken cancellationToken = default)
	{
		if (!Owner.AdmitPackageMutation()) return new Error<string>(Refusal);
		await _provider.BeginSemanticFixturePackageAttemptAsync(Owner, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		return await _inner.UninstallAsync(packageId, force, cancellationToken);
	}
	public async Task<Result<PackageRollbackResult>> RollbackAsync(string packageId, int revision, CancellationToken cancellationToken = default)
	{
		if (!Owner.AdmitPackageMutation()) return new Error<string>(Refusal);
		await _provider.BeginSemanticFixturePackageAttemptAsync(Owner, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		return await _inner.RollbackAsync(packageId, revision, cancellationToken);
	}
}
