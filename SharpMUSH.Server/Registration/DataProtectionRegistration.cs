using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning;

namespace SharpMUSH.Server.Registration;

/// <summary>
/// ASP.NET Core Data Protection's key ring. Left to the framework it lands in the home directory, which
/// in a container is the writable layer: every recreate threw the keys away, and anything protected with
/// them (antiforgery, cookie auth, TempData) stopped verifying (#1669).
/// </summary>
internal static class DataProtectionRegistration
{
	/// <summary>Pinned so two hosts sharing one key ring read each other's payloads.</summary>
	public const string ApplicationName = "SharpMUSH";

	/// <summary>
	/// The key ring sits beside the world, as <c>&lt;world path&gt;.dataprotection-keys</c>, so it is on
	/// whatever volume holds the world (<c>/app/data/lightning.dataprotection-keys</c> in the deploy
	/// compose files) and moves with it. <c>SHARPMUSH_DATAPROTECTION_PATH</c> names another directory.
	/// </summary>
	public static string KeyRingPath(string worldPath) =>
		Path.GetFullPath(Environment.GetEnvironmentVariable("SHARPMUSH_DATAPROTECTION_PATH") is { Length: > 0 } configured
			? configured
			: worldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".dataprotection-keys");

	public static IServiceCollection AddSharpMushDataProtection(this IServiceCollection services)
	{
		services.AddDataProtection().SetApplicationName(ApplicationName);

		// What PersistKeysToFileSystem does, but read from the host's LightningWorldPath when options are
		// first resolved, so a test host given a world of its own gets a key ring of its own beside it.
		// The repository creates the directory on first write.
		services.AddOptions<KeyManagementOptions>()
			.Configure<LightningWorldPath, ILoggerFactory>((options, world, loggerFactory) =>
				options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(KeyRingPath(world.Value)), loggerFactory));

		return services;
	}
}
