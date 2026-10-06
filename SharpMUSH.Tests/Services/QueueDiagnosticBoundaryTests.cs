using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class QueueDiagnosticBoundaryTests
{
	private static Scheduler Create(QueueDiagnosticsRecorder recorder, IMediator? mediator = null,
		IMUSHCodeParser? parser = null, INotifyService? notify = null, IConnectionService? connections = null)
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Limit = config.Limit with { QueueEntryCpuTime = 1000 } });
		return new(parser ?? Substitute.For<IMUSHCodeParser>(), connections ?? Substitute.For<IConnectionService>(),
			Substitute.For<ISchedulerFactory>(), Substitute.For<IAttributeService>(), mediator ?? QueueAdmissionTests.TargetMediator(),
			NullLogger<Scheduler>.Instance, options, notify, diagnostics: recorder);
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task ExecutionLimitObservationEndsBeforeExternalNotification(bool returnNormally)
	{
		var recorder = new QueueDiagnosticsRecorder();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		parser.CommandParse(Arg.Any<long>(), Arg.Any<IConnectionService>(), Arg.Any<MarkupText>())
			.Returns(async ValueTask<CallState> (_) =>
			{
				try { await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionBudget.CurrentToken); }
				catch (OperationCanceledException) when (returnNormally) { }
				return CallState.Empty;
			});
		var entered = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var notify = Substitute.For<INotifyService>();
		// A line typed before login has no enactor object, so its connection hears "CPU usage exceeded.".
		// It is timed only if the login lands before it runs.
		notify.NotifyLocalized(Arg.Any<long>(), "CpuUsageExceeded", Arg.Any<AnySharpObject?>(), Arg.Any<object[]>())
			.Returns(async ValueTask (_) =>
			{
				entered.TrySetResult(DateTimeOffset.UtcNow);
				await release.Task.WaitAsync(ExecutionBudget.CurrentToken);
			});
		var connections = Substitute.For<IConnectionService>();
		await using var queue = Create(recorder, parser: parser, notify: notify, connections: connections);
		var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var loggedIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await queue.AdmitWork(async () => { blocked.TrySetResult(); await loggedIn.Task; return null; }, "blocker", "test");
		try
		{
			await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var admission = await queue.AdmitUserCommand(42, MarkupText.Plain("ignored"), ParserState.Empty);
			await Assert.That(admission.Accepted).IsTrue();
			connections.Get(42).Returns(new IConnectionService.ConnectionData(42, new DBRef(10),
				IConnectionService.ConnectionState.LoggedIn, _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
				() => System.Text.Encoding.UTF8, new System.Collections.Concurrent.ConcurrentDictionary<string, string>()));
			loggedIn.TrySetResult();
			var notificationStarted = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(recorder.Recent().Count).IsEqualTo(2);
			var row = recorder.Recent().Single(r => r.Outcome == QueueOutcome.ExecutionLimit);
			await Assert.That(row.Outcome).IsEqualTo(QueueOutcome.ExecutionLimit);
			await Assert.That(row.EndedAt <= notificationStarted).IsTrue();
			await Assert.That(row.ExecutionDuration).IsNotNull();
		}
		finally { loggedIn.TrySetResult(); release.TrySetResult(); }
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task InvalidTargetAttributionFailureDoesNotReplaceAdmissionResult(bool ownerLookup, bool cancelled)
	{
		var recorder = new QueueDiagnosticsRecorder();
		var mediator = QueueAdmissionTests.TargetMediator();
		var executor = new DBRef(2, 1);
		var missing = new DBRef(999, 1);
		Exception failure = cancelled ? new OperationCanceledException(new CancellationToken(true)) : new InvalidOperationException("Attribution unavailable");
		if (ownerLookup)
		{
			var source = await mediator.Send(new GetObjectNodeQuery(executor));
			source.Expect<SharpPlayer>().Object.Owner = new(_ => Task.FromException<SharpPlayer>(failure));
			mediator.Send(Arg.Is<GetObjectNodeQuery>(query => query.DBRef == executor), Arg.Any<CancellationToken>()).Returns(source);
		}
		else
			mediator.Send(Arg.Is<GetObjectNodeQuery>(query => query.DBRef == executor), Arg.Any<CancellationToken>())
				.Returns(_ => ValueTask.FromException<AnyOptionalSharpObject>(failure));
		mediator.Send(Arg.Is<GetObjectNodeQuery>(query => query.DBRef == missing), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObject>(new None()));
		await using var queue = Create(recorder, mediator);
		async Task<QueueAdmissionResult> Admit() => await queue.AdmitAsyncAttribute(
			() => ValueTask.FromResult(ParserState.Empty), new DbRefAttribute(missing, ["ACTION"]), executor);
		if (cancelled)
		{
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await Admit());
			await Assert.That(recorder.Recent().Count).IsEqualTo(0);
		}
		else
		{
			await Assert.That((await Admit()).Reason).IsEqualTo(QueueRejectionReason.InvalidTarget);
			var row = recorder.Recent().Single();
			await Assert.That(row.Outcome).IsEqualTo(QueueOutcome.InvalidTarget);
			await Assert.That(row.Source).IsEqualTo(executor);
			await Assert.That(row.Owner).IsNull();
		}
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}
}
