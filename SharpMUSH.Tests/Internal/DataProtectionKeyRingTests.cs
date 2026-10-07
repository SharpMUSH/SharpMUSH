using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Tests.Commands;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// The Data Protection key ring is kept beside the world, on the volume that holds it, not in the
/// container's home directory where a recreate loses it (#1669).
/// </summary>
public class DataProtectionKeyRingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Standard { get; init; }

	[ClassDataSource<RealityGameServerFactory>(Shared = SharedType.PerTestSession)]
	public required RealityGameServerFactory Reality { get; init; }

	private static DirectoryInfo KeyRing(IServiceProvider services) =>
		services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository is FileSystemXmlRepository repository
			? repository.Directory
			: throw new InvalidOperationException("The key ring is not kept on the file system.");

	[Test]
	public async Task KeyRingSitsBesideTheWorld()
	{
		var world = Path.GetFullPath(Standard.Services.GetRequiredService<LightningWorldPath>().Value);
		var keyRing = KeyRing(Standard.Services);

		await Assert.That(keyRing.FullName).IsEqualTo(world + ".dataprotection-keys");
		await Assert.That(keyRing.Parent?.FullName).IsEqualTo(Path.GetDirectoryName(world));
	}

	[Test]
	public async Task ProtectedDataRoundTripsThroughKeysWrittenToTheKeyRing()
	{
		var protector = Standard.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(nameof(DataProtectionKeyRingTests));
		var secret = TestIsolationHelpers.GenerateUniqueName("Protected");

		await Assert.That(protector.Unprotect(protector.Protect(secret))).IsEqualTo(secret);
		await Assert.That(KeyRing(Standard.Services).EnumerateFiles("key-*.xml").Any()).IsTrue();
	}

	[Test]
	public async Task HostWithAWorldOfItsOwnHasAKeyRingOfItsOwn()
	{
		await Assert.That(KeyRing(Reality.Services).FullName).IsNotEqualTo(KeyRing(Standard.Services).FullName);
	}
}
