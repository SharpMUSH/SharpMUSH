using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A package's README: read beside its manifest, kept with the revision an apply records, and shown for the
/// version installed.
/// </summary>
public class PackageReadmeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private sealed class Files(Dictionary<string, byte[]> files) : IPackageFileSource
	{
		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
			Task.FromResult(files.GetValueOrDefault(path));
	}

	private static Files With(params (string Name, string Text)[] files) =>
		new(files.ToDictionary(f => f.Name, f => Encoding.UTF8.GetBytes(f.Text)));

	[Test]
	public async Task Read_TakesTheFirstNameFound_WithLineEndingsAndBomNormalized()
	{
		var readme = (await PackageReadme.ReadAsync(With(("readme.md", "lower"), ("README.md", "﻿# Title\r\nBody\r\n"))))
			.Expect<string>();

		await Assert.That(readme).IsEqualTo("# Title\nBody\n");
	}

	[Test]
	public async Task Read_NoSource_NoFile_TooLarge_OrNotUtf8_IsNotFound()
	{
		await Assert.That((await PackageReadme.ReadAsync(null)).Value).IsTypeOf<NotFound>();
		await Assert.That((await PackageReadme.ReadAsync(With(("NOTES.md", "x")))).Value).IsTypeOf<NotFound>();
		await Assert.That((await PackageReadme.ReadAsync(With(("README.md", new string('a', PackageReadme.MaxBytes + 1))))).Value)
			.IsTypeOf<NotFound>();
		await Assert.That(PackageReadme.Decode([0xC3, 0x28]).Value).IsTypeOf<NotFound>();
	}

	/// <summary>Every bundled package that has a README in examples/packages ships it in the image.</summary>
	[Test]
	public async Task BundledPackages_EmbedTheirReadmes()
	{
		var examples = Path.Combine(FindRepoRoot(), "examples", "packages");
		foreach (var descriptor in BundledPackages.All)
		{
			var file = Path.Combine(examples, descriptor.PackageId, "README.md");
			if (!File.Exists(file))
			{
				continue;
			}

			var embedded = BundledPackages.EmbeddedReadme(descriptor.PackageId)
				.Expect<string>($"{descriptor.PackageId}'s README.md must be an EmbeddedResource");
			await Assert.That(embedded).IsEqualTo((await File.ReadAllTextAsync(file)).ReplaceLineEndings("\n"));
		}
	}

	/// <summary>A bundled package with no README of its own gets one written from its manifest.</summary>
	[Test]
	public async Task BundledPackage_WithoutReadme_IsDescribedFromItsManifest()
	{
		var descriptor = BundledPackages.Find("wiki-reader").Expect<BundledPackages.Descriptor>();
		await Assert.That(BundledPackages.EmbeddedReadme("wiki-reader").Value).IsTypeOf<NotFound>();

		var manifest = new PackageManifestService().ParseManifest(BundledPackages.ManifestYaml("wiki-reader"))
			.Expect<ParsedPackageManifest>().Manifest;
		var readme = BundledPackages.Readme(descriptor, manifest);

		await Assert.That(readme).StartsWith($"# wiki-reader {manifest.Version}");
		await Assert.That(readme).Contains("Shipped with this server and not installed automatically.");
	}

	/// <summary>A written README lists the objects a package creates, not the ones it only attaches attributes to.</summary>
	[Test]
	public async Task DescribedReadme_ListsCreatedObjectsOnly()
	{
		var descriptor = BundledPackages.Find("scene").Expect<BundledPackages.Descriptor>();
		var manifest = new PackageManifestService().ParseManifest(BundledPackages.ManifestYaml("scene"))
			.Expect<ParsedPackageManifest>().Manifest;
		await Assert.That(manifest.Objects.Any(o => o.IsAttach)).IsTrue().Because("scene attaches its help registration");

		var creates = BundledPackages.Readme(descriptor, manifest).Split("## Creates\n")[1].Split('\n');

		await Assert.That(creates).IsEquivalentTo(manifest.Objects.Where(o => !o.IsAttach).Select(o => $"- {o.Name} (`{o.Type}`)"));
	}

	/// <summary>
	/// The README shown is the current revision's: an upgrade replaces it, an upgrade without one clears it, and a
	/// rollback brings back the one that revision was applied with.
	/// </summary>
	[Test, NotInParallel]
	public async Task Readme_FollowsTheCurrentRevision()
	{
		var id = $"readme-{Guid.NewGuid():N}";
		var manifest = new PackageManifestService().ParseManifest($$"""
			package: {{id}}
			version: "1.0"
			objects:
			  - ref: thing
			    type: thing
			    name: Readme Thing
			""").Expect<ParsedPackageManifest>().Manifest;

		Task<Result<PackageApplyResult>> Apply(string? readme, string commit) => Installer.ApplyAsync(manifest,
			new PackageApplyRequest(new PackageApplySource("https://example.invalid/packages", id, commit, null),
				new Dictionary<string, string>(), [], Readme: readme));

		await Assert.That((await Installer.GetReadmeAsync(id)).Value).IsTypeOf<NotFound>().Because("not installed yet");

		(await Apply("# First\n", "c1")).Expect<PackageApplyResult>();
		await Assert.That((await Installer.GetReadmeAsync(id)).Expect<string>()).IsEqualTo("# First\n");

		(await Apply("# Second\n", "c2")).Expect<PackageApplyResult>();
		await Assert.That((await Installer.GetReadmeAsync(id)).Expect<string>()).IsEqualTo("# Second\n");

		(await Apply(null, "c3")).Expect<PackageApplyResult>();
		await Assert.That((await Installer.GetReadmeAsync(id)).Value).IsTypeOf<NotFound>();

		(await Installer.RollbackAsync(id, 1)).Expect<PackageRollbackResult>();
		await Assert.That((await Installer.GetReadmeAsync(id)).Expect<string>()).IsEqualTo("# First\n");

		(await Installer.UninstallAsync(id, force: true)).Expect<Success>();
		await Assert.That((await Installer.GetReadmeAsync(id)).Value).IsTypeOf<NotFound>();
	}

	private static string FindRepoRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SharpMUSH.sln")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new DirectoryNotFoundException("SharpMUSH.sln not found above the test output.");
	}
}
