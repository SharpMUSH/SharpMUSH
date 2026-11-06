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

	/// <summary>
	/// A checkout of the official package library (SharpMUSH.Packages), for tests of packages that live there:
	/// <c>SHARPMUSH_PACKAGES_PATH</c>, else a <c>SharpMUSH.Packages</c> or <c>sharpmush.packages</c> folder beside this
	/// repository. CI checks the library out and sets the variable.
	/// </summary>
	public static string PackageLibrary =>
		Environment.GetEnvironmentVariable("SHARPMUSH_PACKAGES_PATH") is { Length: > 0 } configured
			? File.Exists(Path.Combine(configured, "index.yaml"))
				? configured
				: throw new DirectoryNotFoundException($"SHARPMUSH_PACKAGES_PATH is {configured}, which holds no index.yaml.")
			: new[] { "SharpMUSH.Packages", "sharpmush.packages" }
				.Select(name => Path.Combine(Path.GetDirectoryName(RepositoryRoot)!, name))
				.FirstOrDefault(dir => File.Exists(Path.Combine(dir, "index.yaml")))
			?? throw new DirectoryNotFoundException(
				"No package library checkout: clone SharpMUSH/SharpMUSH.Packages beside this repository or set SHARPMUSH_PACKAGES_PATH.");

	/// <summary>The shipped helpfile tree — the same directory the server indexes at startup.</summary>
	public static DirectoryInfo Helpfiles =>
		new(Path.Combine(RepositoryRoot, "SharpMUSH.Documentation", "Helpfiles", "SharpMUSH"));

	/// <summary>A PennMUSH config file that sets nothing, so every option reads back at its default.</summary>
	public static string EmptyConfigFile =>
		Path.Combine(RepositoryRoot, "SharpMUSH.Tests", "Configuration", "Testfile", "empty.cnf");

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
