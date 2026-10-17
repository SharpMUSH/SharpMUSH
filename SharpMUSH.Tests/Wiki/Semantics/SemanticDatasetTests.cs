using SharpMUSH.Tools.WikiSemantics;
namespace SharpMUSH.Tests.Wiki.Semantics;

public class SemanticDatasetTests
{
	[Test]
	public async Task RefusesMissingAndUnverifiedCorpus()
	{
		await Assert.That(PinnedDataset.Load("this-directory-does-not-exist") is DatasetFailure).IsTrue();
		await Assert.That(PinnedDataset.Load(AppContext.BaseDirectory) is DatasetFailure).IsTrue();
	}
	[Test]
	public async Task RefusesTraversalAndAbsolutePaths()
	{
		foreach (var path in new[] { "../outside.md", "/etc/passwd", "wiki/../../outside.md", "wiki\\outside.md" })
			await Assert.That(PinnedDataset.SafePath(AppContext.BaseDirectory, path) is DatasetFailure).IsTrue();
	}

	[Test]
	public async Task RejectsMalformedCollisionAndUnsupportedBodyManifests()
	{
		foreach (var yaml in new[]
		{
			"[broken yaml",
			"format: 1.4\npackage: sample\nversion: 1.0.0\nwiki:\n  - page: main:same\n    title: First\n    file: first.md\n  - page: main:same\n    title: Second\n    file: second.md\n",
			"format: 1.4\npackage: sample\nversion: 1.0.0\nwiki:\n  - page: main:one\n    title: One\n    file: first.html\n"
		})
			await Assert.That(PinnedDataset.ReadManifest(yaml) is DatasetFailure).IsTrue();
	}
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task TamperedFirstLockedFileCannotMasqueradeAsPinnedCorpus(bool configuredScratch)
	{
		var root = ScratchPath("semantic-tamper-", configuredScratch ? Environment.GetEnvironmentVariable("TMPDIR") : null);
		Directory.CreateDirectory(root);
		try
		{
			File.WriteAllText(Path.Combine(root, "NOTICE.md"), new string('x', 2774));
			await Assert.That(PinnedDataset.Load(root) is DatasetFailure failure && failure.Detail == "Source content mismatch: NOTICE.md").IsTrue();
		}
		finally { Directory.Delete(root, true); }
	}

	[Test]
	public async Task DryRunRefusesExistingOutputBeforeSourceRead()
	{
		await Assert.That(DryRunReport.Run("missing-source", AppContext.BaseDirectory) is DatasetFailure failure && failure.Detail == "Output already exists.").IsTrue();
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task DryRunRefusesCaseAliasedSourceOutputBeforeReadingOrWritingSource(bool configuredScratch)
	{
		var scratch = ScratchPath("semantic-output-case-", configuredScratch ? Environment.GetEnvironmentVariable("TMPDIR") : null);
		var source = Path.Combine(scratch, "Corpus");
		Directory.CreateDirectory(source);
		File.WriteAllText(Path.Combine(source, "sentinel"), "immutable source");
		try
		{
			foreach (var output in new[]
			{
				Path.Combine(scratch, "corpus", "report"),
				Path.Combine(scratch, "corpus", "nested", "report"),
				Path.Combine(scratch, "corpus", "nested", "..", "report") + Path.DirectorySeparatorChar
			})
				await Assert.That(DryRunReport.Run(source + Path.DirectorySeparatorChar, output) is DatasetFailure failure && failure.Detail == "Output must be outside the immutable source directory.").IsTrue();
			await Assert.That(File.ReadAllText(Path.Combine(source, "sentinel"))).IsEqualTo("immutable source");
			await Assert.That(Directory.EnumerateFileSystemEntries(source).Count()).IsEqualTo(1);
			await Assert.That(Directory.EnumerateFileSystemEntries(scratch).Count()).IsEqualTo(1);
		}
		finally { Directory.Delete(scratch, true); }
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task DryRunAllowsOrdinarySiblingAndSharedPrefixPastContainmentPreflight(bool configuredScratch)
	{
		var scratch = ScratchPath("semantic-output-sibling-", configuredScratch ? Environment.GetEnvironmentVariable("TMPDIR") : null);
		var source = Path.Combine(scratch, "Corpus");
		Directory.CreateDirectory(source);
		try
		{
			foreach (var output in new[] { Path.Combine(scratch, "report"), Path.Combine(scratch, "CorpusReports", "report") })
				await Assert.That(DryRunReport.Run(source, output) is DatasetFailure failure && failure.Detail == "Missing or unsafe source file: NOTICE.md").IsTrue();
			await Assert.That(Directory.EnumerateFileSystemEntries(source).Any()).IsFalse();
			await Assert.That(Directory.EnumerateFileSystemEntries(scratch).Count()).IsEqualTo(1);
		}
		finally { Directory.Delete(scratch, true); }
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task RefusesOutputSymlinkAncestorBeforeReadingSource(bool configuredScratch)
	{
		var scratch = ScratchPath("semantic-output-link-", configuredScratch ? Environment.GetEnvironmentVariable("TMPDIR") : null);
		var source = Path.Combine(scratch, "source");
		var alias = Path.Combine(scratch, "alias");
		Directory.CreateDirectory(source);
		Directory.CreateSymbolicLink(alias, source);
		try
		{
			var output = Path.Combine(alias, "new-output");
			await Assert.That(DryRunReport.Run(source, output) is DatasetFailure failure && failure.Detail == "Symbolic output ancestors are unsupported.").IsTrue();
			await Assert.That(Directory.EnumerateFileSystemEntries(source).Any()).IsFalse();
		}
		finally { Directory.Delete(scratch, true); }
	}

	private static string ScratchPath(string prefix, string? configuredRoot)
	{
		// CI need not set TMPDIR; keep fallback scratch beside test outputs, never in the OS temporary directory.
		var root = configuredRoot ?? Path.Combine(AppContext.BaseDirectory, "work");
		return Path.Combine(root, prefix + Guid.NewGuid().ToString("N"));
	}

}
