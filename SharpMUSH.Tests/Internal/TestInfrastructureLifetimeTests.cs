namespace SharpMUSH.Tests.Internal;

public class TestInfrastructureLifetimeTests
{
	[Test]
	public async Task GeneratedNamesRemainDistinctUnderConcurrentCreation()
	{
		var names = new string[40_000];
		Parallel.For(0, names.Length, i => names[i] = TestIsolationHelpers.GenerateUniqueName("concurrent"));
		await Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()).IsEqualTo(names.Length);
		// Existing command-level callers need compact names, not a full GUID plus sequence.
		await Assert.That(names.Max(name => name.Length)).IsLessThanOrEqualTo(32);
	}
}
