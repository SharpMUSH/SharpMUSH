using Mediator;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Utilities;
using System.Text.RegularExpressions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class QueueAdmissionTests
{
	private static Scheduler Create(uint global = 2, uint owner = 10, IMediator? mediator = null, IMUSHCodeParser? parser = null, IScheduler? scheduler = null, uint milliseconds = 1000, IConnectionService? connections = null, INotifyService? notifications = null)
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Limit = config.Limit with { GlobalQueueLimit = global, PlayerQueueLimit = owner, QueueEntryCpuTime = milliseconds } });
		var factory = Substitute.For<ISchedulerFactory>();
		if (scheduler is not null) factory.GetScheduler().Returns(scheduler);
		return new(parser ?? Substitute.For<IMUSHCodeParser>(), connections ?? Substitute.For<IConnectionService>(),
		 factory, Substitute.For<IAttributeService>(), mediator ?? TargetMediator(),
		 NullLogger<Scheduler>.Instance, options, notifications);
	}
	internal static IMediator TargetMediator()
	{
		var mediator = Substitute.For<IMediator>();
		ConfigureTargets(mediator);
		return mediator;
	}
	private static void ConfigureTargets(IMediator mediator, bool wizard = false, bool queuePower = false)
	{
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var dbRef = call.Arg<GetObjectNodeQuery>().DBRef;
			var player = new SharpPlayer
			{
				Object = new SharpObject
				{
					Key = dbRef.Number, CreationTime = 1, Name = "Semaphore", Type = "PLAYER", Locks = null!, Owner = null!,
					Powers = new(() => (queuePower ? new[] { new SharpPower { Name = "Queue", Alias = "", System = true, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = [] } } : []).ToAsyncEnumerable()), Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!,
					Flags = new(() => (wizard ? new[] { new SharpObjectFlag { Name = "WIZARD", Symbol = "W", System = true, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = [] } } : []).ToAsyncEnumerable()), Parent = null!, Zone = null!, Children = null!
				},
				Location = null!, Home = null!, PasswordHash = "", Quota = 0
			};
			player.Object.Owner = new(_ => Task.FromResult(player));
			return ValueTask.FromResult<AnyOptionalSharpObject>(player);
		});
	}
	private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	[Test]
	public async Task ExecutionLimitNoticeGetsItsOwnBoundedBudget()
	{
		var connections = Substitute.For<IConnectionService>();
		connections.Get(Arg.Any<DBRef>()).Returns(new[]
		{
			new IConnectionService.ConnectionData(12, new DBRef(10), IConnectionService.ConnectionState.LoggedIn,
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8, new())
		}.ToAsyncEnumerable());
		var notifications = Substitute.For<INotifyService>();
		var reported = new TaskCompletionSource<(bool Cancelled, TimeSpan Remaining)>(TaskCreationOptions.RunContinuationsAsynchronously);
		notifications.Notify(12L, Arg.Any<OneOf.OneOf<MarkupText, string>>(), null, INotifyService.NotificationType.Announce)
			.Returns(_ =>
			{
				reported.TrySetResult((ExecutionBudget.CurrentToken.IsCancellationRequested, ExecutionBudget.Current?.Remaining ?? TimeSpan.MaxValue));
				return ValueTask.CompletedTask;
			});
		await using var queue = Create(milliseconds: 10, connections: connections, notifications: notifications);
		await queue.AdmitWork(async () =>
		{
			await Task.Delay(Timeout.Infinite, ExecutionBudget.CurrentToken);
			return CallState.Empty;
		}, "expired", "test", new DBRef(10, 1));
		var result = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(result.Cancelled).IsFalse();
		await Assert.That(result.Remaining > TimeSpan.Zero && result.Remaining <= TimeSpan.FromSeconds(1)).IsTrue();
	}

	[Test]
	public async Task ReservedCompletionCountsQuotaAndPublishesAtFifoTailExactlyOnce()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var completed = Signal(); var started = Signal(); var release = Signal();
		var order = new List<string>();
		parser.CommandListParse(Arg.Any<MString>()).Returns(_ => { order.Add("completion"); completed.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(global: 3, parser: parser);
		var blocker = await queue.AdmitWork(async () => { started.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
			using var reserved = await queue.ReserveCommandList(MarkupText.Plain("completion"), ParserState.RootFor(new DBRef(10)));
			await Assert.That(reserved.Admission.Accepted).IsTrue();
			await Assert.That((await queue.AdmitWork(() => { order.Add("row"); return ValueTask.FromResult<CallState?>(null); }, "row", "test")).Accepted).IsTrue();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(3);
			await Assert.That((await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "overflow", "test")).Reason).IsEqualTo(QueueRejectionReason.GlobalLimit);
			await Assert.That((await reserved.PublishAsync()).Accepted).IsTrue();
			await Assert.That((await reserved.PublishAsync()).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
			reserved.Dispose();
			release.TrySetResult();
			await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
			await Assert.That(order.ToArray()).IsEquivalentTo(new[] { "row", "completion" });
			await Assert.That(order[0]).IsEqualTo("row");
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AbandonedOrHaltedReservationReleasesQuotaWithoutPublishing(bool halt)
	{
		await using var queue = Create(global: 1, scheduler: Substitute.For<IScheduler>());
		using var reserved = await queue.ReserveCommandList(MarkupText.Plain("completion"), ParserState.RootFor(new DBRef(10)));
		await Assert.That(reserved.Admission.Accepted).IsTrue();
		if (halt) await queue.HaltByPid(reserved.Admission.Pid!.Value);
		else reserved.Dispose();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.That((await reserved.PublishAsync()).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		using var next = await queue.ReserveCommandList(MarkupText.Plain("next"), ParserState.RootFor(new DBRef(10)));
		await Assert.That(next.Admission.Accepted).IsTrue();
	}

	[Test]
	public async Task ShutdownReleasesUnpublishedCompletion()
	{
		var queue = Create(global: 1, scheduler: Substitute.For<IScheduler>());
		using var reserved = await queue.ReserveCommandList(MarkupText.Plain("completion"), ParserState.RootFor(new DBRef(10)));
		await queue.DisposeAsync();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.That((await reserved.PublishAsync()).Reason).IsEqualTo(QueueRejectionReason.ShuttingDown);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task BackgroundAdmissionCanSuppressRejectionPublication(bool notify)
	{
		var connections = Substitute.For<IConnectionService>();
		connections.Get(Arg.Any<DBRef>()).Returns(new[]
		{
			new IConnectionService.ConnectionData(12, new DBRef(10), IConnectionService.ConnectionState.LoggedIn,
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8, new())
		}.ToAsyncEnumerable());
		var notifications = Substitute.For<INotifyService>();
		var entered = Signal(); var release = Signal();
		notifications.NotifyLocalized(Arg.Any<long>(), "QueueRejected", Arg.Any<object[]>()).Returns(_ =>
		{
			entered.TrySetResult();
			return new ValueTask(release.Task);
		});
		await using var queue = Create(global: 0, connections: connections, notifications: notifications);
		var admission = queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "background", "recurring", new DBRef(10), notifyOnRejection: notify).AsTask();
		try
		{
			if (notify) { await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); release.TrySetResult(); }
			var result = await admission.WaitAsync(TimeSpan.FromSeconds(2));
			await Assert.That(result.Reason).IsEqualTo(QueueRejectionReason.GlobalLimit);
			await Assert.That(entered.Task.IsCompleted).IsEqualTo(notify);
		}
		finally { release.TrySetResult(); await admission; }
	}

	[Test]
	public async Task CreditOnlyReconciliationUsesFreshBudgetAfterCommandCancellation()
	{
		await using var queue = Create(scheduler: Substitute.For<IScheduler>());
		var readable = false;
		var fresh = true;
		using var cancelled = new CancellationTokenSource();
		using var original = ExecutionBudget.FromMilliseconds(30000, cancelled.Token);
		using (await queue.EnterSemaphoreMutationAsync())
		using (original.Enter())
		{
			try
			{
				await queue.ApplySemaphoreCommandAsync(new(new DBRef(10), ["SEMAPHORE"]), 1, false,
					_ => { cancelled.Cancel(); return ValueTask.FromException(new OperationCanceledException(cancelled.Token)); },
					() =>
					{
						fresh &= ExecutionBudget.CurrentToken != original.Token && !ExecutionBudget.CurrentToken.IsCancellationRequested;
						return readable ? ValueTask.FromResult(false) : ValueTask.FromException<bool>(new IOException("unreadable credit"));
					});
			}
			catch (AggregateException) { }
		}
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.ThrowsAsync<IOException>(async () => { using var lease = await queue.EnterSemaphoreMutationAsync(); });
		readable = true;
		using (await queue.EnterSemaphoreMutationAsync()) { }
		await Assert.That(fresh).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CommittedSemaphoreCommandFinishesDespiteLostAcknowledgementAndTimerCleanup(bool drain)
	{
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(global: 3, scheduler: scheduler);
		var block = Signal(); var release = Signal();
		await queue.AdmitWork(async () => { block.SetResult(); await release.Task; return null; }, "block", "test");
		await block.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var semaphore = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
			var pending = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think waiting"), ParserState.Empty, semaphore, 0);
			scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new IOException("Quartz unavailable")));
			using (await queue.EnterSemaphoreMutationAsync())
			{
				try
				{
					await queue.ApplySemaphoreCommandAsync(semaphore, 1, drain,
					_ => ValueTask.FromException(new IOException("committed acknowledgement lost")), () => ValueTask.FromResult(true));
				}
				catch (IOException) { }
				await Assert.That((await queue.ReleaseScheduledWork(pending.Pid!.Value)).Accepted).IsFalse();
				await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(drain ? 1 : 2);
			}
		}
		finally { release.SetResult(); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AmbiguousSemaphoreCommandRetainsReservationUntilReconciled(bool drain)
	{
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(scheduler: scheduler);
		var semaphore = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
		var pending = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think waiting"), ParserState.Empty, semaphore, 0);
		var key = new TriggerKey($"dbref:-{pending.Pid}", $"semaphore:{semaphore}");
		scheduler.GetTriggerKeys(Arg.Any<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>()).Returns(new[] { key });
		scheduler.GetTrigger(key, Arg.Any<CancellationToken>()).Returns(TriggerBuilder.Create().WithIdentity(key).ForJob("waiter").Build());
		scheduler.GetJobDetail(Arg.Any<JobKey>(), Arg.Any<CancellationToken>()).Returns(JobBuilder.Create<SemaphoreTask>()
			.SetJobData(new JobDataMap { ["State"] = ParserState.Empty }).Build());
		var readable = false;
		using (await queue.EnterSemaphoreMutationAsync())
		{
			try
			{
				await queue.ApplySemaphoreCommandAsync(semaphore, 1, drain,
				_ => ValueTask.FromException(new IOException("commit acknowledgement lost")),
				() => readable ? ValueTask.FromResult(false) : ValueTask.FromException<bool>(new IOException("provider unavailable")));
			}
			catch (AggregateException) { }
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
			await Assert.That(await queue.ModifyQRegisters(semaphore, new() { ["unexpected"] = MarkupText.Plain("change") })).IsFalse();
			await Assert.That(await queue.DrainCounted(semaphore)).IsEqualTo(0);
			await Assert.That((await queue.NotifyCounted(semaphore, 1)).Count).IsEqualTo(0);
			await Assert.That(scheduler.ReceivedCalls().Any(call => call.GetMethodInfo().Name is "UnscheduleJob" or "UnscheduleJobs")).IsFalse();
			await Assert.That((await queue.ReleaseScheduledWork(pending.Pid!.Value)).Accepted).IsFalse();
		}
		var blocked = false;
		try { using var ignored = await queue.EnterSemaphoreMutationAsync(); }
		catch (IOException) { blocked = true; }
		await Assert.That(blocked).IsTrue();
		readable = true;
		using (await queue.EnterSemaphoreMutationAsync())
			await Assert.That(await queue.DrainCounted(semaphore)).IsEqualTo(1);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FailedSemaphoreCommandPersistenceLeavesWaiterPending(bool drain)
	{
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(scheduler: scheduler);
		var semaphore = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
		var pending = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think waiting"), ParserState.Empty, semaphore, 0);
		var key = new TriggerKey($"dbref:-{pending.Pid}", $"semaphore:{semaphore}");
		scheduler.GetTriggerKeys(Arg.Any<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>()).Returns(new[] { key });
		scheduler.GetTrigger(key, Arg.Any<CancellationToken>()).Returns(TriggerBuilder.Create().WithIdentity(key).ForJob("waiter").Build());
		using (await queue.EnterSemaphoreMutationAsync())
		{
			try
			{
				await queue.ApplySemaphoreCommandAsync(semaphore, 1, drain,
				_ => ValueTask.FromException(new IOException("counter write rejected")), () => ValueTask.FromResult(false));
			}
			catch (IOException) { }
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
			await Assert.That(await queue.DrainCounted(semaphore)).IsEqualTo(1);
		}
	}

	[Test]
	public async Task SemaphoreAttributeReadUsesTheExecutionToken()
	{
		var mediator = TargetMediator();
		var validation = Substitute.For<IValidateService>();
		validation.Valid(Arg.Any<IValidateService.ValidationType>(), Arg.Any<MarkupString.MarkupText>(), Arg.Any<OneOf.OneOf<AnySharpObject, SharpAttributeEntry, SharpChannel, None>>()).Returns(true);
		CancellationToken observed = default;
		mediator.CreateStream(Arg.Any<GetAttributeWithInheritanceQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => { observed = call.ArgAt<CancellationToken>(1); return AsyncEnumerable.Empty<AttributeWithInheritance>(); });
		var service = new SharpMUSH.Library.Services.AttributeService(mediator, Substitute.For<IPermissionService>(),
			Substitute.For<ILocateService>(), validation, Substitute.For<INotifyService>(),
			Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IServiceProvider>());
		var target = (await mediator.Send(new GetObjectNodeQuery(new DBRef(10)))).Known;
		using var budget = ExecutionBudget.FromMilliseconds(30000);
		using var scope = budget.Enter();
		await service.GetAttributeAsync(target, target, "SEMAPHORE", IAttributeService.AttributeMode.Execute, false);
		await Assert.That(observed).IsEqualTo(budget.Token);
	}

	[Test]
	public async Task QueuedWorkDoesNotConsumeSubmittingBreakState()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		ParserState? captured = null;
		parser.FromState(Arg.Do<ParserState>(state => captured = state)).Returns(parser);
		var executed = Signal();
		parser.CommandListParse(Arg.Any<MarkupString.MarkupText>()).Returns(call =>
		{
			captured!.ExecutionStack.TryPop(out _);
			executed.SetResult();
			return ValueTask.FromResult<CallState?>(null);
		});
		var source = ParserState.Empty;
		source.ExecutionStack.Push(new Execution(CommandListBreak: true));
		await using var queue = Create(parser: parser);
		await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think queued"), source);
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(source.ExecutionStack.TryPeek(out var execution) && execution.CommandListBreak).IsTrue();
		await Assert.That(ReferenceEquals(source.ExecutionStack, captured!.ExecutionStack)).IsFalse();
	}

	[Test]
	public async Task DrainDoesNotCancelWorkWhoseTimeoutAlreadyPublishedIt()
	{
		var scheduler = Substitute.For<IScheduler>();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var executed = Signal();
		parser.CommandListParse(Arg.Any<MarkupString.MarkupText>()).Returns(_ => { executed.SetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(global: 3, scheduler: scheduler, parser: parser);
		var blocked = Signal(); var release = Signal();
		await queue.AdmitWork(async () => { blocked.SetResult(); await release.Task; return null; }, "block", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var semaphore = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
			var pending = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think timeout"), ParserState.Empty, semaphore, 0);
			var key = new TriggerKey($"dbref:-{pending.Pid}", $"semaphore:{semaphore}");
			scheduler.GetTriggerKeys(Arg.Any<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>()).Returns(new[] { key });
			await queue.ReleaseScheduledWork(pending.Pid!.Value, semaphoreTimeout: true);
			await Assert.That((await queue.ReleaseScheduledWork(pending.Pid.Value)).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
			using (await queue.EnterSemaphoreMutationAsync())
				await Assert.That(await queue.DrainCounted(semaphore)).IsEqualTo(0);
		}
		finally { release.SetResult(); }
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task CountedDrainCountsOnlyRemovedPendingReservations()
	{
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(global: 3, scheduler: scheduler);
		var semaphore = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
		var first = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think first"), ParserState.Empty, semaphore, 0);
		var second = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think second"), ParserState.Empty, semaphore, 1);
		scheduler.GetTriggerKeys(Arg.Any<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>())
			.Returns(new[] { new TriggerKey($"dbref:-{first.Pid}", $"semaphore:{semaphore}"), new TriggerKey($"dbref:-{second.Pid}", $"semaphore:{semaphore}") });
		await Assert.That(await queue.DrainCounted(semaphore, 1)).IsEqualTo(1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(await queue.DrainCounted(semaphore)).IsEqualTo(1);
		await Assert.That(await queue.DrainCounted(semaphore)).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task ManagedWaitDoesNotExposeReservationBeforeCounterLease()
	{
		var count = 2;
		await using var queue = Create(mediator: CountingMediator(() => count, value => count = value));
		Task<QueueAdmissionResult>? pending = null;
		try
		{
			using var lease = await queue.EnterSemaphoreMutationAsync();
			pending = queue.AdmitCommandList(MarkupString.MarkupText.Plain("think pending"), ParserState.Empty,
				new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 2, TimeSpan.FromHours(1), manageSemaphoreCount: true).AsTask();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		}
		finally
		{
			if (pending is not null)
			{
				var admitted = await pending;
				await queue.HaltByPid(admitted.Pid!.Value);
			}
		}
		await Assert.That(count).IsEqualTo(2);
	}

	internal static IMediator CountingMediator(Func<int> read, Action<int> write)
	{
		var mediator = TargetMediator();
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
			new[] { new SharpAttribute("", "", "SEMAPHORE", [], null, "SEMAPHORE", null!, null!, null!)
			{ Value = MarkupString.MarkupText.Plain(read().ToString()) } }.ToAsyncEnumerable());
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			write(int.Parse(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText()));
			return ValueTask.FromResult(true);
		});
		return mediator;
	}

	[Test]
	public async Task ManagedSemaphorePublishesCountBeforeZeroTimeoutCanRelease()
	{
		var count = 0;
		var mediator = CountingMediator(() => count, value => count = value);
		var scheduler = Substitute.For<IScheduler>();
		Scheduler? queue = null;
		var observedAtPublication = -1;
		Task<QueueAdmissionResult>? firing = null;
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			observedAtPublication = count;
			var pid = long.Parse(call.Arg<ITrigger>().Key.Name.Split('-').Last());
			// Quartz invokes jobs independently of ScheduleJob's return. Start the competing
			// transition here, then await it after admission releases its deferred lease.
			firing = queue!.ReleaseScheduledWork(pid, semaphoreTimeout: true).AsTask();
			return Task.FromResult(DateTimeOffset.UtcNow);
		});
		var executed = Signal();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		parser.CommandListParse(Arg.Any<MarkupString.MarkupText>()).Returns(_ => { executed.SetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var ownedQueue = queue = Create(mediator: mediator, scheduler: scheduler, parser: parser);
		var result = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think ready"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 99, TimeSpan.Zero, manageSemaphoreCount: true);
		await firing!.WaitAsync(TimeSpan.FromSeconds(5));
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(result.Accepted).IsTrue();
		await Assert.That(observedAtPublication).IsEqualTo(1);
		await Assert.That(count).IsEqualTo(0);
	}

	[Test]
	public async Task FailedManagedTimeoutWriteRetainsRetryableReservation()
	{
		var count = 0;
		var fail = false;
		var mediator = CountingMediator(() => count, value =>
		{
			if (fail) throw new InvalidOperationException("injected counter failure");
			count = value;
		});
		await using var queue = Create(mediator: mediator, scheduler: Substitute.For<IScheduler>());
		var result = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think ready"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, manageSemaphoreCount: true);
		fail = true;
		try
		{
			await queue.ReleaseScheduledWork(result.Pid!.Value, semaphoreTimeout: true);
			throw new Exception("Expected injected counter failure");
		}
		catch (InvalidOperationException exception)
		{
			await Assert.That(exception.Message).IsEqualTo("injected counter failure");
		}
		await Assert.That(count).IsEqualTo(1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		fail = false;
		await Assert.That((await queue.ReleaseScheduledWork(result.Pid!.Value, semaphoreTimeout: true)).Accepted).IsTrue();
		await Assert.That(count).IsEqualTo(0);
	}

	[Test]
	[Arguments("read")]
	[Arguments("god")]
	[Arguments("write")]
	public async Task ManagedSemaphoreProviderWorkHonorsEntryDeadline(string phase)
	{
		using var release = new CancellationTokenSource();
		var entered = Signal();
		var drained = Signal();
		async Task<T> Stall<T>(CancellationToken token)
		{
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
			return default!;
		}
		async IAsyncEnumerable<SharpAttribute> Read([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
		{
			await Stall<bool>(token);
			yield break;
		}
		var count = 0;
		var writes = 0;
		var mediator = CountingMediator(() => count, value => count = value);
		if (phase == "read") mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(c => Read(c.Arg<CancellationToken>()));
		if (phase == "god") mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == 1), Arg.Any<CancellationToken>())
			.Returns(c => new ValueTask<AnyOptionalSharpObject>(Stall<AnyOptionalSharpObject>(c.Arg<CancellationToken>())));
		if (phase == "write") mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>())
			.Returns(c =>
			{
				// The provider may commit before cancellation wins delivery of its acknowledgement.
				count = int.Parse(c.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText());
				return Interlocked.Increment(ref writes) == 1
					? new ValueTask<bool>(Stall<bool>(c.Arg<CancellationToken>())) : ValueTask.FromResult(true);
			});
		await using var queue = Create(global: 10, mediator: mediator);
		await queue.AdmitWork(async () =>
		{
			await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think waiting"), ParserState.Empty,
				new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, TimeSpan.FromHours(1), manageSemaphoreCount: true);
			return null;
		}, "managed-stall", "test");
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await queue.AdmitWork(() => { drained.TrySetResult(); return ValueTask.FromResult<CallState?>(null); }, "after-stall", "test");
			await drained.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await Assert.That(count).IsEqualTo(0);
		}
		finally { release.Cancel(); }
	}

	[Test]
	public async Task ShutdownCancelsBookkeepingWithUnlimitedEntryBudget()
	{
		using var release = new CancellationTokenSource();
		var entered = Signal();
		async IAsyncEnumerable<SharpAttribute> Read([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
		{
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
			yield break;
		}
		var mediator = TargetMediator();
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(c => Read(c.Arg<CancellationToken>()));
		var queue = Create(mediator: mediator, milliseconds: 0);
		var entry = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think timeout"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 1, TimeSpan.FromHours(1));
		await queue.ReleaseScheduledWork(entry.Pid!.Value, semaphoreTimeout: true);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var shutdown = queue.DisposeAsync().AsTask();
		try { await shutdown.WaitAsync(TimeSpan.FromSeconds(1)); }
		finally { release.Cancel(); await shutdown; }
	}

	[Test]
	public async Task TimeoutBookkeepingCannotBlockTheQueuePastItsDeadline()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var ran = false;
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { ran = true; return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(global: 3, parser: parser, mediator: CountingMediator(() => 1, _ => { }));
		var entered = Signal(); var release = Signal(); var drained = Signal();
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "blocker", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var waiting = await queue.AdmitCommandList(MarkupText.Plain("think expired"), ParserState.Empty,
				new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 1, TimeSpan.FromHours(1));
			await queue.ReleaseScheduledWork(waiting.Pid!.Value, semaphoreTimeout: true);
			using var held = await queue.EnterSemaphoreMutationAsync();
			await queue.AdmitWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "following", "test");
			release.TrySetResult();
			await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}
		finally { release.TrySetResult(); }
		await Assert.That(ran).IsFalse();
	}

	[Test]
	public async Task ContendedSemaphoreLeaseCannotBlockTheQueuePastItsDeadline()
	{
		await using var queue = Create();
		using var held = await queue.EnterSemaphoreMutationAsync();
		var entered = Signal();
		var drained = Signal();
		var acquired = false;
		await queue.AdmitWork(async () =>
		{
			entered.SetResult();
			using var lease = await queue.EnterSemaphoreMutationAsync();
			acquired = true;
			return null;
		}, "contended", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await queue.AdmitWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "following", "test");
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(acquired).IsFalse();
	}

	[Test]
	public async Task FailedCustomSemaphoreInitializationRemovesPartialAttribute()
	{
		SharpAttribute? attribute = null;
		var mediator = TargetMediator();
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
			(attribute is null ? Array.Empty<SharpAttribute>() : new[] { attribute }).ToAsyncEnumerable());
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			attribute = new SharpAttribute("", "", "CUSTOM", [], null, "CUSTOM", null!, null!, null!)
			{
				Value = call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value,
				Owner = new(_ => Task.FromResult<SharpPlayer?>(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Owner))
			};
			return ValueTask.FromResult(true);
		});
		mediator.CreateStream(Arg.Any<GetAttributeFlagsQuery>(), Arg.Any<CancellationToken>()).Returns(
			new[] { "no_inherit", "no_clone", "locked" }.Select(name => new SharpAttributeFlag
			{ Name = name, Symbol = "", System = true, Inheritable = false }).ToAsyncEnumerable());
		var fail = true;
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeFlagCommand>(), Arg.Any<CancellationToken>())
			.Returns(call => !fail || call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeFlagCommand>().Flag.Name != "no_clone");
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.WipeAttributeCommand>(), Arg.Any<CancellationToken>())
			.Returns(_ => { attribute = null; return true; });
		await using var queue = Create(mediator: mediator, scheduler: Substitute.For<IScheduler>());
		await Assert.That(async () => await queue.AdmitCommandList(MarkupText.Plain("think pending"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["CUSTOM"]), 0, manageSemaphoreCount: true)).Throws<InvalidOperationException>();
		await Assert.That(attribute).IsNull();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		fail = false;
		var retry = await queue.AdmitCommandList(MarkupText.Plain("think pending"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["CUSTOM"]), 0, manageSemaphoreCount: true);
		await Assert.That(retry.Accepted).IsTrue();
		await Assert.That(attribute!.Value.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task FailedHaltCounterWriteRetainsPidForRetry()
	{
		var count = 0;
		var fail = false;
		var mediator = CountingMediator(() => count, value =>
		{
			if (fail) throw new InvalidOperationException("injected halt failure");
			count = value;
		});
		await using var queue = Create(mediator: mediator, scheduler: Substitute.For<IScheduler>());
		var admitted = await queue.AdmitCommandList(MarkupText.Plain("think pending"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, manageSemaphoreCount: true);
		fail = true;
		await Assert.That(async () => await queue.HaltByPid(admitted.Pid!.Value)).Throws<InvalidOperationException>();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(count).IsEqualTo(1);
		fail = false;
		await Assert.That(await queue.HaltByPid(admitted.Pid!.Value)).IsTrue();
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task QuartzSemaphoreReleaseObservesShutdownCancellation()
	{
		using var shutdown = new CancellationTokenSource();
		using var escape = new CancellationTokenSource();
		var entered = Signal();
		var queue = Substitute.For<ITaskScheduler>();
		var context = Substitute.For<IJobExecutionContext>();
		context.CancellationToken.Returns(shutdown.Token);
		context.Trigger.Returns(TriggerBuilder.Create().WithIdentity("dbref:10-42", "semaphore:10/SEMAPHORE").Build());
		context.MergedJobDataMap.Returns(new JobDataMap { ["Generation"] = 0L });
		async ValueTask<QueueAdmissionResult> WaitForShutdown()
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, escape.Token);
			entered.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
			return new QueueAdmissionResult(42, QueueRejectionReason.None);
		}
		queue.ReleaseScheduledWork(42, true, 0).Returns(_ => WaitForShutdown());
		var running = new SemaphoreTask(queue).Execute(context);
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			shutdown.Cancel();
			await Assert.That(async () => await running.WaitAsync(TimeSpan.FromSeconds(1))).Throws<OperationCanceledException>();
		}
		finally { escape.Cancel(); try { await running; } catch (OperationCanceledException) { } }
	}

	[Test]
	public async Task QuartzSemaphoreReleaseHasFiniteAccountingBudget()
	{
		var queue = Substitute.For<ITaskScheduler>();
		var context = Substitute.For<IJobExecutionContext>();
		context.Scheduler.Returns(Substitute.For<IScheduler>());
		context.Trigger.Returns(TriggerBuilder.Create().WithIdentity("dbref:10-42", "semaphore:10/SEMAPHORE").Build());
		context.MergedJobDataMap.Returns(new JobDataMap { ["Generation"] = 0L });
		context.JobDetail.Returns(JobBuilder.Create<SemaphoreTask>().Build());
		TimeSpan? remaining = null;
		queue.ReleaseScheduledWork(42, true, 0).Returns(_ =>
		{
			remaining = ExecutionBudget.Current?.Remaining;
			return ValueTask.FromResult(new QueueAdmissionResult(42, QueueRejectionReason.None));
		});
		await new SemaphoreTask(queue).Execute(context);
		await Assert.That(remaining.HasValue && remaining.Value > TimeSpan.Zero && remaining.Value < TimeSpan.FromMinutes(1)).IsTrue();
		await Assert.That(ExecutionBudget.Current).IsNull();
	}

	[Test]
	[Arguments("false")]
	[Arguments("throw")]
	[Arguments("cancel")]
	public async Task FailedAdmissionRollbackRetainsRepairUntilProviderRecovers(string failure)
	{
		var count = 0;
		var broken = true;
		var writes = 0;
		var mediator = CountingMediator(() => count, value => count = value);
		async ValueTask<bool> Write(NSubstitute.Core.CallInfo call)
		{
			if (Interlocked.Increment(ref writes) > 1 && broken)
			{
				if (failure == "throw") throw new InvalidOperationException("repair unavailable");
				if (failure == "cancel") await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
				return false;
			}
			count = int.Parse(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText());
			return true;
		}
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(call => Write(call));
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromException<DateTimeOffset>(new InvalidOperationException("schedule unavailable")));
		await using var queue = Create(global: 1, mediator: mediator, scheduler: scheduler);
		await Assert.That(async () => await queue.AdmitCommandList(MarkupText.Plain("think rejected"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, manageSemaphoreCount: true)).Throws<AggregateException>();
		await Assert.That(count).IsEqualTo(1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That((await queue.ReleaseScheduledWork(1)).Accepted).IsFalse();
		// No later counter transaction may run before the uncertain write is repaired.
		using (var bounded = ExecutionBudget.FromMilliseconds(50))
		using (bounded.Enter())
			await Assert.That(async () => { using var lease = await queue.EnterSemaphoreMutationAsync(); }).Throws<Exception>();
		await Assert.That(count).IsEqualTo(1);
		broken = false;
		count = 7; // An administrator may bypass the semaphore gate with a raw attribute edit.
		await Assert.That(async () => { using var lease = await queue.EnterSemaphoreMutationAsync(); }).Throws<InvalidOperationException>();
		await Assert.That(count).IsEqualTo(7);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		count = 1;
		await Assert.That(await queue.HaltByPid(1)).IsTrue();
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	[Arguments("none")]
	[Arguments("owner")]
	[Arguments("name")]
	[Arguments("flags")]
	[Arguments("children")]
	public async Task FirstCreatedSemaphoreCleanupPreservesChangedMetadata(string changed)
	{
		SharpAttribute? attribute = null;
		var mediator = TargetMediator();
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
			(attribute is null ? Array.Empty<SharpAttribute>() : new[] { attribute }).ToAsyncEnumerable());
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var command = call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>();
			attribute = new SharpAttribute("created-id", "key", "SEMAPHORE", [], null, "SEMAPHORE", null!, null!, null!)
			{ Value = command.Value, Owner = new(_ => Task.FromResult<SharpPlayer?>(command.Owner)) };
			return ValueTask.FromResult(true);
		});
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.WipeAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{ attribute = null; return ValueTask.FromResult(true); });
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			attribute = changed switch
			{
				"owner" => attribute! with { Owner = new(_ => Task.FromResult<SharpPlayer?>(null)) },
				"name" => attribute! with { Name = "RENAMED" },
				"flags" => attribute! with { Flags = [new SharpAttributeFlag { Name = "wizard", Symbol = "", System = true, Inheritable = false }] },
				"children" => attribute! with { Leaves = new(_ => Task.FromResult(new[] { attribute! }.ToAsyncEnumerable())) },
				_ => attribute
			};
			return Task.FromException<DateTimeOffset>(new InvalidOperationException("schedule unavailable"));
		});
		await using var queue = Create(global: 1, mediator: mediator, scheduler: scheduler);
		try
		{
			await queue.AdmitCommandList(MarkupText.Plain("think rejected"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, manageSemaphoreCount: true);
		}
		catch (InvalidOperationException) { }
		catch (AggregateException) { }
		if (changed == "none") await Assert.That(attribute).IsNull();
		else
		{
			await Assert.That(attribute).IsNotNull();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
			await mediator.DidNotReceive().Send(Arg.Any<SharpMUSH.Library.Commands.Database.WipeAttributeCommand>(), Arg.Any<CancellationToken>());
			attribute = null; // Explicit administrator acknowledgement permits reservation cleanup.
			await queue.HaltByPid(1);
		}
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task ShutdownWaitsForDelayedPublicationCleanup()
	{
		var entered = Signal();
		var publish = Signal();
		var exists = false;
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			entered.TrySetResult();
			await publish.Task;
			exists = true;
			return DateTimeOffset.UtcNow;
		});
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ => { exists = false; return true; });
		var queue = Create(scheduler: scheduler);
		var pending = queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty, TimeSpan.FromDays(100)).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
		var stopping = queue.DisposeAsync().AsTask();
		try { await Assert.That(stopping.IsCompleted).IsFalse(); }
		finally
		{
			publish.TrySetResult();
			try { await pending; } catch (OperationCanceledException) { }
			await stopping;
		}
		await Assert.That(exists).IsFalse();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task ImmediateDelayedTriggerWaitsForPublicationToSettle()
	{
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(scheduler: scheduler);
		Task<QueueAdmissionResult>? firing = null;
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			firing = queue.ReleaseScheduledWork(1).AsTask();
			await Assert.That(firing.IsCompleted).IsFalse();
			return DateTimeOffset.UtcNow;
		});
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think immediate"), ParserState.Empty, TimeSpan.Zero);
		await Assert.That(admission.Accepted).IsTrue();
		await Assert.That((await firing!.WaitAsync(TimeSpan.FromSeconds(2))).Accepted).IsTrue();
	}

	[Test]
	public async Task HaltDuringDelayedPublicationCancelsAndRemovesThePublishedTrigger()
	{
		var entered = Signal();
		var publish = Signal();
		var exists = false;
		var publicationToken = CancellationToken.None;
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			publicationToken = call.Arg<CancellationToken>();
			entered.TrySetResult();
			await publish.Task; // Simulate a provider that commits despite cancellation.
			exists = true;
			return DateTimeOffset.UtcNow;
		});
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ => { exists = false; return true; });
		await using var queue = Create(scheduler: scheduler);
		var pending = queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty, TimeSpan.FromDays(100)).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
		var halt = queue.HaltByPid(1).AsTask();
		try
		{
			await Assert.That(publicationToken.IsCancellationRequested).IsTrue();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		}
		finally
		{
			publish.TrySetResult();
			try { await pending; } catch (OperationCanceledException) { }
			await halt;
		}
		await Assert.That(exists).IsFalse();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LostDelayedScheduleAcknowledgementKeepsQuotaUntilTriggerCleanup(bool cleanupFails)
	{
		var exists = false;
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns<DateTimeOffset>(_ =>
		{
			exists = true;
			throw new InvalidOperationException("Schedule acknowledgement lost");
		});
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			if (cleanupFails) throw new InvalidOperationException("Cleanup unavailable");
			exists = false;
			return true;
		});
		await using var queue = Create(scheduler: scheduler);
		await Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty, TimeSpan.FromDays(100)));
		await Assert.That(exists).IsEqualTo(cleanupFails);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(cleanupFails ? 1 : 0);
		if (cleanupFails)
		{
			await Assert.That((await queue.ReleaseScheduledWork(1)).Accepted).IsFalse();
			cleanupFails = false;
			await queue.HaltByPid(1);
			await Assert.That(exists).IsFalse();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		}
	}

	[Test]
	public async Task DelayedSchedulingHonorsExecutionCancellation()
	{
		using var escape = new CancellationTokenSource();
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.Arg<CancellationToken>(), escape.Token);
			await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
			return DateTimeOffset.UtcNow;
		});
		await using var queue = Create(scheduler: scheduler);
		using var budget = ExecutionBudget.FromMilliseconds(50);
		using var scope = budget.Enter();
		var pending = queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty, TimeSpan.FromHours(1)).AsTask();
		try { await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(1))).Throws<OperationCanceledException>(); }
		finally
		{
			escape.Cancel();
			try { await pending; } catch (OperationCanceledException) { }
		}
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	[Arguments("none")]
	[Arguments("flags")]
	[Arguments("children")]
	public async Task CreatedSemaphoreRepairPreservesInterveningMetadata(string changed)
	{
		SharpAttribute? attribute = null;
		var available = false;
		var mediator = TargetMediator();
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
			(attribute is null ? Array.Empty<SharpAttribute>() : new[] { attribute }).ToAsyncEnumerable());
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			attribute = new SharpAttribute("created-id", "key", "SEMAPHORE", [], null, "SEMAPHORE", null!, null!, null!)
			{
				Value = call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value,
				Owner = new(_ => Task.FromResult<SharpPlayer?>(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Owner))
			};
			return ValueTask.FromResult(true);
		});
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.WipeAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			if (!available) return ValueTask.FromResult(false);
			attribute = null;
			return ValueTask.FromResult(true);
		});
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromException<DateTimeOffset>(new InvalidOperationException("schedule unavailable")));
		await using var queue = Create(global: 1, mediator: mediator, scheduler: scheduler);
		await Assert.That(async () => await queue.AdmitCommandList(MarkupText.Plain("think rejected"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, manageSemaphoreCount: true)).Throws<AggregateException>();
		available = true;
		if (changed != "none")
		{
			attribute = changed == "flags"
				? attribute! with { Flags = [new SharpAttributeFlag { Name = "wizard", Symbol = "", System = true, Inheritable = false }] }
				: attribute! with { Leaves = new(_ => Task.FromResult(new[] { attribute! }.ToAsyncEnumerable())) };
			await Assert.That(async () => { using var lease = await queue.EnterSemaphoreMutationAsync(); }).Throws<InvalidOperationException>();
			await Assert.That(attribute).IsNotNull();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
			attribute = null; // Explicit administrator removal acknowledges the conflicting repair.
		}
		await Assert.That(await queue.HaltByPid(1)).IsTrue();
		await Assert.That(attribute).IsNull();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	[Arguments("no_inherit")]
	[Arguments("no_clone")]
	[Arguments("locked")]
	public async Task MissingSemaphoreFlagNamesTheUnavailableDefinition(string missing)
	{
		var mediator = CountingMediator(() => 1, _ => { });
		mediator.CreateStream(Arg.Any<GetAttributeFlagsQuery>(), Arg.Any<CancellationToken>()).Returns(
			new[] { "no_inherit", "no_clone", "locked" }.Where(name => name != missing)
				.Select(name => new SharpAttributeFlag { Name = name, Symbol = "", System = true, Inheritable = false }).ToAsyncEnumerable());
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeFlagCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(true));
		try
		{
			await SharpMUSH.Library.Services.SemaphoreAttributes.InitializeAsync(mediator, new DBRef(10), ["CUSTOM"]);
			throw new Exception("Expected missing definition failure");
		}
		catch (InvalidOperationException exception)
		{
			await Assert.That(exception.Message).IsEqualTo($"Attribute flag '{missing}' is not defined; cannot initialize semaphore attribute.");
		}
	}

	[Test]
	public async Task DelayedHaltDoesNotWaitForUnrelatedSemaphoreMutation()
	{
		await using var queue = Create(scheduler: Substitute.For<IScheduler>());
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think delayed"), ParserState.Empty, TimeSpan.FromHours(1));
		using var held = await queue.EnterSemaphoreMutationAsync();
		using var budget = ExecutionBudget.FromMilliseconds(100);
		using var scope = budget.Enter();
		await Assert.That(await queue.HaltByPid(admission.Pid!.Value)).IsTrue();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	[Arguments("not-a-counter")]
	[Arguments("2147483647")]
	public async Task InvalidManagedCounterRejectsWithoutWriting(string value)
	{
		var writes = 0;
		var mediator = CountingMediator(() => 0, _ => writes++);
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(
			new[] { new SharpAttribute("id", "key", "SEMAPHORE", [], null, "SEMAPHORE", null!, null!, null!)
			{ Value = MarkupText.Plain(value) } }.ToAsyncEnumerable());
		await using var queue = Create(mediator: mediator, scheduler: Substitute.For<IScheduler>());
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think rejected"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, manageSemaphoreCount: true);
		await Assert.That(admission.Reason).IsEqualTo(QueueRejectionReason.InvalidTarget);
		await Assert.That(writes).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	[Arguments("read", false)]
	[Arguments("owner", false)]
	[Arguments("read", true)]
	[Arguments("owner", true)]
	public async Task CreatedSemaphoreRepairRecoversAfterIdentityReadOutage(string phase, bool modified)
	{
		SharpAttribute? attribute = null;
		var available = false;
		SharpPlayer? creator = null;
		var mediator = TargetMediator();
		IAsyncEnumerable<SharpAttribute> Read()
		{
			if (attribute is null) return Array.Empty<SharpAttribute>().ToAsyncEnumerable();
			if (phase == "read" && !available) throw new InvalidOperationException("read unavailable");
			return new[] { attribute with { Owner = new(_ => available || phase != "owner"
				? Task.FromResult(creator) : Task.FromException<SharpPlayer?>(new InvalidOperationException("owner unavailable"))) } }.ToAsyncEnumerable();
		}
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(_ => Read());
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var command = call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>();
			creator = command.Owner;
			attribute = new SharpAttribute("new-id", "key", "SEMAPHORE", [], null, "SEMAPHORE", null!, null!, null!) { Value = command.Value };
			return ValueTask.FromResult(true);
		});
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.WipeAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{ attribute = null; return ValueTask.FromResult(true); });
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromException<DateTimeOffset>(new InvalidOperationException("schedule unavailable")));
		await using var queue = Create(global: 1, mediator: mediator, scheduler: scheduler);
		await Assert.That(async () => await queue.AdmitCommandList(MarkupText.Plain("think rejected"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, manageSemaphoreCount: true)).Throws<AggregateException>();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		available = true;
		if (modified)
		{
			attribute = attribute! with { Flags = [new SharpAttributeFlag { Name = "wizard", Symbol = "", System = true, Inheritable = false }] };
			await Assert.That(async () => await queue.HaltByPid(1)).Throws<InvalidOperationException>();
			await Assert.That(attribute).IsNotNull();
			attribute = null;
		}
		await Assert.That(await queue.HaltByPid(1)).IsTrue();
		await Assert.That(attribute).IsNull();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task FailedQuartzSemaphoreReleaseRequestsRetryBeforeDeletingSchedule()
	{
		var scheduler = Substitute.For<IScheduler>();
		var queue = Substitute.For<ITaskScheduler>();
		var context = Substitute.For<IJobExecutionContext>();
		context.Scheduler.Returns(scheduler);
		context.Trigger.Returns(TriggerBuilder.Create().WithIdentity("dbref:10-42", "semaphore:10/SEMAPHORE").Build());
		context.JobDetail.Returns(JobBuilder.Create<SemaphoreTask>().Build());
		context.MergedJobDataMap.Returns(new JobDataMap { ["Generation"] = 0L });
		var fail = true;
		queue.ReleaseScheduledWork(42, true, 0).Returns(_ => fail
			? throw new InvalidOperationException("injected release failure")
			: ValueTask.FromResult(new QueueAdmissionResult(42, QueueRejectionReason.None)));
		var job = new SemaphoreTask(queue);
		await Assert.That(async () => await job.Execute(context)).Throws<JobExecutionException>();
		await scheduler.DidNotReceive().UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>());
		await scheduler.DidNotReceive().DeleteJob(Arg.Any<JobKey>(), Arg.Any<CancellationToken>());
		fail = false;
		await job.Execute(context);
		await queue.Received(2).ReleaseScheduledWork(42, true, 0);
		// Generation-aware cleanup belongs to the ledger, so a stale job cannot remove a new timer.
		await scheduler.DidNotReceive().UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>());
		await scheduler.DidNotReceive().DeleteJob(Arg.Any<JobKey>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ManagedNegativeCreditIsRereadAndCommittedBeforeExecution()
	{
		var count = -1;
		var observed = -99;
		var mediator = CountingMediator(() => count, value => count = value);
		var executed = Signal();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		parser.CommandListParse(Arg.Any<MarkupString.MarkupText>()).Returns(_ => { observed = count; executed.SetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(mediator: mediator, parser: parser);
		await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think ready"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 99, TimeSpan.FromHours(1), manageSemaphoreCount: true);
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(observed).IsEqualTo(0);
	}

	[Test]
	public async Task RejectedManagedSemaphoreDoesNotWriteCounter()
	{
		var writes = 0;
		await using var queue = Create(global: 0, mediator: CountingMediator(() => 0, _ => writes++));
		var result = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think rejected"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, TimeSpan.Zero, manageSemaphoreCount: true);
		await Assert.That(result.Reason).IsEqualTo(QueueRejectionReason.GlobalLimit);
		await Assert.That(writes).IsEqualTo(0);
	}

	[Test]
	public async Task FailedScheduleRollsBackBeforeTheNextCounterMutation()
	{
		var count = 4;
		var publishing = Signal();
		var fail = Signal();
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			publishing.SetResult();
			await fail.Task;
			return await Task.FromException<DateTimeOffset>(new InvalidOperationException("Schedule failed"));
		});
		await using var queue = Create(mediator: CountingMediator(() => count, value => count = value), scheduler: scheduler);
		var admission = queue.AdmitCommandList(MarkupString.MarkupText.Plain("think rejected"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 99, TimeSpan.Zero, manageSemaphoreCount: true).AsTask();
		await publishing.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var nextMutation = queue.EnterSemaphoreMutationAsync().AsTask();
		await Assert.That(nextMutation.IsCompleted).IsFalse();
		fail.SetResult();
		var rejected = false;
		try { await admission; } catch (InvalidOperationException ex) { rejected = ex.Message == "Schedule failed"; }
		await Assert.That(rejected).IsTrue();
		using var lease = await nextMutation.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(count).IsEqualTo(4);
		count++;
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.That(count).IsEqualTo(5);
	}

	[Test]
	[Arguments(false, false, 1)]
	[Arguments(true, false, 3)]
	[Arguments(false, true, 3)]
	public async Task PrivilegedAllowanceAddsDatabaseCountButRetainsGlobalCeiling(bool wizard, bool power, int allowance)
	{
		var mediator = Substitute.For<IMediator>();
		ConfigureTargets(mediator, wizard, power);
		mediator.Send(Arg.Any<GetObjectCountQuery>(), Arg.Any<CancellationToken>()).Returns(2);
		await using var queue = Create(global: 4, owner: 1, mediator: mediator);
		var state = ParserState.RootFor(new DBRef(10));
		for (var i = 0; i < allowance; i++)
			await Assert.That((await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think pending"), state, TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think rejected"), state, TimeSpan.FromHours(1))).Reason).IsEqualTo(QueueRejectionReason.OwnerLimit);
		if (wizard || power)
		{
			await Assert.That((await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think other"), ParserState.RootFor(new DBRef(11)), TimeSpan.FromHours(1))).Accepted).IsTrue();
			await Assert.That((await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think capped"), ParserState.RootFor(new DBRef(11)), TimeSpan.FromHours(1))).Reason).IsEqualTo(QueueRejectionReason.GlobalLimit);
		}
	}

	[Test]
	public async Task HaltCancellationCallbacksCanReadQueueWithoutBlockingAdmissionLock()
	{
		await using var queue = Create();
		var entered = Signal(); var release = Signal(); var callbackRead = false;
		var job = await queue.AdmitWork(async () =>
		{
			using var registration = ExecutionBudget.CurrentToken.Register(() =>
			{
				callbackRead = Task.Run(() => queue.GetQueueUsage()).Wait(TimeSpan.FromSeconds(2));
			});
			entered.SetResult();
			await release.Task;
			return null;
		}, "callback", "test");
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await queue.HaltByPid(job.Pid!.Value);
			await Assert.That(callbackRead).IsTrue();
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	public async Task SemaphoreAccountingFailureDoesNotDiscardAdmittedAction()
	{
		var mediator = TargetMediator();
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => throw new InvalidOperationException("Transient bookkeeping failure"));
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var executed = Signal();
		parser.CommandListParse(Arg.Any<MarkupString.MarkupText>()).Returns(_ =>
		{
			executed.SetResult();
			return ValueTask.FromResult<CallState?>(null);
		});
		await using var queue = Create(mediator: mediator, parser: parser);
		var job = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think survives"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 1, TimeSpan.FromHours(1));
		await queue.ReleaseScheduledWork(job.Pid!.Value, semaphoreTimeout: true);
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task ReleasingAHaltedDeferredPidDoesNotCountAsAdmissionRejection()
	{
		await using var queue = Create();
		var job = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think ignored"), ParserState.Empty, TimeSpan.FromHours(1));
		await queue.HaltByPid(job.Pid!.Value);
		var result = await queue.ReleaseScheduledWork(job.Pid.Value);
		await Assert.That(result.Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(queue.GetQueueUsage().Rejections.Count).IsEqualTo(0);
	}

	[Test]
	public async Task MillisecondConfigurationPreservesUnlimitedAndCancellationSemantics()
	{
		using var bounded = ExecutionBudget.FromMilliseconds(1500);
		await Assert.That(bounded.Remaining).IsLessThanOrEqualTo(TimeSpan.FromMilliseconds(1500));
		using var cancellation = new CancellationTokenSource();
		using var unlimited = ExecutionBudget.FromMilliseconds(0, cancellation.Token);
		await Assert.That(unlimited.Remaining).IsEqualTo(TimeSpan.MaxValue);
		cancellation.Cancel();
		await Assert.That(unlimited.IsCancelled).IsTrue();
		await Assert.That(unlimited.IsExpired).IsFalse();
		var oversizedRejected = false;
		try { using var invalid = new ExecutionBudget(TimeSpan.MaxValue); }
		catch (ArgumentOutOfRangeException) { oversizedRejected = true; }
		await Assert.That(oversizedRejected).IsTrue();
	}

	[Test]
	public async Task SaturationRejectsWithoutPidAndConsumerCannotDeadlock()
	{
		await using var queue = Create(global: 1);
		var observed = new TaskCompletionSource<QueueAdmissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = Signal();
		var first = await queue.AdmitWork(async () =>
		{
			observed.SetResult(await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "nested", "test"));
			await release.Task;
			return null;
		}, "first", "test");
		try
		{
			var rejection = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(first.Accepted).IsTrue();
			await Assert.That(rejection.Reason).IsEqualTo(QueueRejectionReason.GlobalLimit);
			await Assert.That(rejection.Pid).IsNull();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
			await Assert.That(queue.GetQueueUsage().Rejections[QueueRejectionReason.GlobalLimit]).IsEqualTo(1);
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	public async Task OwnerBudgetIncludesRunningWork()
	{
		await using var queue = Create(global: 10, owner: 1);
		var entered = Signal(); var release = Signal();
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "first", "test");
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var rejected = await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "second", "test");
			await Assert.That(rejected.Reason).IsEqualTo(QueueRejectionReason.OwnerLimit);
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	public async Task CancellationRetainsBoundUntilDequeuedAndSkipsAction()
	{
		await using var queue = Create(global: 3);
		var entered = Signal(); var release = Signal(); var drained = Signal(); var ran = false;
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "first", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var cancelled = await queue.AdmitWork(() => { ran = true; return ValueTask.FromResult<CallState?>(null); }, "cancel", "test");
			await queue.HaltByPid(cancelled.Pid!.Value);
			await queue.AdmitWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "drain", "test");
			var rejected = await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "extra", "test");
			await Assert.That(rejected.Accepted).IsFalse();
		}
		finally { release.TrySetResult(); }
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(ran).IsFalse();
	}

	[Test]
	public async Task HaltRacingDeferredReleaseCannotReactivateRemovedWork()
	{
		var scheduler = Substitute.For<IScheduler>();
		var unscheduling = Signal();
		var unscheduled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			unscheduling.SetResult();
			return unscheduled.Task;
		});
		await using var queue = Create(global: 2, scheduler: scheduler);
		var entered = Signal(); var release = Signal();
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "block", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var deferred = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think ignored"), ParserState.Empty, TimeSpan.FromHours(1));
			var halt = queue.HaltByPid(deferred.Pid!.Value).AsTask();
			await unscheduling.Task.WaitAsync(TimeSpan.FromSeconds(5));
			// Release now serializes behind cancellation's deferred transition. Do not await
			// it before releasing the fake Quartz barrier, which would deadlock the fixture.
			var firing = queue.ReleaseScheduledWork(deferred.Pid.Value).AsTask();
			unscheduled.SetResult(true);
			await Assert.That(await halt.WaitAsync(TimeSpan.FromSeconds(5))).IsTrue();
			await Assert.That((await firing.WaitAsync(TimeSpan.FromSeconds(5))).Reason)
				.IsEqualTo(QueueRejectionReason.AlreadyReleased);
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
			await Assert.That(queue.GetQueueEntry(deferred.Pid.Value)).IsNull();
		}
		finally { unscheduled.TrySetResult(true); release.TrySetResult(); }
	}

	[Test]
	public async Task ImmediateEntriesRetainFifoOrder()
	{
		await using var queue = Create(global: 5);
		var entered = Signal(); var release = Signal(); var drained = Signal(); var order = new List<int>();
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "first", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		for (var i = 0; i < 3; i++)
		{
			var number = i;
			await queue.AdmitWork(() => { order.Add(number); return ValueTask.FromResult<CallState?>(null); }, "fifo", "test");
		}
		await queue.AdmitWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "drain", "test");
		release.SetResult();
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(string.Join(',', order)).IsEqualTo("0,1,2");
	}

	[Test]
	public async Task ShutdownIsObservable()
	{
		var queue = Create(); await queue.DisposeAsync();
		var rejected = await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "late", "test");
		await Assert.That(rejected.Reason).IsEqualTo(QueueRejectionReason.ShuttingDown);
		await Assert.That(rejected.Pid).IsNull();
	}

	[Test]
	public async Task NestedStateCopiesShareDeadlineAndCancellation()
	{
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(TimeSpan.FromHours(1), cancellation.Token);
		var parent = ParserState.Empty with { ExecutionBudget = budget };
		var nested = parent with { Function = "nested" };
		using var scope = budget.Enter();
		await Assert.That(ReferenceEquals(parent.ExecutionBudget, nested.ExecutionBudget)).IsTrue();
		cancellation.Cancel();
		try { await Task.Delay(TimeSpan.FromSeconds(5), ExecutionBudget.CurrentToken); }
		catch (OperationCanceledException) { }
		await Assert.That(nested.ExecutionBudget!.IsExceeded).IsTrue();
		await Assert.That(ExecutionBudget.CurrentToken.IsCancellationRequested).IsTrue();
		await Assert.That(budget.IsExpired).IsFalse();
	}
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task AllInputOriginsReturnCapacityRejection(int origin)
	{
		await using var queue = Create(global: 1);
		var entered = Signal(); var release = Signal();
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "first", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var command = MarkupString.MarkupText.Plain("think queued");
			var result = origin switch
			{
				0 => await queue.AdmitUserCommand(20, command, ParserState.Empty),
				1 => await queue.AdmitCommandList(command, ParserState.Empty),
				2 => await queue.AdmitCommandList(command, ParserState.Empty, TimeSpan.FromHours(1)),
				_ => await queue.AdmitCommandList(command, ParserState.Empty, new DbRefAttribute(new DBRef(1), ["SEMAPHORE"]), 0)
			};
			await Assert.That(result.Reason).IsEqualTo(QueueRejectionReason.GlobalLimit);
			await Assert.That(result.Pid).IsNull();
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	public async Task DeferredTransferReusesReservationAndPidAtCapacity()
	{
		await using var queue = Create(global: 1);
		var delayed = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think delayed"), ParserState.Empty, TimeSpan.FromHours(1));
		await Assert.That(delayed.Accepted).IsTrue();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		var released = await queue.ReleaseScheduledWork(delayed.Pid!.Value);
		await Assert.That(released.Pid).IsEqualTo(delayed.Pid);
		await Assert.That(released.Accepted).IsTrue();
		await Assert.That(queue.GetQueueUsage().Rejections.Count).IsEqualTo(0);
	}

	[Test]
	public async Task RegexTimeoutIsClampedToRemainingExecutionBudget()
	{
		using var budget = new ExecutionBudget(TimeSpan.FromMilliseconds(80));
		using (budget.Enter())
		{
			var regex = SoftcodeRegex.Create("(a+)+$", RegexOptions.None);
			await Assert.That(regex.MatchTimeout).IsLessThanOrEqualTo(TimeSpan.FromMilliseconds(80));
			try { regex.IsMatch(new string('a', 10000) + "!"); }
			catch (RegexMatchTimeoutException) { }
		}
		using var unrelated = new ExecutionBudget(TimeSpan.FromSeconds(5));
		using (unrelated.Enter()) await Assert.That(SoftcodeRegex.Create("ok", RegexOptions.None).IsMatch("ok")).IsTrue();
	}

	[Test]
	public async Task BareExecutorIsPinnedToItsIncarnationBeforeQueueing()
	{
		SharpPlayer player = new()
		{
			Object = new SharpObject
			{
				Key = 50, CreationTime = 1, Name = "Owner", Type = "PLAYER", Locks = null!, Owner = null!,
				Powers = new(() => AsyncEnumerable.Empty<SharpPower>()), Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!,
				Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()), Parent = null!, Zone = null!, Children = null!
			},
			Location = null!, Home = null!, PasswordHash = "", Quota = 0
		};
		player.Object.Owner = new(_ => Task.FromResult(player));
		var replaced = false;
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
		 ValueTask.FromResult<AnyOptionalSharpObject>(replaced ? new None() : player));
		var parser = Substitute.For<IMUSHCodeParser>();
		await using var queue = Create(global: 4, mediator: mediator, parser: parser);
		var entered = Signal(); var release = Signal(); var drained = Signal();
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "block", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var accepted = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think old"), ParserState.Empty with { Executor = new DBRef(50) });
			await Assert.That(accepted.Accepted).IsTrue();
			replaced = true;
			await queue.AdmitWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "done", "test");
		}
		finally { release.TrySetResult(); }
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(parser.ReceivedCalls().Any()).IsFalse();
		await mediator.Received().Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.CreationMilliseconds == 1), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task SlowHttpIsCancelledBeforeTheNextJobRuns()
	{
		using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
		listener.Start();
		using var client = new HttpClient();
		await using var queue = Create(global: 2);
		var completed = Signal(); var cancelled = false;
		var endpoint = (System.Net.IPEndPoint)listener.LocalEndpoint;
		await queue.AdmitWork(async () =>
		{
			try { using var response = await client.GetAsync($"http://127.0.0.1:{endpoint.Port}/", ExecutionBudget.CurrentToken); }
			catch (OperationCanceledException) { cancelled = true; throw; }
			return null;
		}, "http", "test");
		using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
		await queue.AdmitWork(() => { completed.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "next", "test");
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(cancelled).IsTrue();
	}

	[Test]
	public async Task RejectedAttributeNeverStartsItsExternalCallback()
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<AnyOptionalSharpObject>(new None()));
		await using var queue = Create(mediator: mediator);
		var called = false;
		var result = await queue.AdmitAsyncAttribute(() => { called = true; return ValueTask.FromResult(ParserState.Empty); },
		 new DbRefAttribute(new DBRef(123), ["CALLBACK"]));
		await Assert.That(result.Reason).IsEqualTo(QueueRejectionReason.InvalidTarget);
		await Assert.That(result.Pid).IsNull();
		await Assert.That(called).IsFalse();
	}

	[Test]
	[Arguments("#50/SEMAPHORE")]
	[Arguments("#50:123456/SEMAPHORE")]
	[Arguments("#50/ATTR_#1")]
	public async Task SemaphoreIdentityRoundTrips(string identity)
	{
		await Assert.That(DbRefAttribute.TryParse(identity, out var parsed)).IsTrue();
		await Assert.That(parsed!.Value.ToString()).IsEqualTo(identity);
	}

	[Test]
	public async Task DeferredWaitConsumesQuotaButStartsANewBudgetOnlyOnExecution()
	{
		using var submittingCancellation = new CancellationTokenSource();
		using var submittingBudget = new ExecutionBudget(TimeSpan.FromHours(1), submittingCancellation.Token);
		var executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		parser.CommandListParse(Arg.Any<MarkupString.MarkupText>()).Returns(_ =>
		{
			var active = ExecutionBudget.Current;
			executed.TrySetResult(active is not null && !ReferenceEquals(active, submittingBudget) && !active.IsExceeded);
			return ValueTask.FromResult<CallState?>(null);
		});
		await using var queue = Create(global: 1, parser: parser);
		var admission = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think deferred"),
		 ParserState.Empty with { ExecutionBudget = submittingBudget }, TimeSpan.FromHours(1));
		submittingCancellation.Cancel();
		await Assert.That(submittingBudget.IsExceeded).IsTrue();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(executed.Task.IsCompleted).IsFalse();
		await queue.ReleaseScheduledWork(admission.Pid!.Value);
		await Assert.That(await executed.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsTrue();
	}

}
