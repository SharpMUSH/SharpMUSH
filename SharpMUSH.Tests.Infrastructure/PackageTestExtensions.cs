using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

public static class PackageTestExtensions
{
	/// <summary>
	/// Force-uninstalls <paramref name="packageId"/> as a test's cleanup does, and first the bundled bridges over it to
	/// +help and +config, as the packages page does: a bridge left behind would keep naming the objects of a package that
	/// is gone, and the next install of it would not register again. Both steps run in one hold of the package gate, as the
	/// packages page's do, so another test's install cannot put a bridge back between them.
	/// </summary>
	public static Task<Result<Success>> UninstallWithBridgesAsync(this IServiceProvider services, string packageId)
		=> services.GetRequiredService<IPackageOperationGate>().RunAsync(async () =>
		{
			await services.GetRequiredService<IBundledPackageBootstrap>().UninstallBridgesOverAsync(packageId, CancellationToken.None);
			return await services.GetRequiredService<IPackageInstallService>().UninstallAsync(packageId, force: true, CancellationToken.None);
		});

	/// <summary>
	/// Installs <paramref name="packageId"/> from the package library checkout (<see cref="TestPaths.PackageLibrary"/>),
	/// recorded as from the official library, as a game that installed it from there would have it.
	/// </summary>
	public static async Task<Result<PackageApplyResult>> ApplyFromLibraryAsync(this IServiceProvider services, string packageId)
	{
		var directory = Path.Join(TestPaths.PackageLibrary, packageId);
		if (services.GetRequiredService<IPackageManifestService>().ParseManifest(await File.ReadAllTextAsync(Path.Join(directory, "package.yaml")))
			is not ParsedPackageManifest parsed)
		{
			return new Error<string>($"{packageId}/package.yaml in the package library does not parse.");
		}

		var request = new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH.Packages", packageId + "/", "library-checkout", "main"),
			new Dictionary<string, string>(), []);
		return await services.GetRequiredService<IPackageInstallService>()
			.ApplyAsync(parsed.Manifest, request, fileSource: new DirectoryPackageFileSource(directory));
	}
}
