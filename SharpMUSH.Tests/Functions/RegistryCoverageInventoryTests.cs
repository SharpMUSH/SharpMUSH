namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Which registered names the suite mentions, regenerated rather than transcribed.
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
/// guarantee. What was actually run is measured separately: <see cref="FunctionCoverage.Build"/>
/// compares the parser's dispatch telemetry with this scan, and the test job's telemetry summary lists
/// every function that is mentioned but was never dispatched. A function with a reason not to be
/// run yet is in <see cref="FunctionDispatchCoverageGate.KnownGaps"/>, which this check shares.</para>
/// </summary>
public class RegistryCoverageInventoryTests
{
	[Test]
	public async Task EveryRegisteredFunctionIsCalledBySomeTest()
	{
		var unexplained = FunctionCoverage.NeverMentioned()
			.Where(name => !FunctionDispatchCoverageGate.KnownGaps.ContainsKey(name))
			.ToList();

		await Assert.That(unexplained).IsEmpty();
	}
}
