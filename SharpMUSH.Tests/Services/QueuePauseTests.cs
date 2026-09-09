using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class QueuePauseTests
{
	[Test]
	public async Task PauseRetainsPendingPidAndQuota()
	{
		await using var queue = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			Substitute.For<ISchedulerFactory>(), Substitute.For<IAttributeService>(), Substitute.For<IMediator>(), NullLogger<Scheduler>.Instance);
		var admitted = await queue.WriteCommandList(MarkupText.Plain("think retained"), ParserState.Empty, TimeSpan.FromHours(1));
		await Assert.That(admitted.Accepted).IsTrue();
		await Assert.That(await queue.PausePending(admitted.Pid!.Value, "Inspect timed system")).IsEqualTo(QueueControlResult.Applied);
		var paused = queue.GetQueueEntries().Single();
		await Assert.That(paused.Pid).IsEqualTo(admitted.Pid.Value);
		await Assert.That(paused.State).IsEqualTo(QueueEntryState.Paused);
		await Assert.That(paused.PauseReason).IsEqualTo("Inspect timed system");
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
	}
}
