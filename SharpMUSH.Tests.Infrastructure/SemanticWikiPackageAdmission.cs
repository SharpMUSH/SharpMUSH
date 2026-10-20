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
	/// <summary>
	/// One attempt per installer operation. Inside a coordinated operation the attempt is that operation's
	/// own, so the coordinator can still settle it; otherwise it is a retained attempt of its own that owns
	/// the installer's flow, so its transactions join it, and it never settles.
	/// </summary>
	private async Task<T> AttemptAsync<T>(Func<Task<T>> run, CancellationToken cancellationToken)
	{
		if (_provider.CurrentSemanticFixturePackageOperation is { } coordinated)
		{
			await _provider.BeginSemanticFixturePackageAttemptAsync(Owner, cancellationToken, coordinated);
			cancellationToken.ThrowIfCancellationRequested();
			return await run();
		}

		await using var attempt = await _provider.BeginRetainedSemanticFixturePackageOperationAsync(Owner, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		using (SemanticFixturePackageOperation.EnterAmbient(attempt)) return await run();
	}

	public Task<PackageChangeset> PlanAsync(PackageManifest manifest, IReadOnlyDictionary<string, string>? configureAnswers = null, CancellationToken cancellationToken = default)
		=> _inner.PlanAsync(manifest, configureAnswers, cancellationToken);
	public Task<Found<string>> GetReadmeAsync(string packageId) => _inner.GetReadmeAsync(packageId);
	public async Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, PackageApplyRequest request,
		CancellationToken cancellationToken = default, IPackageFileSource? fileSource = null)
	{
		if (!Owner.AdmitPackageMutation()) return new Error<string>(Refusal);
		return await AttemptAsync(() => _inner.ApplyAsync(manifest, request, cancellationToken, fileSource), cancellationToken);
	}
	public async Task<Result<Success>> UninstallAsync(string packageId, bool force = false, CancellationToken cancellationToken = default)
	{
		if (!Owner.AdmitPackageMutation()) return new Error<string>(Refusal);
		return await AttemptAsync(() => _inner.UninstallAsync(packageId, force, cancellationToken), cancellationToken);
	}
	public async Task<Result<PackageRollbackResult>> RollbackAsync(string packageId, int revision, CancellationToken cancellationToken = default)
	{
		if (!Owner.AdmitPackageMutation()) return new Error<string>(Refusal);
		return await AttemptAsync(() => _inner.RollbackAsync(packageId, revision, cancellationToken), cancellationToken);
	}
}
