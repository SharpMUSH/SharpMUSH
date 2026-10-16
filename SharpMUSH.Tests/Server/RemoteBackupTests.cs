using Microsoft.Extensions.Configuration;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

public class RemoteBackupTests
{
	[Test]
	[Arguments("/data/lightning")]
	[Arguments("sftp:host:/backup")]
	[Arguments("rest:https://user:password@example.com/repo")]
	[Arguments("s3:https://example.com/bucket?password=secret")]
	[Arguments("b2:bucket:path\n--password-command=evil")]
	public async Task RejectsUnsafeRepositories(string repository)
		=> await Assert.That(RemoteBackupCoordinator.Validate(new(true, 24, repository)) is Error<string>).IsTrue();

	[Test]
	[Arguments("b2:bucket:path")]
	[Arguments("s3:https://s3.example.com/bucket")]
	[Arguments("rest:https://backup.example.com/repo")]
	public async Task AcceptsSupportedRepositories(string repository)
		=> await Assert.That(RemoteBackupCoordinator.Validate(new(true, 24, repository)) is Success).IsTrue();

	[Test]
	public async Task PartialSnapshotIsFailure()
		=> await Assert.That(RemoteBackupCoordinator.IsSuccessful(new(Guid.NewGuid(), "nonce", 3, new string('a', 64), null))).IsFalse();

	[Test]
	public async Task MissingSnapshotIsFailure()
		=> await Assert.That(RemoteBackupCoordinator.IsSuccessful(new(Guid.NewGuid(), "nonce", 0, null, null))).IsFalse();

	[Test]
	public async Task CompleteSnapshotSucceeds()
		=> await Assert.That(RemoteBackupCoordinator.IsSuccessful(new(Guid.NewGuid(), "nonce", 0, new string('a', 64), null))).IsTrue();
	[Test]
	public async Task WikiStorageAndBackupMetadataShareTheConfiguredRootResolver()
	{
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Wiki:AssetRoot"] = "data/custom-wiki-assets"
		}).Build();
		await Assert.That(FileSystemWikiAssetService.ResolveRoot(config)).IsEqualTo(Path.GetFullPath("data/custom-wiki-assets"));
	}

}
