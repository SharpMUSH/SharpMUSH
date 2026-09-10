using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OneOf;
using OneOf.Types;
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
		IMUSHCodeParser? parser = null, INotifyService? notify = null)
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Limit = config.Limit with { QueueEntryCpuTime = 1000 } });
		return new(parser ?? Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			Substitute.For<ISchedulerFactory>(), Substitute.For<IAttributeService>(), mediator ?? QueueAdmissionTests.TargetMediator(),
			NullLogger<Scheduler>.Instance, options, notify, recorder);
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
		notify.Notify(Arg.Any<long>(), Arg.Any<OneOf<MarkupText, string>>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>())
			.Returns(async ValueTask (_) =>
			{
				entered.TrySetResult(DateTimeOffset.UtcNow);
				await release.Task.WaitAsync(ExecutionBudget.CurrentToken);
			});
		await using var queue = Create(recorder, parser: parser, notify: notify);
		try
		{
			var admission = await queue.AdmitUserCommand(42, MarkupText.Plain("ignored"), ParserState.Empty);
			await Assert.That(admission.Accepted).IsTrue();
			var notificationStarted = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(recorder.Recent().Count).IsEqualTo(1);
			var row = recorder.Recent().Single();
			await Assert.That(row.Outcome).IsEqualTo(QueueOutcome.ExecutionLimit);
			await Assert.That(row.EndedAt <= notificationStarted).IsTrue();
			await Assert.That(row.ExecutionDuration).IsNotNull();
		}
		finally { release.TrySetResult(); }
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
			source.AsPlayer.Object.Owner = new(_ => Task.FromException<SharpPlayer>(failure));
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
