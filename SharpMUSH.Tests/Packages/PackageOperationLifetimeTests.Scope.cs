using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

public partial class PackageOperationLifetimeTests
{
	private sealed class FailingFirstCapture : IPackageOperationScope
	{
		private int _captures;
		public object? Capture() => Interlocked.Increment(ref _captures) == 1 ? throw new InvalidDataException("capture failed") : null;
		public IDisposable Enter(object? captured) => Substitute.For<IDisposable>();
	}

	[Test]
	public async Task AFailingScopeCaptureFailsItsOperationAndStillAdmitsTheNext()
	{
		using var deadline = new CancellationTokenSource(TestDeadline);
		var scheduler = new ControlledScheduler(observed: true);
		var backups = Substitute.For<IWorldBackupService>();
		backups.IsSupported.Returns(false);
		var runner = new PackageOperationRunner(scheduler.Scheduler, new PackageOperationGate(), backups,
			Substitute.For<IPackageLifecycleRunner>(), NullLogger<PackageOperationRunner>.Instance, new FailingFirstCapture());

		var failed = await runner.StartAsync("apply", _ => Task.FromResult(false)).WaitAsync(deadline.Token);
		await Assert.That(async () => await failed.Completion.WaitAsync(deadline.Token)).Throws<InvalidDataException>();
		await Assert.That((await failed.Observation.WaitAsync(deadline.Token)).Coordinator).IsEqualTo(PackageTaskObservation.Faulted);

		var next = await runner.StartAsync("apply", _ => Task.FromResult(true)).WaitAsync(deadline.Token);
		await (await scheduler.TakeAsync(deadline.Token)).RunAsync(CancellationToken.None).WaitAsync(deadline.Token);
		await Assert.That((await next.Completion.WaitAsync(deadline.Token)).Expect<PackageOperationRan<bool>>().Result).IsTrue();
	}
}
