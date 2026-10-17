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
	public async Task TamperedFirstLockedFileCannotMasqueradeAsPinnedCorpus()
	{
		var root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? throw new InvalidOperationException("Disk TMPDIR required."), "semantic-tamper-" + Guid.NewGuid().ToString("N"));
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
	public async Task RefusesOutputSymlinkAncestorBeforeReadingSource()
	{
		var scratch = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? throw new InvalidOperationException("Disk TMPDIR required."), "semantic-output-link-" + Guid.NewGuid().ToString("N"));
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
}
