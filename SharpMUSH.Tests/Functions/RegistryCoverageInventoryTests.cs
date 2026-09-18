namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Which registered names the suite actually calls, regenerated rather than transcribed.
///
/// <para>#974 carried a hand-written list of 44 never-invoked functions. By the time anyone came back
/// to it the list was wrong in both directions — several of the named functions had since gained
/// tests, and the list said nothing about the ones that had been added in the meantime. A list in an
/// issue decays; this computes the same thing from the registry and the test sources on every run, so
/// the only way a function can ship untested is to delete a test, which shows up as a deletion in a
/// diff rather than as an exception nobody reads.</para>
///
/// <para>The measure is deliberately crude: does any test source contain <c>name(</c>. That counts a
/// mention inside an unrelated assertion string as coverage, so it is a floor rather than a
/// guarantee. A floor at zero is still the difference between "nobody has ever run this" and "someone
/// has".</para>
/// </summary>
public class RegistryCoverageInventoryTests
{
	private static readonly Lazy<string> TestCorpus = new(() =>
		string.Join('\n', TestPaths.TestSourceFiles()
			.Concat(Directory.EnumerateFiles(
				Path.Combine(TestPaths.RepositoryRoot, "SharpMUSH.Tests"), "*.t", SearchOption.AllDirectories))
			.Select(File.ReadAllText))
		.ToLowerInvariant());

	[Test]
	public async Task EveryRegisteredFunctionIsCalledBySomeTest()
	{
		var corpus = TestCorpus.Value;

		var never = RegistryInventory.Functions
			.Select(f => f.Name)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(name => !corpus.Contains($"{name.ToLowerInvariant()}(", StringComparison.Ordinal))
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(never).IsEmpty();
	}
}
