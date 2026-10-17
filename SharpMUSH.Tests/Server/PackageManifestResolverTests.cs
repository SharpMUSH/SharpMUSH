using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Plugins;

namespace SharpMUSH.Tests.Server;

public class PackageManifestResolverTests
{
	private sealed class MutableFiles : IPackageFileSource
	{
		public byte[] Bytes { get; set; } = [1, 2, 3];
		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
			=> Task.FromResult<byte[]?>(path switch
			{
				PluginPackageFixture.DllName => Bytes,
				"plugin.json" => PluginPackageFixture.PluginJson("sample"),
				_ => null
			});
	}
	private static readonly PackageRemoteRecord Remote = new("test", "https://example.test/repo", PackageRemoteTrust.Unknown, "main");
	private const string Yaml = "package: sample\nversion: 1.0.0\nobjects:\n  - ref: root\n    name: Sample\n    type: thing\n";

	private sealed class Fixture : IDisposable
	{
		public IPackageRegistryService Registry { get; } = Substitute.For<IPackageRegistryService>();
		public IPackageSourceService Source { get; } = Substitute.For<IPackageSourceService>();
		private readonly Library.Plugins.PluginDirectories directories = PluginPackageFixture.ScratchDirectories();
		public PackageManifestResolver Resolver { get; }
		public Fixture()
		{
			var manifests = new PackageManifestService();
			Registry.GetPackageRemoteAsync("test").Returns(Remote);
			Source.RefreshAsync(Remote, Arg.Any<CancellationToken>()).Returns(new PackageRepoSnapshot("test", Remote.Url, "new", []));
			Source.GetManifestAsync(Remote, "sample", null, Arg.Any<CancellationToken>()).Returns(new PackageManifestSource(Yaml, "new", null));
			Source.GetFileSourceAsync(Remote, "sample", "new", Arg.Any<CancellationToken>()).Returns(new Error<string>("no files"));
			Resolver = new(Registry, Source, manifests, new PluginUploadStore(directories, manifests, NullLogger<PluginUploadStore>.Instance));
		}
		public void Dispose() => PluginPackageFixture.Delete(directories);
	}

	[Test]
	public async Task RefreshesBeforeResolvingAndBindsTrust()
	{
		using var f = new Fixture();
		var first = (await f.Resolver.ResolveAsync(new("test", "sample"), true, CancellationToken.None)).Expect<ResolvedPackageManifest>();
		Received.InOrder(() =>
		{
			f.Source.RefreshAsync(Remote, Arg.Any<CancellationToken>());
			f.Source.GetManifestAsync(Remote, "sample", null, Arg.Any<CancellationToken>());
		});
		f.Registry.GetPackageRemoteAsync("test").Returns(Remote with { Trust = PackageRemoteTrust.Official });
		f.Source.GetManifestAsync(Arg.Any<PackageRemoteRecord>(), "sample", null, Arg.Any<CancellationToken>()).Returns(new PackageManifestSource(Yaml, "new", null));
		f.Source.GetFileSourceAsync(Arg.Any<PackageRemoteRecord>(), "sample", "new", Arg.Any<CancellationToken>()).Returns(new Error<string>("no files"));
		var second = (await f.Resolver.ResolveAsync(new("test", "sample"), false, CancellationToken.None)).Expect<ResolvedPackageManifest>();
		await Assert.That(first.Fingerprint).IsNotEqualTo(second.Fingerprint);
	}

	[Test]
	public async Task RefreshFailureDoesNotUseCachedManifest()
	{
		using var f = new Fixture();
		f.Source.RefreshAsync(Remote, Arg.Any<CancellationToken>()).Returns(new Error<string>("fetch failed"));
		var failure = (await f.Resolver.ResolveAsync(new("test", "sample"), true, CancellationToken.None)).Expect<PackageManifestResolutionFailure>();
		await Assert.That(failure.Message).Contains("fetch failed");
		await f.Source.DidNotReceive().GetManifestAsync(Arg.Any<PackageRemoteRecord>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task OrdinaryResolutionPreservesCacheAndInvalidManifestStatus()
	{
		using var f = new Fixture();
		f.Source.GetManifestAsync(Remote, "sample", null, Arg.Any<CancellationToken>()).Returns(new PackageManifestSource("not: a package", "new", null));
		var failure = (await f.Resolver.ResolveAsync(new("test", "sample"), false, CancellationToken.None)).Expect<PackageManifestResolutionFailure>();
		await Assert.That(failure.StatusCode).IsEqualTo(422);
		await Assert.That(failure.Issues).IsNotEmpty();
		await f.Source.DidNotReceive().RefreshAsync(Arg.Any<PackageRemoteRecord>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task CatalogueHonoursVersionAndNormalizesPath()
	{
		using var f = new Fixture();
		var id = BundledPackages.All.First().PackageId;
		var found = (await f.Resolver.ResolveAsync(new(BundledPackages.RemoteName, $"/{id}/"), false, CancellationToken.None)).Expect<ResolvedPackageManifest>();
		await Assert.That(found.ApplyPath).IsEqualTo(id);
		var failure = (await f.Resolver.ResolveAsync(new(BundledPackages.RemoteName, id, "9999.0.0"), false, CancellationToken.None)).Expect<PackageManifestResolutionFailure>();
		await Assert.That(failure.StatusCode).IsEqualTo(404);
	}

	[Test]
	public async Task BinarySnapshotAndFingerprintBindActualBytes()
	{
		using var f = new Fixture();
		var files = new MutableFiles();
		f.Source.GetManifestAsync(Remote, "sample", null, Arg.Any<CancellationToken>()).Returns(
			new PackageManifestSource(PluginPackageFixture.Yaml("sample", dllSha: new string('0', 64)), "new", null));
		f.Source.GetFileSourceAsync(Remote, "sample", "new", Arg.Any<CancellationToken>()).Returns(files);
		var first = (await f.Resolver.ResolveAsync(new("test", "sample"), false, CancellationToken.None)).Expect<ResolvedPackageManifest>();
		files.Bytes[0] = 9;
		var original = await first.Files!.ReadFileAsync(PluginPackageFixture.DllName);
		await Assert.That(original![0]).IsEqualTo((byte)1);
		original[0] = 8;
		await Assert.That((await first.Files.ReadFileAsync(PluginPackageFixture.DllName))![0]).IsEqualTo((byte)1);
		var second = (await f.Resolver.ResolveAsync(new("test", "sample"), false, CancellationToken.None)).Expect<ResolvedPackageManifest>();
		await Assert.That(first.Fingerprint).IsNotEqualTo(second.Fingerprint);
	}
}
