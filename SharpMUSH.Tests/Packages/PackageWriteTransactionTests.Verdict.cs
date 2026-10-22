using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A transaction reports exactly how it ended to its hold, once, before it releases it, and owns the
/// work it admits after its commit until it closes.
/// </summary>
public partial class PackageWriteTransactionTests
{
	private async Task<(PackageWriteTransaction Writes, TestPackageWrites.RecordingHold Hold)> RecordedAsync()
	{
		var hold = new TestPackageWrites.RecordingHold();
		return (TestPackageWrites.Issue(WebAppFactoryArg.Services, await PackageManagerAsync(), hold), hold);
	}

	[Test]
	[Arguments(0, PackageWriteVerdict.Committed)]
	[Arguments(1, PackageWriteVerdict.Reverted)]
	[Arguments(2, PackageWriteVerdict.CompensationFailed)]
	[Arguments(3, PackageWriteVerdict.ReleasedWorkFailed)]
	public async Task ATransactionReportsHowItEndedBeforeReleasingItsHold(int ending, PackageWriteVerdict expected)
	{
		var (_, dbref, _) = await SubjectAsync("Verdict Subject", "verdict-" + ending);
		var (writes, hold) = await RecordedAsync();
		await writes.SetAttributeAsync(dbref, ["TX_VALUE"], MarkupText.Plain("changed"), await PackageManagerAsync(), CancellationToken.None);
		switch (ending)
		{
			case 0:
				writes.Commit();
				await writes.DisposeAsync();
				break;
			case 1:
				await writes.DisposeAsync();
				break;
			case 2:
				writes.Track("refusing inverse", () => throw new ArithmeticException("cannot undo"));
				await Assert.That(async () => await writes.DisposeAsync()).Throws<PackageCompensationException>();
				break;
			default:
				writes.Commit();
				using (writes.AdmitReleased()) writes.ReleasedWorkFailed();
				await writes.DisposeAsync();
				break;
		}

		await Assert.That(hold.Verdicts).IsEquivalentTo([expected]);
		await Assert.That(hold.Released).IsTrue();
	}

	[Test]
	public async Task ClosingWaitsForReleasedWorkAndReleasedWorkNeedsACommit()
	{
		var (writes, hold) = await RecordedAsync();
		await Assert.That(() => writes.AdmitReleased()).Throws<InvalidOperationException>();
		writes.Commit();
		var admitted = Signal();
		var release = Signal();
		var work = Task.Run(async () =>
		{
			using var released = writes.AdmitReleased();
			admitted.SetResult();
			await release.Task;
		});
		await admitted.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var dispose = writes.DisposeAsync().AsTask();
		await Task.Delay(100);
		await Assert.That(dispose.IsCompleted).IsFalse();
		await Assert.That(hold.Released).IsFalse();
		release.SetResult();
		await work;
		await dispose;
		await Assert.That(hold.Verdicts).IsEquivalentTo([PackageWriteVerdict.Committed]);
		await Assert.That(() => writes.AdmitReleased()).Throws<InvalidOperationException>();
	}
}
