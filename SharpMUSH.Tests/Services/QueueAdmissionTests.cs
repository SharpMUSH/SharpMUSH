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
	private static Scheduler Create(uint global = 2, uint owner = 10, IMediator? mediator = null, IMUSHCodeParser? parser = null, IScheduler? scheduler = null)
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Limit = config.Limit with { GlobalQueueLimit = global, PlayerQueueLimit = owner, QueueEntryCpuTime = 1000 } });
		var factory = Substitute.For<ISchedulerFactory>();
		if (scheduler is not null) factory.GetScheduler().Returns(scheduler);
		return new(parser ?? Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
		 factory, Substitute.For<IAttributeService>(), mediator ?? TargetMediator(),
		 NullLogger<Scheduler>.Instance, options);
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
	public async Task DrainDoesNotCancelWorkWhoseTimeoutAlreadyPublishedIt()
	{
		var scheduler = Substitute.For<IScheduler>();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var executed = Signal();
		parser.CommandListParse(Arg.Any<MarkupString.MarkupText>()).Returns(_ => { executed.SetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(global: 3, scheduler: scheduler, parser: parser);
		var blocked = Signal(); var release = Signal();
		await queue.EnqueueWork(async () => { blocked.SetResult(); await release.Task; return null; }, "block", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var semaphore = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
			var pending = await queue.WriteCommandList(MarkupString.MarkupText.Plain("think timeout"), ParserState.Empty, semaphore, 0);
			var key = new TriggerKey($"dbref:-{pending.Pid}", $"semaphore:{semaphore}");
			scheduler.GetTriggerKeys(Arg.Any<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>()).Returns(new[] { key });
			await queue.ReleaseScheduledWork(pending.Pid!.Value, semaphoreTimeout: true);
			using (await queue.EnterSemaphoreMutationAsync()) await queue.Drain(semaphore);
		}
		finally { release.SetResult(); }
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task ManagedWaitDoesNotExposeReservationBeforeCounterLease()
	{
		var count = 2;
		await using var queue = Create(mediator: CountingMediator(() => count, value => count = value));
		var lease = await queue.EnterSemaphoreMutationAsync();
		Task<QueueAdmissionResult>? pending = null;
		try
		{
			pending = queue.WriteCommandList(MarkupString.MarkupText.Plain("think pending"), ParserState.Empty,
				new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 2, TimeSpan.FromHours(1), manageSemaphoreCount: true).AsTask();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		}
		finally
		{
			lease.Dispose();
			if (pending is not null)
			{
				var admitted = await pending;
				await queue.HaltByPid(admitted.Pid!.Value);
			}
		}
		await Assert.That(count).IsEqualTo(2);
	}

	private static IMediator CountingMediator(Func<int> read, Action<int> write)
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
		var result = await queue.WriteCommandList(MarkupString.MarkupText.Plain("think ready"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 99, TimeSpan.Zero, manageSemaphoreCount: true);
		await firing!.WaitAsync(TimeSpan.FromSeconds(5));
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(result.Accepted).IsTrue();
		await Assert.That(observedAtPublication).IsEqualTo(1);
		await Assert.That(count).IsEqualTo(0);
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
		await queue.WriteCommandList(MarkupString.MarkupText.Plain("think ready"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 99, TimeSpan.FromHours(1), manageSemaphoreCount: true);
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(observed).IsEqualTo(0);
	}

	[Test]
	public async Task RejectedManagedSemaphoreDoesNotWriteCounter()
	{
		var writes = 0;
		await using var queue = Create(global: 0, mediator: CountingMediator(() => 0, _ => writes++));
		var result = await queue.WriteCommandList(MarkupString.MarkupText.Plain("think rejected"), ParserState.Empty,
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
		var admission = queue.WriteCommandList(MarkupString.MarkupText.Plain("think rejected"), ParserState.Empty,
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
			await Assert.That((await queue.WriteCommandList(MarkupString.MarkupText.Plain("think pending"), state, TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.WriteCommandList(MarkupString.MarkupText.Plain("think rejected"), state, TimeSpan.FromHours(1))).Reason).IsEqualTo(QueueRejectionReason.OwnerLimit);
		if (wizard || power)
		{
			await Assert.That((await queue.WriteCommandList(MarkupString.MarkupText.Plain("think other"), ParserState.RootFor(new DBRef(11)), TimeSpan.FromHours(1))).Accepted).IsTrue();
			await Assert.That((await queue.WriteCommandList(MarkupString.MarkupText.Plain("think capped"), ParserState.RootFor(new DBRef(11)), TimeSpan.FromHours(1))).Reason).IsEqualTo(QueueRejectionReason.GlobalLimit);
		}
	}

	[Test]
	public async Task HaltCancellationCallbacksCanReadQueueWithoutBlockingAdmissionLock()
	{
		await using var queue = Create();
		var entered = Signal(); var release = Signal(); var callbackRead = false;
		var job = await queue.EnqueueWork(async () =>
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
		var job = await queue.WriteCommandList(MarkupString.MarkupText.Plain("think survives"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 1, TimeSpan.FromHours(1));
		await queue.ReleaseScheduledWork(job.Pid!.Value, semaphoreTimeout: true);
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task ReleasingAHaltedDeferredPidDoesNotCountAsAdmissionRejection()
	{
		await using var queue = Create();
		var job = await queue.WriteCommandList(MarkupString.MarkupText.Plain("think ignored"), ParserState.Empty, TimeSpan.FromHours(1));
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
		var first = await queue.EnqueueWork(async () =>
		{
			observed.SetResult(await queue.EnqueueWork(() => ValueTask.FromResult<CallState?>(null), "nested", "test"));
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
		await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "first", "test");
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var rejected = await queue.EnqueueWork(() => ValueTask.FromResult<CallState?>(null), "second", "test");
			await Assert.That(rejected.Reason).IsEqualTo(QueueRejectionReason.OwnerLimit);
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	public async Task CancellationRetainsBoundUntilDequeuedAndSkipsAction()
	{
		await using var queue = Create(global: 3);
		var entered = Signal(); var release = Signal(); var drained = Signal(); var ran = false;
		await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "first", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var cancelled = await queue.EnqueueWork(() => { ran = true; return ValueTask.FromResult<CallState?>(null); }, "cancel", "test");
			await queue.HaltByPid(cancelled.Pid!.Value);
			await queue.EnqueueWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "drain", "test");
			var rejected = await queue.EnqueueWork(() => ValueTask.FromResult<CallState?>(null), "extra", "test");
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
		await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "block", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var deferred = await queue.WriteCommandList(MarkupString.MarkupText.Plain("think ignored"), ParserState.Empty, TimeSpan.FromHours(1));
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
		await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "first", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		for (var i = 0; i < 3; i++)
		{
			var number = i;
			await queue.EnqueueWork(() => { order.Add(number); return ValueTask.FromResult<CallState?>(null); }, "fifo", "test");
		}
		await queue.EnqueueWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "drain", "test");
		release.SetResult();
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(string.Join(',', order)).IsEqualTo("0,1,2");
	}

	[Test]
	public async Task ShutdownIsObservable()
	{
		var queue = Create(); await queue.DisposeAsync();
		var rejected = await queue.EnqueueWork(() => ValueTask.FromResult<CallState?>(null), "late", "test");
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
		await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "first", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var command = MarkupString.MarkupText.Plain("think queued");
			var result = origin switch
			{
				0 => await queue.WriteUserCommand(20, command, ParserState.Empty),
				1 => await queue.WriteCommandList(command, ParserState.Empty),
				2 => await queue.WriteCommandList(command, ParserState.Empty, TimeSpan.FromHours(1)),
				_ => await queue.WriteCommandList(command, ParserState.Empty, new DbRefAttribute(new DBRef(1), ["SEMAPHORE"]), 0)
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
		var delayed = await queue.WriteCommandList(MarkupString.MarkupText.Plain("think delayed"), ParserState.Empty, TimeSpan.FromHours(1));
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
		await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "block", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var accepted = await queue.WriteCommandList(MarkupString.MarkupText.Plain("think old"), ParserState.Empty with { Executor = new DBRef(50) });
			await Assert.That(accepted.Accepted).IsTrue();
			replaced = true;
			await queue.EnqueueWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "done", "test");
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
		await queue.EnqueueWork(async () =>
		{
			try { using var response = await client.GetAsync($"http://127.0.0.1:{endpoint.Port}/", ExecutionBudget.CurrentToken); }
			catch (OperationCanceledException) { cancelled = true; throw; }
			return null;
		}, "http", "test");
		using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
		await queue.EnqueueWork(() => { completed.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "next", "test");
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
		var result = await queue.WriteAsyncAttribute(() => { called = true; return ValueTask.FromResult(ParserState.Empty); },
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
		var admission = await queue.WriteCommandList(MarkupString.MarkupText.Plain("think deferred"),
		 ParserState.Empty with { ExecutionBudget = submittingBudget }, TimeSpan.FromHours(1));
		submittingCancellation.Cancel();
		await Assert.That(submittingBudget.IsExceeded).IsTrue();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(executed.Task.IsCompleted).IsFalse();
		await queue.ReleaseScheduledWork(admission.Pid!.Value);
		await Assert.That(await executed.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsTrue();
	}

}
