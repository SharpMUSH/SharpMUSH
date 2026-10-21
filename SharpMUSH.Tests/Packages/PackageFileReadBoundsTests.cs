using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A package's files are read only when asked for and only when they fit (#2028): an unused blob beside a small
/// manifest is never read, a file over the reader's limit is refused before it is copied, and a pack's page
/// bodies have a total as well as a per-page limit.
/// </summary>
public class PackageFileReadBoundsTests
{
	private sealed record Fixture(string Origin, string Cache) : IDisposable
	{
		public PackageRemoteRecord Remote => new("Bounds", Origin, PackageRemoteTrust.Community, "main");

		public GitPackageSourceService Service() => new(new PackageManifestService(), Cache);

		/// <summary>The service's clone of <see cref="Origin"/>, at the path it keeps it.</summary>
		public string Cached => Path.Join(Cache,
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Origin)))[..16].ToLowerInvariant());

		public void Dispose() => Directory.Delete(Path.GetDirectoryName(Origin)!, recursive: true);
	}

	private static Fixture NewFixture()
	{
		var root = Path.Join(Path.GetTempPath(), $"sharpmush-filebounds-{Guid.NewGuid():N}");
		var fixture = new Fixture(Path.Join(root, "origin"), Path.Join(root, "cache"));
		Directory.CreateDirectory(fixture.Origin);
		Git(fixture.Origin, "init", "-b", "main");
		Git(fixture.Origin, "config", "user.email", "test@example.com");
		Git(fixture.Origin, "config", "user.name", "Fixture");
		return fixture;
	}

	private static string Git(string directory, params string[] args)
	{
		var psi = new ProcessStartInfo("git")
		{
			WorkingDirectory = directory,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (var arg in args)
		{
			psi.ArgumentList.Add(arg);
		}

		using var process = Process.Start(psi)!;
		var output = process.StandardOutput.ReadToEnd();
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		return process.ExitCode == 0
			? output.Trim()
			: throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {error}");
	}

	private static void Write(Fixture fixture, string path, byte[] bytes)
	{
		var full = Path.Join(fixture.Origin, path);
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllBytes(full, bytes);
	}

	private static string Commit(Fixture fixture, string message)
	{
		Git(fixture.Origin, "add", "-A");
		Git(fixture.Origin, "commit", "-m", message);
		return Git(fixture.Origin, "rev-parse", "HEAD");
	}

	/// <summary>Takes <paramref name="path"/>'s blob out of the service's clone, so any read of it fails.</summary>
	private static void RemoveFromClone(Fixture fixture, string path)
	{
		var id = Git(fixture.Cached, "rev-parse", $"HEAD:{path}");
		var packs = Path.Join(fixture.Cached, ".git", "objects", "pack");
		foreach (var pack in Directory.Exists(packs) ? Directory.GetFiles(packs, "*.pack") : [])
		{
			var moved = Path.Join(Path.GetDirectoryName(fixture.Origin)!, Path.GetFileName(pack));
			File.Move(pack, moved);
			foreach (var index in Directory.GetFiles(packs))
			{
				File.Delete(index);
			}

			var unpack = new ProcessStartInfo("git") { WorkingDirectory = fixture.Cached, RedirectStandardInput = true };
			unpack.ArgumentList.Add("unpack-objects");
			using var process = Process.Start(unpack)!;
			using (var input = File.OpenRead(moved))
			{
				input.CopyTo(process.StandardInput.BaseStream);
			}

			process.StandardInput.Close();
			process.WaitForExit();
		}

		File.Delete(Path.Join(fixture.Cached, ".git", "objects", id[..2], id[2..]));
	}

	[Test]
	public async Task GitSource_NeverReadsABlobThePackageDoesNotAskFor()
	{
		using var fixture = NewFixture();
		Write(fixture, "pack/package.yaml", "package: pack\nversion: \"1.0.0\"\n"u8.ToArray());
		Write(fixture, "pack/README.md", "# Pack\n"u8.ToArray());
		Write(fixture, "pack/unused/big.bin", RandomNumberGenerator.GetBytes(4 * 1024 * 1024));
		var commit = Commit(fixture, "pack");
		var service = fixture.Service();
		await service.RefreshAsync(fixture.Remote);
		// The snapshot this replaced copied every blob under the package; with this one gone it could not.
		RemoveFromClone(fixture, "pack/unused/big.bin");

		var files = (await service.GetFileSourceAsync(fixture.Remote, "pack", commit)).Expect<IPackageFileSource>();

		await Assert.That(await PackageReadme.ReadAsync(files) is string readme && readme == "# Pack\n").IsTrue();
		await Assert.That(await files.ReadFileAsync("missing.md")).IsNull();
	}

	[Test]
	public async Task GitSource_RefusesAFileOverTheLimitBeforeCopyingIt()
	{
		using var fixture = NewFixture();
		var big = RandomNumberGenerator.GetBytes(256 * 1024);
		Write(fixture, "pack/package.yaml", "package: pack\nversion: \"1.0.0\"\n"u8.ToArray());
		Write(fixture, "pack/wiki/big.md", big);
		var commit = Commit(fixture, "pack");
		var files = (await fixture.Service().GetFileSourceAsync(fixture.Remote, "pack", commit)).Expect<IPackageFileSource>();

		var refused = await files.ReadFileAsync("wiki/big.md", 1024);
		await Assert.That(refused.Expect<PackageFileTooLarge>().Length).IsEqualTo(big.LongLength);
		var exact = await files.ReadFileAsync("wiki/big.md", big.LongLength);
		await Assert.That(exact.Expect<byte[]>().SequenceEqual(big)).IsTrue();
	}

	[Test]
	public async Task GitSource_ReadsThePinnedCommitAfterTheBranchMoves()
	{
		using var fixture = NewFixture();
		Write(fixture, "pack/package.yaml", "package: pack\nversion: \"1.0.0\"\n"u8.ToArray());
		Write(fixture, "pack/wiki/page.md", "reviewed"u8.ToArray());
		var reviewed = Commit(fixture, "reviewed");
		var service = fixture.Service();
		var files = (await service.GetFileSourceAsync(fixture.Remote, "pack", reviewed)).Expect<IPackageFileSource>();

		Write(fixture, "pack/wiki/page.md", "moved"u8.ToArray());
		Write(fixture, "pack/wiki/later.md", "later"u8.ToArray());
		Commit(fixture, "moved");
		await service.RefreshAsync(fixture.Remote);

		await Assert.That(Encoding.UTF8.GetString((await files.ReadFileAsync("wiki/page.md"))!)).IsEqualTo("reviewed");
		await Assert.That(await files.ReadFileAsync("wiki/later.md")).IsNull();
	}

	[Test]
	public async Task GitSource_DirectReadmeOverTheLimitIsRefused()
	{
		using var fixture = NewFixture();
		Write(fixture, "pack/package.yaml", "package: pack\nversion: \"1.0.0\"\n"u8.ToArray());
		Write(fixture, "pack/README.md", Encoding.UTF8.GetBytes(new string('a', PackageReadme.MaxBytes + 1)));
		Commit(fixture, "pack");

		var readme = await fixture.Service().GetReadmeAsync(fixture.Remote, "pack");

		await Assert.That(readme.Expect<Error<string>>().Value).Contains("larger than");
	}

	[Test]
	public async Task DirectorySource_RefusesAFileOverTheLimit()
	{
		var directory = Directory.CreateTempSubdirectory("sharpmush-filebounds-").FullName;
		try
		{
			await File.WriteAllBytesAsync(Path.Join(directory, "big.md"), new byte[2048]);
			var files = new DirectoryPackageFileSource(directory);

			await Assert.That((await files.ReadFileAsync("big.md", 1024)).Expect<PackageFileTooLarge>().Length).IsEqualTo(2048);
			await Assert.That((await files.ReadFileAsync("big.md", 2048)).Expect<byte[]>().Length).IsEqualTo(2048);
			await Assert.That((await files.ReadFileAsync("../big.md", 2048)).Value).IsTypeOf<NotFound>();
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>Counts reads and the limit each was given; every path holds the same bytes.</summary>
	private sealed class Pages(byte[] body, Action<int>? onRead = null) : IPackageFileSource
	{
		public List<long> Limits { get; } = [];

		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
			Task.FromResult<byte[]?>(body);

		public Task<PackageFileRead> ReadFileAsync(string path, long maxBytes, CancellationToken cancellationToken = default)
		{
			Limits.Add(maxBytes);
			onRead?.Invoke(Limits.Count);
			return Task.FromResult<PackageFileRead>(body.LongLength > maxBytes ? new PackageFileTooLarge(body.LongLength) : body);
		}
	}

	private static PackageManifest Manifest(int pages) => new PackageManifestService().ParseManifest(
		"format: 1.4\npackage: pages\nversion: 1.0.0\nwiki:\n" + string.Concat(Enumerable.Range(0, pages)
			.Select(n => $"  - page: main:page{n}\n    title: Page {n}\n    file: wiki/page{n}.md\n")))
		.Expect<ParsedPackageManifest>().Manifest;

	[Test]
	public async Task WikiBodies_PagesUnderTheirOwnLimitStillHaveATotal()
	{
		// Each page fits the 1 MiB per-page limit; together they pass the total.
		var body = Encoding.UTF8.GetBytes(new string('a', PackageWikiBodies.MaxBodyBytes - 1000));
		var pages = (int)(PackageWikiBodies.MaxTotalBytes / body.Length) + 2;
		var files = new Pages(body);

		var load = await PackageWikiBodies.LoadAsync(Manifest(pages), files);

		await Assert.That(load.Expect<Error<string>>().Value).Contains("in all");
		// The read past the total was asked for no more than what was left, and nothing was read after it.
		await Assert.That(files.Limits.Count).IsEqualTo(pages - 1);
		await Assert.That(files.Limits[^1]).IsEqualTo(PackageWikiBodies.MaxTotalBytes - (pages - 2) * body.LongLength);
	}

	[Test]
	public async Task WikiBodies_CancellationStopsTheReads()
	{
		using var cancellation = new CancellationTokenSource();
		var files = new Pages("body"u8.ToArray(), count => { if (count == 3) cancellation.Cancel(); });

		await Assert.ThrowsAsync<OperationCanceledException>(async () =>
			await PackageWikiBodies.LoadAsync(Manifest(10), files, cancellation.Token));
		await Assert.That(files.Limits.Count).IsEqualTo(3);
	}
}
