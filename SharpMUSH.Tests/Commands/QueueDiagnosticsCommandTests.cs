using Mediator;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class QueueDiagnosticsCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task MetadataExposesProfileArgumentsAndHistorySwitch()
	{
		var profile = typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod("QueueProfile")!
			.GetCustomAttribute<SharpMUSH.Library.Attributes.SharpCommandAttribute>()!;
		await Assert.That(profile.Name).IsEqualTo("@PROFILE");
		await Assert.That(profile.ParameterNames.SequenceEqual(["seconds"])).IsTrue();
		await Assert.That(profile.Switches!.ToHashSet().SetEquals(["START", "STOP"])).IsTrue();
		var ps = typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod("ProcessStatus")!
			.GetCustomAttribute<SharpMUSH.Library.Attributes.SharpCommandAttribute>()!;
		await Assert.That(ps.Switches!.Contains("HISTORY")).IsTrue();
		await Assert.That(ps.ParameterNames.SequenceEqual(["player, pid, or history-limit"])).IsTrue();
	}

	[Test]
	public async Task GameAndServiceShareVisibleHistoryAndProfile()
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var queue = Factory.Services.GetRequiredService<ITaskScheduler>();
		var recorder = Factory.Services.GetRequiredService<QueueDiagnosticsRecorder>();
		var service = Factory.Services.GetRequiredService<IQueueDiagnosticsService>();
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var full = (await mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Known().Object().DBRef;
		var actor = await Factory.Services.GetRequiredService<IAdministrativeCapabilityService>().GetGameActorAsync(full);
		await Assert.That(actor).IsNotNull();
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain("@profile/start 60"));
		try
		{
			var queued = await queue.WriteCommandList(MarkupText.Plain("think add(2,3)"), ParserState.Empty with { Executor = full, Enactor = full, Caller = full, CurrentEvaluation = new DBAttribute(full, "DIAGNOSTIC_SOURCE") });
			await Assert.That(queued.Accepted).IsTrue();
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			while (!recorder.Recent().Any(row => row.Pid == queued.Pid)) await Task.Delay(10, timeout.Token);
			await service.CollectProfilesAsync();
			var report = await service.InspectAsync(actor!);
			await Assert.That(report.IsT0).IsTrue();
			await Assert.That(report.AsT0.Recent.Any(row => row.Pid == queued.Pid && row.InvocationCount >= 2)).IsTrue();
			await Assert.That(report.AsT0.Profile!.Rows.Any(row => row.Name == "ADD")).IsTrue();
			var before = Factory.Notifications.CountFor(Factory.ExecutorDBRef);
			await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain("@ps/history 10"));
			var historyOutput = string.Join('\n', Factory.Notifications.For(Factory.ExecutorDBRef).Skip(before));
			await Assert.That(historyOutput.Contains("/DIAGNOSTIC_SOURCE", StringComparison.Ordinal)).IsTrue();
			await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain("@profile"));
			var output = string.Join('\n', Factory.Notifications.For(Factory.ExecutorDBRef).Skip(before));
			await Assert.That(output.Contains(queued.Pid!.Value.ToString(), StringComparison.Ordinal)).IsTrue();
			await Assert.That(output.Contains("ADD", StringComparison.Ordinal)).IsTrue();
			await Assert.That(output.Contains("think add(2,3)", StringComparison.Ordinal)).IsFalse();
		}
		finally { await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain("@profile/stop")); }
	}

	[Test]
	[Arguments("@profile/start 301")]
	[Arguments("@profile/start 1.5")]
	[Arguments("@profile/stop unexpected")]
	[Arguments("@ps/history 101")]
	public async Task InvalidArgumentsAreRejected(string command)
	{
		var result = await Factory.CommandParser.CommandParse(1, Factory.Services.GetRequiredService<IConnectionService>(), MarkupText.Plain(command));
		await Assert.That(result.Message!.ToPlainText().StartsWith("#-1", StringComparison.Ordinal)).IsTrue();
	}
}
