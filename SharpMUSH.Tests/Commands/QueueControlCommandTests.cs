using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class QueueControlCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task ExistingPidRetimingAdjustsAFrozenSemaphoreWithoutResumingIt()
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var queue = Factory.Services.GetRequiredService<ITaskScheduler>();
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connections, "QueueRetime");
		var objid = (await mediator.Send(new GetObjectNodeQuery(target))).Known().Object().DBRef;
		var job = await queue.WriteCommandList(MarkupText.Plain("think later"), ParserState.Empty with { Executor = objid },
			new SharpMUSH.Library.Models.DbRefAttribute(objid, ["SEMAPHORE"]), 1, TimeSpan.FromHours(1));
		try
		{
			await queue.PausePending(job.Pid!.Value, "hold");
			var before = queue.GetQueueEntry(job.Pid.Value)!.RemainingDelay!.Value;
			var result = await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@wait/pid {job.Pid}=+30"));
			await Assert.That(result.Message!.ToPlainText()).IsEqualTo(job.Pid.Value.ToString());
			await Assert.That(queue.GetQueueEntry(job.Pid.Value)!.RemainingDelay).IsEqualTo(before + TimeSpan.FromSeconds(30));
			await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@wait/pid {job.Pid}=-20"));
			await Assert.That(queue.GetQueueEntry(job.Pid.Value)!.RemainingDelay).IsEqualTo(before + TimeSpan.FromSeconds(10));
			await Assert.That(queue.GetQueueEntry(job.Pid.Value)!.State).IsEqualTo(QueueEntryState.Paused);
		}
		finally { if (job.Pid is { } pid) await queue.HaltByPid(pid); }
	}

	[Test]
	public async Task GameCommandsPauseListAndResumeWithoutExposingCommandText()
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var queue = Factory.Services.GetRequiredService<ITaskScheduler>();
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connections, "QueuePause");
		var objid = (await mediator.Send(new GetObjectNodeQuery(target))).Known().Object().DBRef;
		var job = await queue.WriteCommandList(MarkupText.Plain("think QueuePrivatePayload"), ParserState.Empty with { Executor = objid }, TimeSpan.FromHours(1));
		try
		{
			await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@queue/pause {job.Pid}=Inspect timer"));
			await Assert.That(queue.GetQueueEntry(job.Pid!.Value)!.State).IsEqualTo(QueueEntryState.Paused);
			var before = Factory.Notifications.CountFor(Factory.ExecutorDBRef);
			await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@queue/list {job.Pid}"));
			var output = string.Join("\n", Factory.Notifications.For(Factory.ExecutorDBRef).Skip(before));
			await Assert.That(output.Contains("Paused")).IsTrue();
			await Assert.That(output.Contains("Inspect timer")).IsTrue();
			await Assert.That(output.Contains("QueuePrivatePayload")).IsFalse();
			await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@queue/resume {job.Pid}"));
			await Assert.That(queue.GetQueueEntry(job.Pid.Value)!.State).IsEqualTo(QueueEntryState.Pending);
		}
		finally { if (job.Pid is { } pid) await queue.HaltByPid(pid); }
	}
}
