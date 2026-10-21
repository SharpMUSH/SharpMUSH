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
	private const string ReadRequirementRefusal = "A package that declares read requirements for its wiki pages cannot be applied to an active semantic wiki yet: "
		+ "a read requirement changes which readers see a page, and that rewrite is not supported.";
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
	/// <summary>An active fixture admits installer work only inside a live coordinated operation.</summary>
	private bool Admit() => Owner.AdmitPackageMutation(coordinated: _provider.CurrentSemanticFixturePackageOperation is not null);

	/// <summary>Test seam: runs after the attempt is retained and immediately before the installer's own work.</summary>
	internal Func<Task>? BeforeInstallerWorkForTests { get; set; }

	/// <summary>
	/// One attempt per installer operation. Inside a coordinated operation the attempt is that operation's
	/// own, so the coordinator can still settle it; otherwise it is a retained attempt of its own that owns
	/// the installer's flow, so its transactions join it, and it never settles.
	/// </summary>
	private async Task<T> AttemptAsync<T>(Func<Task<T>> run, CancellationToken cancellationToken)
	{
		// Joining is atomic with the root's liveness: a root released after the ambient was captured is not
		// joined, and the work falls back to an attempt of its own. Either way it runs under a retained hold.
		if (_provider.JoinSemanticFixturePackageOperation() is { } joined)
		{
			await using (joined)
			{
				var verdict = PackageWriteVerdict.Abandoned;
				try
				{
					await _provider.BeginSemanticFixturePackageAttemptAsync(Owner, cancellationToken, joined.Root);
					cancellationToken.ThrowIfCancellationRequested();
					if (BeforeInstallerWorkForTests is { } joinedSeam) await joinedSeam();
					var result = await run();
					verdict = PackageWriteVerdict.Committed;
					return result;
				}
				finally { joined.Report(verdict); }
			}
		}

		await using var attempt = await _provider.BeginRetainedSemanticFixturePackageOperationAsync(Owner, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		using (SemanticFixturePackageOperation.EnterAmbient(attempt))
		{
			if (BeforeInstallerWorkForTests is { } ownSeam) await ownSeam();
			return await run();
		}
	}

	public Task<PackageChangeset> PlanAsync(PackageManifest manifest, IReadOnlyDictionary<string, string>? configureAnswers = null, CancellationToken cancellationToken = default)
		=> _inner.PlanAsync(manifest, configureAnswers, cancellationToken);
	public Task<Found<string>> GetReadmeAsync(string packageId) => _inner.GetReadmeAsync(packageId);
	public async Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, PackageApplyRequest request,
		CancellationToken cancellationToken = default, IPackageFileSource? fileSource = null)
	{
		// Refused before admission, so a refused apply begins nothing and writes nothing.
		if (Owner.Phase == SemanticFixturePhase.Active && manifest.WikiPages.Any(page => page.Requires.View.Count > 0))
			return new Error<string>(ReadRequirementRefusal);
		if (!Admit()) return new Error<string>(Refusal);
		return await AttemptAsync(() => _inner.ApplyAsync(manifest, request, cancellationToken, fileSource), cancellationToken);
	}
	public async Task<Result<Success>> UninstallAsync(string packageId, bool force = false, CancellationToken cancellationToken = default)
	{
		if (!Admit()) return new Error<string>(Refusal);
		return await AttemptAsync(() => _inner.UninstallAsync(packageId, force, cancellationToken), cancellationToken);
	}
	public async Task<Result<PackageRollbackResult>> RollbackAsync(string packageId, int revision, CancellationToken cancellationToken = default)
	{
		if (!Admit()) return new Error<string>(Refusal);
		return await AttemptAsync(() => _inner.RollbackAsync(packageId, revision, cancellationToken), cancellationToken);
	}
}
