namespace SharpMUSH.Tests.PackageLibrary;

/// <summary>
/// The behaviour tests the package library keeps for its own packages, <c>tests/&lt;package&gt;/*.yaml</c> in a
/// SharpMUSH.Packages checkout (<see cref="TestPaths.PackageLibrary"/>), one test per file. The library's CI runs
/// every test in this namespace against the engine; the engine's CI runs them against the library's main. Not in
/// parallel: a package's commands and settings are the game's, one for every test.
/// </summary>
[NotInParallel]
public class PackageLibraryScenarioTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	public static IEnumerable<Func<PackageScenarioFile>> Scenarios() =>
		PackageScenarioFile.Discover().Select(file => (Func<PackageScenarioFile>)(() => file));

	[Test]
	[MethodDataSource(nameof(Scenarios))]
	public async Task Scenario(PackageScenarioFile scenario) =>
		await new PackageScenarioRunner(WebAppFactoryArg, scenario).RunAsync();
}
