using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

public static class PackageTestExtensions
{
	/// <summary>
	/// Force-uninstalls <paramref name="packageId"/> as a test's cleanup does, and first the bundled bridges over it to
	/// +help and +config, as the packages page does: a bridge left behind would keep naming the objects of a package that
	/// is gone, and the next install of it would not register again.
	/// </summary>
	public static async Task<Result<Success>> UninstallWithBridgesAsync(this IServiceProvider services, string packageId)
	{
		await services.GetRequiredService<IBundledPackageBootstrap>().UninstallBridgesOverAsync(packageId, CancellationToken.None);
		return await services.GetRequiredService<IPackageInstallService>().UninstallAsync(packageId, force: true, CancellationToken.None);
	}
}
