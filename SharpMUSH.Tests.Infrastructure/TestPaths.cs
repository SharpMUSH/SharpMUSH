namespace SharpMUSH.Tests;

/// <summary>
/// Locations inside the checkout that tests read from. Test binaries run out of
/// <c>bin/&lt;config&gt;/&lt;tfm&gt;</c>, so anything that has to read a source-controlled file has to walk
/// back up to the repository root first; every test that did so hand-rolled the same loop.
/// </summary>
public static class TestPaths
{
	private static readonly Lazy<string> LazyRepositoryRoot = new(FindRepositoryRoot);

	/// <summary>The directory holding <c>SharpMUSH.sln</c>, found by walking up from the test binaries.</summary>
	public static string RepositoryRoot => LazyRepositoryRoot.Value;

	/// <summary>The shipped helpfile tree — the same directory the server indexes at startup.</summary>
	public static DirectoryInfo Helpfiles =>
		new(Path.Combine(RepositoryRoot, "SharpMUSH.Documentation", "Helpfiles", "SharpMUSH"));

	/// <summary>
	/// Every <c>.cs</c> file under a shipped project. Test projects are excluded, so a caller asking
	/// "does production code do X" cannot be answered by its own assertions.
	/// </summary>
	public static IEnumerable<string> ProductionSourceFiles() =>
		SourceFilesUnder(d => !Path.GetFileName(d).Contains("Tests", StringComparison.Ordinal));

	/// <summary>Every <c>.cs</c> file under a test project — the corpus a coverage inventory searches.</summary>
	public static IEnumerable<string> TestSourceFiles() =>
		SourceFilesUnder(d => Path.GetFileName(d).Contains("Tests", StringComparison.Ordinal));

	private static IEnumerable<string> SourceFilesUnder(Func<string, bool> projectFilter) =>
		Directory.EnumerateDirectories(RepositoryRoot, "SharpMUSH.*")
			.Where(projectFilter)
			.SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SharpMUSH.sln")))
			directory = directory.Parent;

		return directory?.FullName
			?? throw new InvalidOperationException("Could not locate the repository root (no SharpMUSH.sln above the test binaries).");
	}
}
