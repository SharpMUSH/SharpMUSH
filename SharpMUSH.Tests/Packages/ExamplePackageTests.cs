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
}
