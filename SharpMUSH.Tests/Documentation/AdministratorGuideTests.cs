using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Repository-owned administrator guides ship with the test output and keep their relative links usable.
/// </summary>
public partial class AdministratorGuideTests
{
	private static readonly string RepositoryDocuments = Path.Combine(
		AppContext.BaseDirectory, "Documentation", "Repository");

	public static IEnumerable<string> Guides()
	{
		yield return "docs/guides/pennmush-migration.md";
		yield return "docs/guides/operator-handbook.md";
		yield return "docs/design/deployment-architecture.md";
	}

	[Test]
	[MethodDataSource(nameof(Guides))]
	public async Task AdministratorGuideShipsWithTheRepository(string relativePath)
	{
		await Assert.That(File.Exists(Resolve(relativePath))).IsTrue()
			.Because($"{relativePath} is part of the maintained administrator documentation");
	}

	[Test]
	[MethodDataSource(nameof(Guides))]
	public async Task AdministratorGuideRelativeLinksResolve(string relativePath)
	{
		var path = Resolve(relativePath);
		if (!File.Exists(path))
		{
			return;
		}

		var markdown = await File.ReadAllTextAsync(path);
		foreach (Match match in MarkdownLink().Matches(markdown))
		{
			var destination = match.Groups[1].Value.Trim();
			if (destination.Length == 0 || destination[0] == '#'
				|| Uri.TryCreate(destination, UriKind.Absolute, out _))
			{
				continue;
			}

			var withoutFragment = destination.Split('#', 2)[0];
			var decoded = Uri.UnescapeDataString(withoutFragment);
			var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, decoded));
			await Assert.That(File.Exists(target) || Directory.Exists(target)).IsTrue()
				.Because($"{relativePath} links to {destination}");
		}
	}

	private static string Resolve(string relativePath)
		=> Path.Combine(RepositoryDocuments, relativePath.Replace('/', Path.DirectorySeparatorChar));

	[GeneratedRegex("""(?<!!)\[[^\]]+\]\(([^)\s]+)(?:\s+['"][^)]*['"])?\)""")]
	private static partial Regex MarkdownLink();
}
