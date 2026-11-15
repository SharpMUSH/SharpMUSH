using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>A world comes with the official package library configured as a remote, once.</summary>
public class OfficialPackageRemoteDefaultTests
{
	private static readonly PackageRemoteRecord Library = new(
		OfficialPackageRemote.Name, OfficialPackageRemote.Url, PackageRemoteTrust.Official, null);

	private static IPackageRegistryService Registry(params PackageRemoteRecord[] remotes)
	{
		var registry = Substitute.For<IPackageRegistryService>();
		registry.GetPackageRemotesAsync().Returns(remotes);
		return registry;
	}

	private static IExpandedObjectDataService ServerData(bool offered)
	{
		var serverData = Substitute.For<IExpandedObjectDataService>();
		serverData.GetExpandedServerDataAsync<OfficialPackageRemoteOffered>()
			.Returns(new ValueTask<OfficialPackageRemoteOffered?>(offered ? new OfficialPackageRemoteOffered { Offered = true } : null));
		return serverData;
	}

	[Test]
	public async Task World_WithoutTheLibrary_GetsIt()
	{
		var registry = Registry(new PackageRemoteRecord("mine", "https://git.example.org/mine", PackageRemoteTrust.Community, null));
		var serverData = ServerData(offered: false);

		var added = await OfficialPackageRemoteDefault.RunAsync(registry, serverData, NullLogger.Instance);

		await Assert.That(added).IsTrue();
		await registry.Received(1).UpsertPackageRemoteAsync(Library);
		await serverData.Received(1).SetExpandedServerDataAsync(
			Arg.Is<OfficialPackageRemoteOffered>(o => o.Offered), Arg.Any<bool>());
	}

	/// <summary>A library the administrator removed is not added again on the next boot.</summary>
	[Test]
	public async Task World_AlreadyOffered_IsLeftAlone()
	{
		var registry = Registry();

		var added = await OfficialPackageRemoteDefault.RunAsync(registry, ServerData(offered: true), NullLogger.Instance);

		await Assert.That(added).IsFalse();
		await registry.DidNotReceive().UpsertPackageRemoteAsync(Arg.Any<PackageRemoteRecord>());
	}

	[Test]
	[Arguments("Packages", OfficialPackageRemote.FormerUrl + ".git")]
	[Arguments("Packages", OfficialPackageRemote.Url + "/")]
	[Arguments(OfficialPackageRemote.Name, "https://git.example.org/mine")]
	[Arguments("sharpmush official", "https://git.example.org/mine")]
	public async Task World_WithTheLibraryOrItsName_KeepsItsRemote(string name, string url)
	{
		var registry = Registry(new PackageRemoteRecord(name, url, PackageRemoteTrust.Community, null));
		var serverData = ServerData(offered: false);

		var added = await OfficialPackageRemoteDefault.RunAsync(registry, serverData, NullLogger.Instance);

		await Assert.That(added).IsFalse();
		await registry.DidNotReceive().UpsertPackageRemoteAsync(Arg.Any<PackageRemoteRecord>());
		await serverData.Received(1).SetExpandedServerDataAsync(
			Arg.Is<OfficialPackageRemoteOffered>(o => o.Offered), Arg.Any<bool>());
	}
}
