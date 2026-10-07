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

	[Test]
	public async Task MigrationGuideDefinesSupportedInputsAndRollbackBoundary()
	{
		var text = Read("docs/guides/pennmush-migration.md").ToLowerInvariant();
		foreach (var required in new[]
		{
			"pennmush flatfile", "mush.cnf", "maildb", "chatdb", "rhostmush", "tinymux",
			"no direct supported importer", "one-way", "rehearsal", "validation", "## 4. cut over",
			"<world>.previous", "point-in-time world copy", "identity and passwords", "objects and dbrefs",
			"attributes and locks", "flags and powers", "channels and mail", "configuration",
			"handlers and packages", "softcode", "connections", "portal", "integrations"
		})
		{
			await Assert.That(text).Contains(required);
		}
	}

	[Test]
	public async Task OperatorGuideDistinguishesStateAndRecoveryKinds()
	{
		var text = Read("docs/guides/operator-handbook.md").ToLowerInvariant();
		foreach (var required in new[]
		{
			"lightning/lmdb world", "wiki assets", "nats jetstream", "plugin assemblies",
			"restore drill", "deployment rollback", "data restore", "@storage", "readiness",
			"prometheus", "restic", "first claim", "logs", "upgrade", "incident"
		})
		{
			await Assert.That(text).Contains(required);
		}
	}

	[Test]
	public async Task ArchitectureNamesRuntimeAndObservationBoundaries()
	{
		var text = Read("docs/design/deployment-architecture.md");
		foreach (var required in new[]
		{
			"SharpMUSH.Server", "SharpMUSH.SocketServer", "SharpMUSH.RenderingWorker", "NATS",
			"Lightning/LMDB", "Wiki assets", "Restic", "Off-host storage", "Prometheus",
			"health/readiness"
		})
		{
			await Assert.That(text).Contains(required);
		}
	}

	[Test]
	public async Task MaintainedRepositoryGuidesDoNotClaimDotNet10()
	{
		foreach (var relativePath in new[]
		{
			"docs/guides/pennmush-migration.md", "docs/guides/operator-handbook.md",
			"docs/design/deployment-architecture.md", "docs/guides/writing-a-plugin.md"
		})
		{
			var stale = Regex.IsMatch(Read(relativePath), @"(?:\.NET\s+10\b|net10\.0)", RegexOptions.IgnoreCase);
			await Assert.That(stale).IsFalse().Because($"{relativePath} must follow the SDK pinned in global.json");
		}
	}

	private static string Resolve(string relativePath)
		=> Path.Combine(RepositoryDocuments, relativePath.Replace('/', Path.DirectorySeparatorChar));

	private static string Read(string relativePath) => File.ReadAllText(Resolve(relativePath));

	[GeneratedRegex("""(?<!!)\[[^\]]+\]\(([^)\s]+)(?:\s+['"][^)]*['"])?\)""")]
	private static partial Regex MarkdownLink();
}
