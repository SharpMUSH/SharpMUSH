using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Parses every manifest under examples/packages/ so the example packages
/// (and the README they document) can never drift from the parser.
/// </summary>
public class ExamplePackageTests
{
	private readonly PackageManifestService _service = new();

	private static string ExamplesRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, "examples", "packages");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			dir = dir.Parent!;
		}

		throw new DirectoryNotFoundException("Could not locate examples/packages above the test directory.");
	}

	[Test]
	public async Task Index_IsValid_AndListsExistingPackages()
	{
		var root = ExamplesRoot();
		var result = _service.ParseIndex(await File.ReadAllTextAsync(Path.Combine(root, "index.yaml")));

		await Assert.That(result.IsT0).IsTrue();
		var index = result.AsT0;
		await Assert.That(index.Packages.Count).IsGreaterThan(0);

		foreach (var entry in index.Packages)
		{
			var manifestPath = Path.Combine(root, entry.Path, "package.yaml");
			await Assert.That(File.Exists(manifestPath)).IsTrue();
		}
	}

	[Test]
	public async Task EveryExampleManifest_ParsesWithoutErrorsOrWarnings()
	{
		var root = ExamplesRoot();
		var manifests = Directory.GetFiles(root, "package.yaml", SearchOption.AllDirectories);
		await Assert.That(manifests.Length).IsGreaterThan(0);

		foreach (var path in manifests)
		{
			var result = _service.ParseManifest(await File.ReadAllTextAsync(path));

			if (result.IsT1)
			{
				Assert.Fail($"{path} failed to parse:\n{string.Join("\n", result.AsT1.Issues)}");
			}

			var warnings = result.AsT0.Warnings;
			if (warnings.Count > 0)
			{
				Assert.Fail($"{path} parsed with warnings:\n{string.Join("\n", warnings)}");
			}
		}
	}

	[Test]
	public async Task EveryExampleDirectoryInIndex_AndEveryManifestInIndex()
	{
		var root = ExamplesRoot();
		var indexResult = _service.ParseIndex(await File.ReadAllTextAsync(Path.Combine(root, "index.yaml")));
		var indexedPaths = indexResult.AsT0.Packages
			.Select(p => p.Path.TrimEnd('/'))
			.ToHashSet(StringComparer.Ordinal);

		var manifestDirs = Directory.GetFiles(root, "package.yaml", SearchOption.AllDirectories)
			.Select(p => Path.GetRelativePath(root, Path.GetDirectoryName(p)!))
			.ToHashSet(StringComparer.Ordinal);

		await Assert.That(manifestDirs.SetEquals(indexedPaths)).IsTrue();
	}

	/// <summary>
	/// The index carries id, version and description so a browse can list a repo without parsing
	/// every manifest (decision 20.17) — which only helps if the id and version agree with the
	/// manifest they summarize. Nothing checked that before, and both scene and profile-handler had
	/// drifted several releases behind their own manifests while the paths test stayed green.
	///
	/// <para>The description is deliberately NOT compared: the index blurb is a one-line browse
	/// summary and the manifest's is the full sentence, so they read differently on purpose. What
	/// matters is that a blurb is there at all.</para>
	/// </summary>
	[Test]
	public async Task EveryIndexEntry_AgreesWithTheManifestItSummarizes()
	{
		var root = ExamplesRoot();
		var index = _service.ParseIndex(await File.ReadAllTextAsync(Path.Combine(root, "index.yaml"))).AsT0;

		foreach (var entry in index.Packages)
		{
			// An index path is relative to the repo it indexes; a rooted one would make Path.Combine
			// silently discard the root and read some other file entirely.
			await Assert.That(Path.IsPathRooted(entry.Path)).IsFalse()
				.Because($"{entry.Path} must be relative to the package root");

			var path = Path.Combine(root, entry.Path, "package.yaml");
			var manifest = _service.ParseManifest(await File.ReadAllTextAsync(path)).AsT0.Manifest;

			await Assert.That(entry.PackageId).IsEqualTo(manifest.Name)
				.Because($"{entry.Path} is indexed under the wrong id");
			await Assert.That(entry.Version?.ToString()).IsEqualTo(manifest.Version.ToString())
				.Because($"{entry.Path} is indexed at a stale version");
			await Assert.That(string.IsNullOrWhiteSpace(entry.Description)).IsFalse()
				.Because($"{entry.Path} has no browse blurb, which is half of what the index is for");
		}
	}
}
