using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class SemaphoreInspectionCancellationTests
{
	[Test]
	[Arguments("object")]
	[Arguments("pid")]
	[Arguments("attribute")]
	public async Task SemaphoreInspectionReadsLedgerWithoutQuartz(string selector)
	{
		var scheduler = Substitute.For<IScheduler>();
		var factory = Substitute.For<ISchedulerFactory>();
		factory.GetScheduler().Returns(scheduler);
		await using var queue = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			factory, Substitute.For<IAttributeService>(), QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance);
		var target = new DbRefAttribute(new DBRef(8, 1), ["SEMAPHORE"]);
		var admitted = await queue.AdmitCommandList(MarkupText.Plain("think retained"),
			ParserState.Empty with { Executor = new DBRef(7, 1) }, target, 1);
		await Assert.That(admitted.Accepted).IsTrue();
		scheduler.ClearReceivedCalls();
		var entries = selector switch
		{
			"object" => queue.GetSemaphoreTasks(target.DbRef),
			"pid" => queue.GetSemaphoreTasks(admitted.Pid!.Value),
			_ => queue.GetSemaphoreTasks(target)
		};
		await Assert.That(await entries.CountAsync()).IsEqualTo(1);
		await scheduler.DidNotReceiveWithAnyArgs().GetTriggerKeys(default!, default);
		await scheduler.DidNotReceiveWithAnyArgs().GetTrigger(default!, default);
		await scheduler.DidNotReceiveWithAnyArgs().GetJobDetail(default!, default);
	}
}
