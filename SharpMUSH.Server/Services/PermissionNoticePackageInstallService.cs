using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Tells every portal tab to read its permissions again once a package apply, uninstall or rollback has
/// finished, whatever its outcome. A package's roles, permissions and overrides are written straight to the
/// role store, so characters holding them on any account would not hear of it, and a failed apply undoes
/// grants that their account holders were already told about.
/// </summary>
public sealed class PermissionNoticePackageInstallService(IPackageInstallService inner, PortalPermissionNotices notices)
	: IPackageInstallService
{
	public Task<PackageChangeset> PlanAsync(PackageManifest manifest, IReadOnlyDictionary<string, string>? configureAnswers = null,
		CancellationToken cancellationToken = default)
		=> inner.PlanAsync(manifest, configureAnswers, cancellationToken);

	public async Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, PackageApplyRequest request,
		CancellationToken cancellationToken = default, IPackageFileSource? fileSource = null)
	{
		try
		{
			return await inner.ApplyAsync(manifest, request, cancellationToken, fileSource);
		}
		finally
		{
			notices.Everyone();
		}
	}

	public async Task<Result<Success>> UninstallAsync(string packageId, bool force = false, CancellationToken cancellationToken = default)
	{
		try
		{
			return await inner.UninstallAsync(packageId, force, cancellationToken);
		}
		finally
		{
			notices.Everyone();
		}
	}

	public async Task<Result<PackageRollbackResult>> RollbackAsync(string packageId, int revision,
		CancellationToken cancellationToken = default)
	{
		try
		{
			return await inner.RollbackAsync(packageId, revision, cancellationToken);
		}
		finally
		{
			notices.Everyone();
		}
	}

	public Task<Found<string>> GetReadmeAsync(string packageId) => inner.GetReadmeAsync(packageId);
}
