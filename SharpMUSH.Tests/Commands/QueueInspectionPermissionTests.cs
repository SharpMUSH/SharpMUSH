using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class QueueInspectionPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AnotherPlayersQueuedCommandIsPrivate(bool debug)
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var queue = Factory.Services.GetRequiredService<ITaskScheduler>();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, connections, "QueueReader");
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connections, "PrivateQueue");
		var reference = target;
		var job = await queue.WriteCommandList(MarkupText.Plain("think PrivateQueueSecret"),
			ParserState.Empty with { Executor = reference }, new DbRefAttribute(reference, ["SEMAPHORE"]), 1);
		try
		{
			var before = Factory.Notifications.CountFor(mortal.DbRef);
			var command = debug ? $"@ps/debug {job.Pid}" : $"@ps {target}";
			var result = await Factory.CommandParser.CommandParse(mortal.Handle, connections, MarkupText.Plain(command));
			await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
			await Assert.That(Factory.Notifications.For(mortal.DbRef).Skip(before).Any(m => m.Contains("PrivateQueueSecret"))).IsFalse();
		}
		finally { if (job.Pid is { } pid) await queue.HaltByPid(pid); }
	}
}
