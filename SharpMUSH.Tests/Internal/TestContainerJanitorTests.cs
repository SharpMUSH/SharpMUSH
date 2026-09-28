using System.Diagnostics;

namespace SharpMUSH.Tests.Internal;

// Only the ownership decision is tested here; no container runtime is contacted.
public class TestContainerJanitorTests
{
	[Test]
	public async Task OwnContainersAreNotOrphaned()
	{
		await Assert.That(TestContainerJanitor.IsOrphaned(Labels(TestContainerJanitor.OwnerLabels))).IsFalse();
	}

	[Test]
	public async Task ContainersOfAnExitedProcessAreOrphaned()
	{
		using var process = Process.Start(new ProcessStartInfo("sleep", "60") { UseShellExecute = false })!;
		var labels = Labels(TestContainerJanitor.OwnerLabels);
		labels[TestContainerJanitor.OwnerPidLabel] = process.Id.ToString();
		labels[TestContainerJanitor.OwnerStartLabel] =
			new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds().ToString();
		process.Kill();
		await process.WaitForExitAsync();

		await Assert.That(TestContainerJanitor.IsOrphaned(labels)).IsTrue();
	}

	[Test]
	public async Task ReusedProcessIdWithAnotherStartTimeIsOrphaned()
	{
		var labels = Labels(TestContainerJanitor.OwnerLabels);
		labels[TestContainerJanitor.OwnerStartLabel] =
			(long.Parse(labels[TestContainerJanitor.OwnerStartLabel]) - 3600).ToString();

		await Assert.That(TestContainerJanitor.IsOrphaned(labels)).IsTrue();
	}

	[Test]
	public async Task ContainersOwnedByAnotherMachineAreLeftAlone()
	{
		var labels = Labels(TestContainerJanitor.OwnerLabels);
		labels[TestContainerJanitor.OwnerHostLabel] = "some-other-host";
		labels[TestContainerJanitor.OwnerPidLabel] = int.MaxValue.ToString();

		await Assert.That(TestContainerJanitor.IsOrphaned(labels)).IsFalse();
	}

	[Test]
	public async Task UnlabeledOrMalformedContainersAreLeftAlone()
	{
		var malformed = Labels(TestContainerJanitor.OwnerLabels);
		malformed[TestContainerJanitor.OwnerPidLabel] = "not-a-pid";

		await Assert.That(TestContainerJanitor.IsOrphaned(null)).IsFalse();
		await Assert.That(TestContainerJanitor.IsOrphaned(new Dictionary<string, string>())).IsFalse();
		await Assert.That(TestContainerJanitor.IsOrphaned(malformed)).IsFalse();
	}

	private static Dictionary<string, string> Labels(IReadOnlyDictionary<string, string> source) =>
		source.ToDictionary(pair => pair.Key, pair => pair.Value);
}
