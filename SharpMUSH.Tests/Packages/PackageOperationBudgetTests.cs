using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>A package operation is not softcode: queue_entry_cpu_time never times its queue entry (#2065).</summary>
public class PackageOperationBudgetTests
{
	[Test]
	public async Task OperationOutlivingTheSoftcodeDeadlineCompletesWithoutAnExecutionLimit()
	{
		var recorder = new QueueDiagnosticsRecorder();
		await using var queue = QueueDiagnosticsSchedulerTests.Create(recorder, milliseconds: 1);
		var runner = new PackageOperationRunner(queue, new PackageOperationGate(), Substitute.For<IWorldBackupService>(),
			Substitute.For<IPackageLifecycleRunner>(), NullLogger<PackageOperationRunner>.Instance);

		var outcome = await runner.RunAsync("apply", async token =>
		{
			// Well past the 1 ms a softcode entry would get.
			await Task.Delay(50, token);
			return true;
		}).WaitAsync(TimeSpan.FromSeconds(10));

		await Assert.That(outcome is PackageOperationRan<bool> { Result: true }).IsTrue();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		while (recorder.Recent().Count < 1) await Task.Delay(5, timeout.Token);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(QueueOutcome.Completed);
	}
}
