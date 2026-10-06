using SharpMUSH.Library.Authorization;
using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Utilities;
using System.Collections.Concurrent;
using System.Text;
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
	private static Scheduler Create(uint global = 2, uint owner = 10, IMediator? mediator = null, IMUSHCodeParser? parser = null, IScheduler? scheduler = null, uint milliseconds = 1000, QueueDiagnosticsRecorder? diagnostics = null, IConnectionService? connections = null, INotifyService? notifications = null, uint burst = LimitOptions.DefaultCommandBurstSize, bool ownerQueues = false, Func<bool>? ownerQueuesNow = null)
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(_ => config with
		{
			Limit = config.Limit with { GlobalQueueLimit = global, PlayerQueueLimit = owner, QueueEntryCpuTime = milliseconds, CommandBurstSize = burst },
			Command = config.Command with { OwnerQueues = ownerQueuesNow?.Invoke() ?? ownerQueues }
		});
		var factory = Substitute.For<ISchedulerFactory>();
		if (scheduler is not null) factory.GetScheduler().Returns(scheduler);
		return new(parser ?? Substitute.For<IMUSHCodeParser>(), connections ?? Substitute.For<IConnectionService>(),
		 factory, Substitute.For<IAttributeService>(), mediator ?? TargetMediator(),
		 NullLogger<Scheduler>.Instance, options, notifications, diagnostics: diagnostics);
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
					Grants = new(_ => Task.FromResult(ObjectGrants.None)),
					Powers = new(() => AsyncEnumerable.Empty<SharpPower>()), Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!,
					Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()), Parent = null!, Zone = null!, Children = null!
				},
				Location = null!, Home = null!, PasswordHash = "", Quota = 0
			};
			player.Object.Owner = new(_ => Task.FromResult(player));
			new AnySharpObject(player).Grant(wizard ? ["wizard"] : null, queuePower ? ["Queue"] : null);
			return ValueTask.FromResult<AnyOptionalSharpObject>(player);
		});
	}
	private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	[Test]
	public async Task UncertainHaltCompletesItsObservationOnceAfterSettlement()
	{
		var count = 3;
		var writes = 0;
		var diagnostics = new QueueDiagnosticsRecorder();
		var mediator = CountingMediator(() => count, value =>
		{
			count = value;
			if (++writes == 1) throw new IOException("counter committed without acknowledgement");
		});
		await using var queue = Create(mediator: mediator, diagnostics: diagnostics, scheduler: Substitute.For<IScheduler>());
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0);
		await Assert.ThrowsAsync<IOException>(async () => await queue.HaltByPid(admission.Pid!.Value));
		await Assert.That(diagnostics.Recent().Count).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await queue.HaltByPid(admission.Pid!.Value);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		// The settling halt hands the cancelled entry to the consumer, which races this halt to release
		// it. The quota is released under the admission lock either way, but when the consumer wins, it
		// completes the observation after HaltByPid has returned (#1251). The consumer is sequential, so
		// once the sentinel has run, the halted entry has been released and observed.
		var drained = Signal();
		await queue.AdmitWork(() => { drained.TrySetResult(); return ValueTask.FromResult<CallState?>(null); }, "sentinel", "test");
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(diagnostics.Recent().Single(row => row.Pid == admission.Pid).Outcome).IsEqualTo(QueueOutcome.Cancelled);
		await Assert.That(count).IsEqualTo(2);
		await Assert.That(writes).IsEqualTo(1);
	}

	[Test]
	public async Task AbandoningReservedCommandClosesTheDiagnosticObservation()
	{
		var diagnostics = new QueueDiagnosticsRecorder();
		await using var queue = Create(diagnostics: diagnostics);
		using var reservation = await queue.ReserveCommandList(MarkupText.Plain("think unpublished"), ParserState.RootFor(new DBRef(10)));
		await Assert.That(reservation.Admission.Accepted).IsTrue();
		reservation.Dispose();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.That(diagnostics.Recent().Count).IsEqualTo(1);
		await Assert.That(diagnostics.Recent().Single().Outcome).IsEqualTo(QueueOutcome.Cancelled);
	}

	[Test]
	public async Task ManagedDrainCompletesTheDiagnosticObservation()
	{
		var diagnostics = new QueueDiagnosticsRecorder();
		await using var queue = Create(diagnostics: diagnostics, scheduler: Substitute.For<IScheduler>());
		var semaphore = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
		await queue.AdmitCommandList(MarkupText.Plain("think pending"), ParserState.Empty, semaphore, 0);
		using (await queue.EnterSemaphoreMutationAsync())
			await Assert.That(await queue.ApplySemaphoreCommandAsync(semaphore, null, true,
				_ => ValueTask.CompletedTask, () => ValueTask.FromResult(false))).IsEqualTo(1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.That(diagnostics.Recent().Single().Outcome).IsEqualTo(QueueOutcome.Cancelled);
	}

	[Test]
	public async Task ExpiredCompoundLockDoesNotStartTheNextLegacyRead()
	{
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(10), cancellation.Token);
		using var scope = budget.Enter();
		var target = new TestObjectFactory().CreatePlayer(10, "lock target");
		var services = Substitute.For<ILockEvaluationServices>();
		services.EvaluateAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "LEFT")
			.Returns(_ => { cancellation.Cancel(); return ValueTask.FromResult<LockEvaluation>("yes"); });
		var reads = 0;
		services.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<IAttributeService.AttributeMode>(), Arg.Any<bool>())
			.Returns(_ => { reads++; return ValueTask.FromResult<OptionalSharpAttributeOrError>(new None()); });
		using var cache = new ZiggyCreatures.Caching.Fusion.FusionCache(new ZiggyCreatures.Caching.Fusion.FusionCacheOptions());
		var parser = new SharpMUSH.Implementation.BooleanExpressionParser(services, Substitute.For<IMediator>(), cache);
		bool cancelled = false;
		try { await parser.Compile("LEFT/yes & RIGHT:value")(target, target); }
		catch (OperationCanceledException) { cancelled = true; }
		await Assert.That(cancelled).IsTrue();
		await Assert.That(reads).IsEqualTo(0);
	}

	[Test]
	public async Task LockBindingReleasesTheConsumerOnCancellation()
	{
		var target = new TestObjectFactory().CreatePlayer(10, "lock setter");
		var services = Substitute.For<ILockEvaluationServices>();
		var release = new TaskCompletionSource<AnyOptionalSharpObjectOrError>(TaskCreationOptions.RunContinuationsAsynchronously);
		services.LocateAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "target", Arg.Any<LocateFlags>())
			.Returns(_ => new ValueTask<AnyOptionalSharpObjectOrError>(release.Task));
		using var cache = new ZiggyCreatures.Caching.Fusion.FusionCache(new ZiggyCreatures.Caching.Fusion.FusionCacheOptions());
		var parser = new SharpMUSH.Implementation.BooleanExpressionParser(services, Substitute.For<IMediator>(), cache);
		using var cancellation = new CancellationTokenSource();
		var pending = parser.BindAsync("target", target, cancellation.Token).AsTask();
		try
		{
			await Assert.That(pending.IsCompleted).IsFalse();
			await cancellation.CancelAsync();
			await Assert.That(async () => await pending).Throws<OperationCanceledException>();
		}
		finally
		{
			release.TrySetResult(new None());
		}
	}

	[Test]
	[Arguments("FLAG^WIZARD")]
	[Arguments("POWER^QUEUE")]
	[Arguments("$#10")]
	[Arguments("CHANNEL^test")]
	[Arguments("@#10")]
	[Arguments("+#11")]
	[Arguments("#11")]
	[Arguments("TEST:value")]
	public async Task CompiledLockReadsReleaseTheConsumerOnExpiry(string expression)
	{
		var target = new TestObjectFactory().CreatePlayer(10, "lock target");
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		async Task Block(CancellationToken token)
		{
			entered.TrySetResult(token);
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
		}
		async IAsyncEnumerable<T> Stream<T>([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
		{
			await Block(token);
			yield break;
		}
		var targetPlayer = target.Expect<SharpPlayer>();
		target.Object().Flags = new(() => Stream<SharpObjectFlag>());
		target.Object().Powers = new(() => Stream<SharpPower>());
		target.Object().Grants = new(async token => { await Block(token); return ObjectGrants.None; });
		target.Object().Owner = new(async token => { await Block(token); return targetPlayer; });
		var mediator = Substitute.For<IMediator>();
		async ValueTask<AnyOptionalSharpObject> ObjectRead(CancellationToken token) { await Block(token); return target; }
		async ValueTask<bool> ChannelRead(CancellationToken token) { await Block(token); return true; }
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(c => ObjectRead(c.Arg<CancellationToken>()));
		mediator.Send(Arg.Any<SharpMUSH.Library.Queries.IsOnChannelQuery>(), Arg.Any<CancellationToken>()).Returns(c => ChannelRead(c.Arg<CancellationToken>()));
		mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>()).Returns(c => Stream<AnySharpContent>(c.Arg<CancellationToken>()));
		using var cache = new ZiggyCreatures.Caching.Fusion.FusionCache(new ZiggyCreatures.Caching.Fusion.FusionCacheOptions());
		var services = Substitute.For<ILockEvaluationServices>();
		async ValueTask<OptionalSharpAttributeOrError> AttributeRead() { await Block(CancellationToken.None); return new None(); }
		services.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<IAttributeService.AttributeMode>(), Arg.Any<bool>()).Returns(_ => AttributeRead());
		var parser = new SharpMUSH.Implementation.BooleanExpressionParser(services, mediator, cache);
		var compiled = parser.Compile(expression);
		await using var queue = Create(global: 10, milliseconds: 200);
		var following = Signal();
		bool cancelled = false;
		await queue.AdmitWork(async () =>
		{
			try { await compiled(target, target); }
			catch (OperationCanceledException) { cancelled = true; throw; }
			return null;
		}, "lock", "test");
		await queue.AdmitWork(() => { following.TrySetResult(); return ValueTask.FromResult<CallState?>(null); }, "following", "test");
		try
		{
			var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await following.Task.WaitAsync(TimeSpan.FromSeconds(3));
			if (expression != "TEST:value") await Assert.That(token.CanBeCanceled).IsTrue();
			await Assert.That(cancelled).IsTrue();
		}
		finally
		{
			cleanup.Cancel();
			await following.Task.WaitAsync(TimeSpan.FromSeconds(3));
		}

		if (expression == "FLAG^WIZARD")
		{
			target.Grant(roles: ["wizard"]);
			using var fresh = new ExecutionBudget(TimeSpan.FromSeconds(3));
			using var scope = fresh.Enter();
			await Assert.That(await compiled(target, target)).IsTrue();
			await Assert.That(ReferenceEquals(compiled, parser.Compile(expression))).IsTrue();
		}
	}

	[Test]
	[Arguments("read", true)]
	[Arguments("write", true)]
	[Arguments("read", false)]
	[Arguments("write", false)]
	public async Task DirectSemaphoreAdmissionHasShutdownLinkedFiniteTransaction(string stage, bool shutdown)
	{
		var entered = Signal();
		using var release = new CancellationTokenSource();
		var count = 0;
		var mediator = CountingMediator(() => count, value => count = value);
		async Task Block(CancellationToken token)
		{
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
		}
		async IAsyncEnumerable<SharpAttribute> Read([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
		{
			await Block(token);
			yield break;
		}
		if (stage == "read")
			mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(call => Read(call.Arg<CancellationToken>()));
		else
		{
			var writes = 0;
			mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>())
				.Returns(async ValueTask<bool> (call) =>
				{
					if (Interlocked.Increment(ref writes) == 1) await Block(call.Arg<CancellationToken>());
					count = int.Parse(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText());
					return true;
				});
		}
		var queue = Create(mediator: mediator, milliseconds: 0);
		var admission = queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, TimeSpan.FromHours(1), true).AsTask();
		Task? stopping = null;
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			if (shutdown) stopping = queue.DisposeAsync().AsTask();
			await Assert.That(async () => await admission.WaitAsync(TimeSpan.FromSeconds(3))).Throws<OperationCanceledException>();
			if (stopping is not null) await stopping.WaitAsync(TimeSpan.FromSeconds(2));
			await Assert.That(count).IsEqualTo(0);
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		}
		finally
		{
			release.Cancel();
			try { await admission; } catch (OperationCanceledException) { }
			if (stopping is not null) await stopping;
			else await queue.DisposeAsync();
		}
	}

	[Test]
	[Arguments("executor", false)]
	[Arguments("enactor", false)]
	[Arguments("caller", false)]
	[Arguments("executor", true)]
	[Arguments("enactor", true)]
	[Arguments("caller", true)]
	public async Task ParserIdentityReadsReceiveConsumerCancellation(string identity, bool known)
	{
		var mediator = Substitute.For<IMediator>();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		async ValueTask<AnyOptionalSharpObject> Block(CancellationToken token)
		{
			entered.TrySetResult(token);
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
			return new None();
		}
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Block(call.Arg<CancellationToken>()));
		await using var queue = Create(global: 10, milliseconds: 200);
		var following = Signal();
		await queue.AdmitWork(async () =>
		{
			var state = ParserState.RootFor(new DBRef(10));
			switch (identity, known)
			{
				case ("executor", false): await state.ExecutorObject(mediator); break;
				case ("enactor", false): await state.EnactorObject(mediator); break;
				case ("caller", false): await state.CallerObject(mediator); break;
				case ("executor", true): await state.KnownExecutorObject(mediator); break;
				case ("enactor", true): await state.KnownEnactorObject(mediator); break;
				case ("caller", true): await state.KnownCallerObject(mediator); break;
			}
			return null;
		}, "identity", "test");
		await queue.AdmitWork(() => { following.TrySetResult(); return ValueTask.FromResult<CallState?>(null); }, "following", "test");
		try
		{
			await Assert.That((await entered.Task.WaitAsync(TimeSpan.FromSeconds(3))).CanBeCanceled).IsTrue();
			await following.Task.WaitAsync(TimeSpan.FromSeconds(3));
		}
		finally
		{
			cleanup.Cancel();
			await following.Task.WaitAsync(TimeSpan.FromSeconds(3));
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AdmissionPrivilegeStreamsReceiveTheConsumerExecutionToken(bool powers)
	{
		var target = new TestObjectFactory().CreateThing(10, "target");
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		async IAsyncEnumerable<T> Block<T>([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
		{
			entered.TrySetResult(token);
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
			yield break;
		}
		// Powers and the privilege flags are read through the object's grants.
		if (powers) target.Object().Grants = new(async token => { await Block<bool>(token).ToListAsync(token); return ObjectGrants.None; });
		else target.Object().Flags = new(() => Block<SharpObjectFlag>());
		var mediator = TargetMediator();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObject>(target));
		await using var queue = Create(global: 10, mediator: mediator, milliseconds: 200);
		var following = Signal();
		var ran = false;
		await queue.AdmitWork(async () =>
		{
			await queue.AdmitWork(() => { ran = true; return ValueTask.FromResult<CallState?>(null); }, "nested", "test", new DBRef(10, 1));
			return null;
		}, "outer", "test");
		await queue.AdmitWork(() => { following.TrySetResult(); return ValueTask.FromResult<CallState?>(null); }, "following", "test");
		try
		{
			var observed = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await Assert.That(observed.CanBeCanceled).IsTrue();
			await following.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await Assert.That(ran).IsFalse();
		}
		finally
		{
			cleanup.Cancel();
			await following.Task.WaitAsync(TimeSpan.FromSeconds(3));
		}

	}

	/// <summary>
	/// PennMUSH reports an entry that runs out of <c>queue_entry_cpu_time</c> to its ENACTOR, as
	/// "CPU usage exceeded." (<c>src/parse.c:2083-2084</c>), and the notice has its own bounded budget
	/// here so it is not cancelled with the work it reports on.
	/// </summary>
	[Test]
	public async Task ExecutionLimitNoticeGetsItsOwnBoundedBudget()
	{
		var notifications = Substitute.For<INotifyService>();
		var reported = new TaskCompletionSource<(DBRef Who, bool Cancelled, TimeSpan Remaining)>(TaskCreationOptions.RunContinuationsAsynchronously);
		notifications.NotifyLocalized(Arg.Any<DBRef>(), "CpuUsageExceeded", Arg.Any<AnySharpObject?>(), Arg.Any<object[]>())
			.Returns(call =>
			{
				reported.TrySetResult((call.ArgAt<DBRef>(0),
					ExecutionBudget.CurrentToken.IsCancellationRequested, ExecutionBudget.Current?.Remaining ?? TimeSpan.MaxValue));
				return ValueTask.CompletedTask;
			});
		await using var queue = Create(milliseconds: 10, notifications: notifications);
		await Assert.That((await queue.AdmitWork(async () =>
		{
			await Task.Delay(Timeout.Infinite, ExecutionBudget.CurrentToken);
			return CallState.Empty;
		}, "expired", "test", new DBRef(10, 1))).Accepted).IsTrue();
		var result = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(result.Who.Number).IsEqualTo(10);
		await Assert.That(result.Cancelled).IsFalse();
		await Assert.That(result.Remaining > TimeSpan.Zero && result.Remaining <= TimeSpan.FromSeconds(1)).IsTrue();
		await notifications.DidNotReceive().Notify(Arg.Any<long>(), Arg.Any<SharpMessage>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}

	/// <summary>
	/// The notice goes to the queue entry's enactor rather than to its executor's owner, and an enactor
	/// that is QUIET does not hear it: <c>if (GoodObject(enactor) &amp;&amp; !Quiet(enactor))</c>
	/// (<c>src/parse.c:2083</c>).
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ExecutionLimitNoticeGoesToTheEnactorUnlessQuiet(bool quiet)
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var dbRef = call.Arg<GetObjectNodeQuery>().DBRef;
			var flags = quiet && dbRef.Number == 12
				? new[] { new SharpObjectFlag { Name = "QUIET", Symbol = "Q", System = true, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = [] } }
				: [];
			var player = new SharpPlayer
			{
				Object = new SharpObject
				{
					Key = dbRef.Number, CreationTime = 1, Name = $"P{dbRef.Number}", Type = "PLAYER", Locks = null!, Owner = null!,
					Grants = new(_ => Task.FromResult(ObjectGrants.None)),
					Powers = new(() => Array.Empty<SharpPower>().ToAsyncEnumerable()), Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!,
					Flags = new(() => flags.ToAsyncEnumerable()), Parent = null!, Zone = null!, Children = null!
				},
				Location = null!, Home = null!, PasswordHash = "", Quota = 0
			};
			player.Object.Owner = new(_ => Task.FromResult(player));
			return ValueTask.FromResult<AnyOptionalSharpObject>(player);
		});
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		static async ValueTask<CallState?> Spin()
		{
			await Task.Delay(Timeout.Infinite, ExecutionBudget.CurrentToken);
			return null;
		}
		parser.CommandListParse(Arg.Any<MString>()).Returns(_ => Spin());
		var notifications = Substitute.For<INotifyService>();
		var finished = Signal();
		await using var queue = Create(milliseconds: 10, mediator: mediator, parser: parser, notifications: notifications);
		notifications.NotifyLocalized(Arg.Any<DBRef>(), "CpuUsageExceeded", Arg.Any<AnySharpObject?>(), Arg.Any<object[]>())
			.Returns(_ => { finished.TrySetResult(); return ValueTask.CompletedTask; });

		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("spin"),
			ParserState.RootFor(new DBRef(10)) with { Enactor = new DBRef(12) })).Accepted).IsTrue();
		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(5));
		if (!quiet) await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

		var told = notifications.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.NotifyLocalized)
				&& call.GetArguments()[1] as string == "CpuUsageExceeded")
			.Select(call => ((DBRef)call.GetArguments()[0]!).Number)
			.ToList();
		await Assert.That(told).IsEquivalentTo(quiet ? Array.Empty<int>() : [12]);
	}

	/// <summary>
	/// A delayed entry (<c>@wait</c>) and an attribute callback (<c>@http</c>, <c>@mapsql</c>) keep the
	/// enactor they were queued with, so the notice reaches it rather than the executor.
	/// </summary>
	[Test]
	[Arguments("delayed")]
	[Arguments("attribute")]
	public async Task ExecutionLimitNoticeKeepsTheEnactorOnEveryAdmissionPath(string path)
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var dbRef = call.Arg<GetObjectNodeQuery>().DBRef;
			var player = new SharpPlayer
			{
				Object = new SharpObject
				{
					Key = dbRef.Number, CreationTime = 1, Name = $"P{dbRef.Number}", Type = "PLAYER", Locks = null!, Owner = null!,
					Grants = new(_ => Task.FromResult(ObjectGrants.None)),
					Powers = new(() => Array.Empty<SharpPower>().ToAsyncEnumerable()), Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!,
					Flags = new(() => Array.Empty<SharpObjectFlag>().ToAsyncEnumerable()), Parent = null!, Zone = null!, Children = null!
				},
				Location = null!, Home = null!, PasswordHash = "", Quota = 0
			};
			player.Object.Owner = new(_ => Task.FromResult(player));
			return ValueTask.FromResult<AnyOptionalSharpObject>(player);
		});
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		static async ValueTask<CallState?> Spin()
		{
			await Task.Delay(Timeout.Infinite, ExecutionBudget.CurrentToken);
			return null;
		}
		parser.CommandListParse(Arg.Any<MString>()).Returns(_ => Spin());
		var notifications = Substitute.For<INotifyService>();
		var told = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var quartz = Substitute.For<IScheduler>();
		long generation = 0;
		quartz.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				generation = (long)call.Arg<IJobDetail>().JobDataMap["Generation"];
				return Task.FromResult(call.Arg<ITrigger>().StartTimeUtc);
			});
		await using var queue = Create(milliseconds: 10, mediator: mediator, parser: parser, notifications: notifications, scheduler: quartz);
		notifications.NotifyLocalized(Arg.Any<DBRef>(), "CpuUsageExceeded", Arg.Any<AnySharpObject?>(), Arg.Any<object[]>())
			.Returns(call => { told.TrySetResult(call.ArgAt<DBRef>(0).Number); return ValueTask.CompletedTask; });
		var state = ParserState.RootFor(new DBRef(10)) with { Enactor = new DBRef(12) };

		if (path == "delayed")
		{
			var delayed = await queue.AdmitCommandList(MarkupText.Plain("spin"), state, TimeSpan.FromHours(1));
			await Assert.That(delayed.Accepted).IsTrue();
			// What Quartz's DelayedTask does when the trigger fires.
			await queue.ReleaseScheduledWork(delayed.Pid!.Value, false, generation);
		}
		else
		{
			var admission = await queue.AdmitAsyncAttribute(async () =>
			{
				await Task.Delay(Timeout.Infinite, ExecutionBudget.CurrentToken);
				return state;
			}, new DbRefAttribute(new DBRef(10), ["CALLBACK"]), new DBRef(10), new DBRef(12));
			await Assert.That(admission.Accepted).IsTrue();
		}

		await Assert.That(await told.Task.WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo(12);
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
			await QueueEnumerationOrderingTests.AssertIndexMatchesLedger(queue);
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
		await QueueEnumerationOrderingTests.AssertIndexMatchesLedger(queue);
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
		await QueueEnumerationOrderingTests.AssertIndexMatchesLedger(queue);
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
		notifications.NotifyLocalized(Arg.Any<long>(), "QueueRejected", Arg.Any<AnySharpObject?>(), Arg.Any<object[]>()).Returns(_ =>
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
				catch (AggregateException) { }
				await Assert.That((await queue.ReleaseScheduledWork(pending.Pid!.Value)).Accepted).IsFalse();
				await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
			}
			scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(true);
			using (await queue.EnterSemaphoreMutationAsync()) { }
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(drain ? 1 : 2);
		}
		finally { release.SetResult(); }
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task SemaphoreCommandRetainsTimersAndQuotaUntilCleanupAcknowledged(bool drain, bool expire)
	{
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(global: 3, scheduler: scheduler);
		var entered = Signal(); var release = Signal();
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "block", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var target = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
			var first = await queue.AdmitCommandList(MarkupText.Plain("first"), ParserState.Empty, target, 0);
			var second = await queue.AdmitCommandList(MarkupText.Plain("second"), ParserState.Empty, target, 0);
			var retained = new HashSet<string> { $"dbref:-{first.Pid}", $"dbref:-{second.Pid}" };
			var fail = true;
			scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(call =>
			{
				call.Arg<CancellationToken>().ThrowIfCancellationRequested();
				var key = call.Arg<TriggerKey>().Name;
				if (fail && key == $"dbref:-{second.Pid}") throw new IOException("cleanup acknowledgement lost");
				return Task.FromResult(retained.Remove(key));
			});
			using var cancellation = new CancellationTokenSource();
			using var budget = ExecutionBudget.FromMilliseconds(30000, cancellation.Token);
			var failed = false;
			using (await queue.EnterSemaphoreMutationAsync())
			using (budget.Enter())
			{
				try
				{
					await queue.ApplySemaphoreCommandAsync(target, null, drain,
						_ => { if (expire) cancellation.Cancel(); return ValueTask.CompletedTask; },
						() => ValueTask.FromException<bool>(new Exception("A confirmed write must not be reconciled again")));
				}
				catch (IOException) { failed = true; }
				catch (OperationCanceledException) { failed = true; }
				await Assert.That(failed).IsTrue();
				await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(3);
				await Assert.That((await queue.ReleaseScheduledWork(first.Pid!.Value)).Accepted).IsFalse();
			}
			fail = false;
			using (await queue.EnterSemaphoreMutationAsync()) { }
			await Assert.That(retained.Count).IsEqualTo(0);
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(drain ? 1 : 3);
		}
		finally { release.TrySetResult(); }
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
		validation.Valid(Arg.Any<IValidateService.ValidationType>(), Arg.Any<MarkupString.MarkupText>(), Arg.Any<ValidationTarget>()).Returns(true);
		CancellationToken observed = default;
		mediator.CreateStream(Arg.Any<GetAttributeWithInheritanceQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => { observed = call.ArgAt<CancellationToken>(1); return AsyncEnumerable.Empty<AttributeWithInheritance>(); });
		var service = new SharpMUSH.Library.Services.AttributeService(mediator, Substitute.For<IPermissionService>(),
			Substitute.For<ILocateService>(), validation, Substitute.For<INotifyService>(),
			Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IServiceProvider>(),
			NullLogger<SharpMUSH.Library.Services.AttributeService>.Instance);
		var target = (await mediator.Send(new GetObjectNodeQuery(new DBRef(10)))).Expect<AnySharpObject>();
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
	public async Task HaltedReadyTimeoutStillCompletesSemaphoreBookkeeping()
	{
		var count = 1;
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		await using var queue = Create(global: 3, parser: parser,
			mediator: CountingMediator(() => count, value => count = value));
		var blocked = Signal(); var release = Signal(); var drained = Signal();
		await queue.AdmitWork(async () => { blocked.SetResult(); await release.Task; return null; }, "block", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var semaphore = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
			var pending = await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think timeout"), ParserState.Empty, semaphore, 0);
			await queue.ReleaseScheduledWork(pending.Pid!.Value, semaphoreTimeout: true);
			await queue.HaltByPid(pending.Pid.Value);
			await queue.AdmitWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "drained", "test");
		}
		finally { release.SetResult(); }
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(count).IsEqualTo(0);
		await parser.DidNotReceive().CommandListParse(Arg.Any<MarkupString.MarkupText>());
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
		var firing = queue.ReleaseScheduledWork(entry.Pid!.Value, semaphoreTimeout: true).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var shutdown = queue.DisposeAsync().AsTask();
		try { await shutdown.WaitAsync(TimeSpan.FromSeconds(1)); }
		finally { release.Cancel(); await shutdown; }
		await Assert.That(async () => await firing).Throws<OperationCanceledException>();
	}

	[Test]
	public async Task TimeoutBookkeepingCannotBlockTheQueuePastItsDeadline()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var ran = false;
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { ran = true; return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(global: 4, parser: parser, mediator: CountingMediator(() => 1, _ => { }));
		var entered = Signal(); var release = Signal(); var drained = Signal();
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "blocker", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var waiting = await queue.AdmitCommandList(MarkupText.Plain("think expired"), ParserState.Empty,
				new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 1, TimeSpan.FromHours(1));
			using var held = await queue.EnterSemaphoreMutationAsync();
			await queue.AdmitWork(async () => { await queue.ReleaseScheduledWork(waiting.Pid!.Value, semaphoreTimeout: true); return null; }, "timeout", "test");
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
		var diagnostics = new QueueDiagnosticsRecorder();
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
		await using var queue = Create(global: 1, mediator: mediator, scheduler: scheduler, diagnostics: diagnostics);
		await Assert.That(async () => await queue.AdmitCommandList(MarkupText.Plain("think rejected"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, manageSemaphoreCount: true)).Throws<AggregateException>();
		await Assert.That(count).IsEqualTo(1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await QueueEnumerationOrderingTests.AssertIndexMatchesLedger(queue);
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
		await QueueEnumerationOrderingTests.AssertIndexMatchesLedger(queue);
		count = 1;
		await Assert.That(await queue.HaltByPid(1)).IsTrue();
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await QueueEnumerationOrderingTests.AssertIndexMatchesLedger(queue);
		await Assert.That(diagnostics.Recent().Single().Outcome).IsEqualTo(QueueOutcome.ScheduleFailed);
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
	public async Task ShutdownCancelsHaltProviderCallBeforeWaitingForDelayedLease()
	{
		var entered = Signal();
		var release = Signal();
		var settled = false;
		CancellationToken providerToken = default;
		var scheduler = Substitute.For<IScheduler>();
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			providerToken = call.Arg<CancellationToken>();
			entered.TrySetResult();
			try { await release.Task.WaitAsync(providerToken); }
			finally { settled = true; }
			return true;
		});
		var queue = Create(scheduler: scheduler);
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty, TimeSpan.FromDays(100));
		var halt = queue.HaltByPid(admission.Pid!.Value).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
		var stopping = queue.DisposeAsync().AsTask();
		try
		{
			await stopping.WaitAsync(TimeSpan.FromSeconds(2));
			await Assert.That(providerToken.IsCancellationRequested).IsTrue();
			await Assert.That(settled).IsTrue();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		}
		finally
		{
			release.TrySetResult();
			try { await halt; } catch (OperationCanceledException) { }
			await stopping;
		}
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
	public async Task ShutdownSettlesSemaphorePublicationBeforeReleasingItsReservation(bool managed)
	{
		var entered = Signal();
		var finish = Signal();
		var exists = false;
		var count = 0;
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			entered.TrySetResult();
			await finish.Task;
			exists = true;
			return DateTimeOffset.UtcNow;
		});
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ => { exists = false; return true; });
		var queue = Create(mediator: CountingMediator(() => count, value => count = value), scheduler: scheduler);
		var admission = queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, TimeSpan.FromDays(36500), managed).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
		var stopping = queue.DisposeAsync().AsTask();
		try { await Assert.That(stopping.IsCompleted).IsFalse(); }
		finally
		{
			finish.TrySetResult();
			try { await admission; } catch (OperationCanceledException) { }
			await stopping.WaitAsync(TimeSpan.FromSeconds(2));
		}
		await Assert.That(exists).IsFalse();
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task UnmanagedSemaphorePublicationSerializesHaltAndTimeout(bool halt)
	{
		var entered = Signal();
		var finish = Signal();
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			entered.TrySetResult();
			await finish.Task;
			return await Task.FromException<DateTimeOffset>(new InvalidOperationException("Publication acknowledgement lost"));
		});
		await using var queue = Create(scheduler: scheduler);
		var admission = queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, TimeSpan.Zero).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
		Task operation = halt ? queue.HaltByPid(1).AsTask() : queue.ReleaseScheduledWork(1, true).AsTask();
		try
		{
			await Assert.That(operation.IsCompleted).IsFalse();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		}
		finally
		{
			finish.TrySetResult();
			try { await admission; } catch (InvalidOperationException) { }
			await operation.WaitAsync(TimeSpan.FromSeconds(2));
		}
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task LostSemaphorePublicationRetainsQuotaUntilTriggerAndCounterAreRestored(bool managed, bool cleanupFails)
	{
		var exists = false;
		var cleanupWasBounded = false;
		var count = 0;
		var mediator = CountingMediator(() => count, value => count = value);
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns<DateTimeOffset>(_ =>
		{
			exists = true;
			throw new InvalidOperationException("Schedule acknowledgement lost");
		});
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			if (cleanupFails) throw new InvalidOperationException("Cleanup unavailable");
			cleanupWasBounded = call.Arg<CancellationToken>().CanBeCanceled;
			exists = false;
			return true;
		});
		await using var queue = Create(global: 1, mediator: mediator, scheduler: scheduler);
		async Task Admit() => await queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, TimeSpan.FromDays(36500), managed);
		if (cleanupFails) await Assert.ThrowsAsync<AggregateException>(Admit);
		else await Assert.ThrowsAsync<InvalidOperationException>(Admit);
		await Assert.That(exists).IsEqualTo(cleanupFails);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(cleanupFails ? 1 : 0);
		await Assert.That(count).IsEqualTo(cleanupFails && managed ? 1 : 0);
		if (cleanupFails)
		{
			await Assert.That((await queue.ReleaseScheduledWork(1)).Accepted).IsFalse();
			cleanupFails = false;
			using (await queue.EnterSemaphoreMutationAsync()) { }
			await Assert.That(exists).IsFalse();
			await Assert.That(count).IsEqualTo(0);
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		}
		await Assert.That(cleanupWasBounded).IsTrue();
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
		var diagnostics = new QueueDiagnosticsRecorder();
		await using var queue = Create(scheduler: scheduler, diagnostics: diagnostics);
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
		await Assert.That(diagnostics.Recent().Single().Outcome).IsEqualTo(QueueOutcome.ScheduleFailed);
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
		var diagnostics = new QueueDiagnosticsRecorder();
		await using var queue = Create(mediator: mediator, scheduler: Substitute.For<IScheduler>(), diagnostics: diagnostics);
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think rejected"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, manageSemaphoreCount: true);
		await Assert.That(admission.Reason).IsEqualTo(QueueRejectionReason.InvalidTarget);
		await Assert.That(diagnostics.Recent().Single().Outcome).IsEqualTo(QueueOutcome.InvalidTarget);
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
	[Arguments(false)]
	[Arguments(true)]
	public async Task ConcurrentQuartzTimeoutCannotDeleteAnUnacknowledgedRelease(bool managed)
	{
		var count = managed ? 0 : 1;
		var armed = false;
		var entered = Signal(); var release = Signal(); var executed = Signal();
		var mediator = CountingMediator(() => count, value => count = value);
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>())
			.Returns(async ValueTask<bool> (call) =>
			{
				if (armed) { entered.TrySetResult(); await release.Task; }
				count = int.Parse(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText());
				return true;
			});
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var ran = 0;
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { Interlocked.Increment(ref ran); executed.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(mediator: mediator, parser: parser, scheduler: scheduler);
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think timeout"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), managed ? 0 : 1, TimeSpan.FromHours(1), managed);
		var context = Substitute.For<IJobExecutionContext>();
		context.Scheduler.Returns(scheduler);
		context.MergedJobDataMap.Returns(new JobDataMap { ["Generation"] = 0L });
		context.Trigger.Returns(TriggerBuilder.Create().WithIdentity("dbref:10-" + admission.Pid, "semaphore:10/SEMAPHORE").Build());
		context.JobDetail.Returns(JobBuilder.Create<SemaphoreTask>().Build());
		var job = new SemaphoreTask(queue);
		armed = true;
		var first = job.Execute(context);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Task? second = null;
		try
		{
			second = job.Execute(context);
			await Assert.That(second.IsCompleted).IsFalse();
			await scheduler.DidNotReceive().DeleteJob(Arg.Any<JobKey>(), Arg.Any<CancellationToken>());
		}
		finally { release.TrySetResult(); await first; if (second is not null) await second; }
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(ran).IsEqualTo(1);
		await Assert.That(count).IsEqualTo(0);
	}

	[Test]
	[Arguments(false, false, false)]
	[Arguments(true, false, false)]
	[Arguments(false, true, false)]
	[Arguments(true, true, false)]
	[Arguments(false, false, true)]
	[Arguments(true, false, true)]
	[Arguments(false, true, true)]
	[Arguments(true, true, true)]
	public async Task FailedTimeoutCounterWriteRetainsTheQuartzJobAndQuota(bool managed, bool lostAcknowledgement, bool mutationRepairs)
	{
		var count = managed ? 0 : 2;
		var fail = false;
		var ran = 0;
		var completed = Signal();
		var mediator = CountingMediator(() => count, value => count = value);
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				if (fail && !lostAcknowledgement) return ValueTask.FromResult(false);
				count = int.Parse(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText());
				if (fail) throw new InvalidOperationException("lost timeout write acknowledgement");
				return ValueTask.FromResult(true);
			});
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { Interlocked.Increment(ref ran); completed.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(global: 2, mediator: mediator, parser: parser, scheduler: scheduler);
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think timeout"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), managed ? 0 : 1, TimeSpan.FromHours(1), managed);
		await queue.AdmitCommandList(MarkupText.Plain("think remaining"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 1, TimeSpan.FromHours(1), managed);

		var context = Substitute.For<IJobExecutionContext>();
		context.Scheduler.Returns(scheduler);
		context.MergedJobDataMap.Returns(new JobDataMap { ["Generation"] = 0L });
		context.Trigger.Returns(TriggerBuilder.Create().WithIdentity("dbref:10-" + admission.Pid, "semaphore:10/SEMAPHORE").Build());
		context.JobDetail.Returns(JobBuilder.Create<SemaphoreTask>().Build());
		var storedTrigger = context.Trigger.GetTriggerBuilder().ForJob(context.JobDetail).Build();
		scheduler.GetTrigger(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(storedTrigger);
		var job = new SemaphoreTask(queue);
		fail = true;
		await Assert.That(async () => await job.Execute(context)).Throws<JobExecutionException>();
		await Assert.That(ran).IsEqualTo(0);
		await Assert.That(count).IsEqualTo(lostAcknowledgement ? 1 : 2);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
		await scheduler.DidNotReceive().DeleteJob(Arg.Any<JobKey>(), Arg.Any<CancellationToken>());
		fail = false;
		if (mutationRepairs) { using var lease = await queue.EnterSemaphoreMutationAsync(); }
		await job.Execute(context);
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		for (var attempt = 0; attempt < 100 && queue.GetQueueUsage().Total != 1; attempt++) await Task.Delay(10);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(ran).IsEqualTo(1);
		await Assert.That(count).IsEqualTo(1);
		await scheduler.Received(1).DeleteJob(context.JobDetail.Key, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task FailedQuartzSemaphoreReleaseRequestsRetryBeforeDeletingSchedule()
	{
		var scheduler = Substitute.For<IScheduler>();
		var queue = Substitute.For<ITaskScheduler>();
		var context = Substitute.For<IJobExecutionContext>();
		context.Scheduler.Returns(scheduler);
		context.MergedJobDataMap.Returns(new JobDataMap { ["Generation"] = 0L });
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
	public async Task PrivilegedAllowanceAddsDatabaseCount(bool wizard, bool power, int allowance)
	{
		var mediator = Substitute.For<IMediator>();
		ConfigureTargets(mediator, wizard, power);
		mediator.Send(Arg.Any<GetObjectFlagQuery>(), Arg.Any<CancellationToken>()).Returns(HaltFlag());
		mediator.Send(Arg.Any<GetObjectCountQuery>(), Arg.Any<CancellationToken>()).Returns(2);
		await using var queue = Create(global: 4, owner: 1, mediator: mediator);
		var state = ParserState.RootFor(new DBRef(10));
		for (var i = 0; i < allowance; i++)
			await Assert.That((await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think pending"), state, TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think rejected"), state, TimeSpan.FromHours(1))).Reason).IsEqualTo(QueueRejectionReason.OwnerLimit);
		// That rejection starts the runaway halt, which wipes what #10 had queued (pay_queue,
		// src/cque.c:303-313). Let it finish before the queue is disposed, or it races teardown.
		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));
	}

	/// <summary>
	/// The privileged allowance is added to the owner's quota, not to the global one: a wizard whose
	/// own bucket still has room is refused all the same once the server-wide ceiling is reached.
	/// </summary>
	/// <remarks>
	/// Nothing here is allowed to trip the owner limit. That rejection halts the offender and wipes its
	/// queue, which would give back the very entries this measures the global ceiling with.
	/// </remarks>
	[Test]
	[Arguments(true, false)]
	[Arguments(false, true)]
	public async Task PrivilegedAllowanceStillRespectsTheGlobalCeiling(bool wizard, bool power)
	{
		var mediator = Substitute.For<IMediator>();
		ConfigureTargets(mediator, wizard, power);
		mediator.Send(Arg.Any<GetObjectCountQuery>(), Arg.Any<CancellationToken>()).Returns(2);
		await using var queue = Create(global: 4, owner: 1, mediator: mediator);
		// Three for #10 and one for #11 — each inside its own allowance of three, four in all.
		var state = ParserState.RootFor(new DBRef(10));
		for (var i = 0; i < 3; i++)
			await Assert.That((await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think pending"), state, TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think other"), ParserState.RootFor(new DBRef(11)), TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupString.MarkupText.Plain("think capped"), ParserState.RootFor(new DBRef(11)), TimeSpan.FromHours(1))).Reason).IsEqualTo(QueueRejectionReason.GlobalLimit);
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
	public async Task SemaphoreAccountingFailureRetainsActionUntilRetry()
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
		await Assert.That(async () => await queue.ReleaseScheduledWork(job.Pid!.Value, semaphoreTimeout: true)).Throws<InvalidOperationException>();
		await Assert.That(executed.Task.IsCompleted).IsFalse();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<SharpAttribute>());
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
	public async Task SystemOwnerBudgetIncludesRunningWork()
	{
		await using var queue = Create(global: 10, owner: 1);
		var entered = Signal(); var release = Signal();
		await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "first", "test");
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var rejected = await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "second", "test");
			await Assert.That(rejected.Reason).IsEqualTo(QueueRejectionReason.OwnerLimit);
			await Assert.That(queue.GetQueueUsage().Owners["system"]).IsEqualTo(1);
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
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
				Grants = new(_ => Task.FromResult(ObjectGrants.None)),
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

	/// <summary>
	/// Socket work — an inbound HTTP request — is charged to no owner (PennMUSH <c>do_entry</c> skips
	/// <c>QUEUE_SOCKET</c> entries): it neither fills the shared system bucket nor is refused by it, and
	/// only the global limit applies (#1184).
	/// </summary>
	[Test]
	public async Task SocketWorkIsChargedToNoOwnerOnlyToTheGlobalLimit()
	{
		await using var queue = Create(global: 6, owner: 2);
		var started = Signal(); var release = Signal();
		await queue.AdmitWork(async () => { started.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
			for (var i = 0; i < 3; i++)
				await Assert.That((await queue.AdmitSocketWork(() => ValueTask.FromResult<CallState?>(null), $"socket-{i}", "http")).Accepted)
					.IsTrue().Because("three socket entries are past the owner limit of two, but they belong to no owner");

			await Assert.That((await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "system", "test")).Accepted)
				.IsTrue().Because("the socket entries took nothing from the system bucket: the blocker is its only other entry");
			await Assert.That((await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "system-over", "test")).Reason)
				.IsEqualTo(QueueRejectionReason.OwnerLimit).Because("the system bucket itself is still bounded");
			await Assert.That((await queue.AdmitSocketWork(() => ValueTask.FromResult<CallState?>(null), "socket-last", "http")).Accepted).IsTrue();
			await Assert.That((await queue.AdmitSocketWork(() => ValueTask.FromResult<CallState?>(null), "socket-over", "http")).Reason)
				.IsEqualTo(QueueRejectionReason.GlobalLimit);
		}
		finally { release.TrySetResult(); }
	}

	/// <summary>A socket entry halted before it runs still reports its release, so whoever waits on it has an answer.</summary>
	[Test]
	public async Task SocketWorkHaltedBeforeItRunsIsReleased()
	{
		await using var queue = Create(global: 3);
		var started = Signal(); var release = Signal(); var released = Signal();
		var ran = false;
		await queue.AdmitWork(async () => { started.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
			var socket = await queue.AdmitSocketWork(() => { ran = true; return ValueTask.FromResult<CallState?>(null); },
				"socket", "http", onReleased: () => released.TrySetResult());
			await Assert.That(socket.Accepted).IsTrue();

			await queue.HaltByPid(socket.Pid!.Value);
			release.TrySetResult();

			await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(5));
			await Assert.That(ran).IsFalse();
		}
		finally { release.TrySetResult(); }
	}

	/// <summary>The HALT flag <see cref="ConfigureTargets"/>'s objects do not carry until one is set.</summary>
	private static SharpObjectFlag HaltFlag() => new()
	{
		Name = "HALT", Symbol = "h", System = true, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = []
	};

	/// <summary>
	/// A mediator whose objects are <see cref="ConfigureTargets"/>'s, that answers the HALT flag
	/// lookup, and that completes the returned signal when the runaway path writes the flag — which
	/// <see cref="HaltRunaway"/> does after the wipe, so it doubles as the wipe's completion signal.
	/// </summary>
	private static (IMediator Mediator, TaskCompletionSource Halted) RunawayMediator()
	{
		var mediator = TargetMediator();
		var halted = Signal();
		mediator.Send(Arg.Any<GetObjectFlagQuery>(), Arg.Any<CancellationToken>()).Returns(HaltFlag());
		mediator.Send(Arg.Any<SetObjectFlagCommand>(), Arg.Any<CancellationToken>())
			.Returns(_ => { halted.TrySetResult(); return ValueTask.FromResult(true); });
		return (mediator, halted);
	}

	/// <summary>
	/// Every object answers as a player owned by #5, so two executors share one owner.
	/// </summary>
	private static IMediator SharedOwnerMediator()
	{
		var mediator = Substitute.For<IMediator>();
		SharpPlayer Player(int number) => new()
		{
			Object = new SharpObject
			{
				Key = number, CreationTime = 1, Name = $"Obj{number}", Type = "PLAYER", Locks = null!, Owner = null!,
				Grants = new(_ => Task.FromResult(ObjectGrants.None)),
				Powers = new(() => AsyncEnumerable.Empty<SharpPower>()), Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!,
				Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()), Parent = null!, Zone = null!, Children = null!
			},
			Location = null!, Home = null!, PasswordHash = "", Quota = 0
		};
		var owner = Player(5);
		owner.Object.Owner = new(_ => Task.FromResult(owner));
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var number = call.Arg<GetObjectNodeQuery>().DBRef.Number;
			if (number == 5) return ValueTask.FromResult<AnyOptionalSharpObject>(owner);
			var player = Player(number);
			player.Object.Owner = new(_ => Task.FromResult(owner));
			return ValueTask.FromResult<AnyOptionalSharpObject>(player);
		});
		return mediator;
	}

	/// <summary>
	/// <c>player_queue_limit</c> counts each object's own entries: <c>pay_queue</c> charges
	/// <c>queue_limit(QUEUE_PER_OWNER ? Owner(player) : player)</c> (<c>src/cque.c:303</c>), and
	/// <c>owner_queues</c> ships off. Two objects of one owner each get the whole limit.
	/// </summary>
	[Test]
	public async Task PlayerQueueLimitCountsEachObjectOnItsOwn()
	{
		await using var queue = Create(global: 10, owner: 1, mediator: SharedOwnerMediator());

		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think ten"), ParserState.RootFor(new DBRef(10)), TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think eleven"), ParserState.RootFor(new DBRef(11)), TimeSpan.FromHours(1))).Accepted)
			.IsTrue().Because("#11 has a count of its own, though #10 has the same owner and has used its one");
		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think ten again"), ParserState.RootFor(new DBRef(10)), TimeSpan.FromHours(1))).Reason)
			.IsEqualTo(QueueRejectionReason.OwnerLimit).Because("#10's own count is full");
	}

	/// <summary>With <c>owner_queues</c> on, objects share their owner's count (<c>src/cque.c:303</c>).</summary>
	[Test]
	public async Task OwnerQueuesPoolsAnOwnersObjectsIntoOneCount()
	{
		await using var queue = Create(global: 10, owner: 1, mediator: SharedOwnerMediator(), ownerQueues: true);

		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think ten"), ParserState.RootFor(new DBRef(10)), TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think eleven"), ParserState.RootFor(new DBRef(11)), TimeSpan.FromHours(1))).Reason)
			.IsEqualTo(QueueRejectionReason.OwnerLimit).Because("#11 is charged to #5, whose count #10 has filled");
	}

	/// <summary>
	/// <c>owner_queues</c> can be set while entries are pending; each pending entry still counts under
	/// whichever grouping admission reads next, so turning the option over frees no quota.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task TurningOwnerQueuesOverKeepsPendingEntriesCounted(bool pooledFirst)
	{
		var pooled = pooledFirst;
		await using var queue = Create(global: 10, owner: 1, mediator: SharedOwnerMediator(), ownerQueuesNow: () => pooled);

		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think ten"), ParserState.RootFor(new DBRef(10)), TimeSpan.FromHours(1))).Accepted).IsTrue();
		pooled = !pooledFirst;

		// Pooled now: #11 shares #5's count, which #10 filled. Unpooled now: #10's own count is full.
		var next = pooled ? new DBRef(11) : new DBRef(10);
		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think next"), ParserState.RootFor(next), TimeSpan.FromHours(1))).Reason)
			.IsEqualTo(QueueRejectionReason.OwnerLimit).Because("the entry admitted before the change still counts");
		await AssertTalliesMatchLedger(queue, "both groupings follow the ledger");
	}

	/// <summary>
	/// The owner of a runaway hears <c>pay_queue</c>'s notice and then <c>do_halt</c>'s, each naming the
	/// object by its plain dbref (<c>src/cque.c:304,2176-2178</c>), and nothing else: the refused entry
	/// gets no admission notice of its own.
	/// </summary>
	[Test]
	public async Task ARunawaysOwnerHearsTheRunawayAndHaltedNoticesOnly()
	{
		var (mediator, _) = RunawayMediator();
		var notifications = Substitute.For<INotifyService>();
		var connections = Substitute.For<IConnectionService>();
		connections.Get(Arg.Any<DBRef>()).Returns(new[]
		{
			new IConnectionService.ConnectionData(12, new DBRef(10), IConnectionService.ConnectionState.LoggedIn,
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8, new())
		}.ToAsyncEnumerable());
		await using var queue = Create(global: 4, owner: 1, mediator: mediator, notifications: notifications, connections: connections);
		var state = ParserState.RootFor(new DBRef(10));

		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think pending"), state, TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think runaway"), state, TimeSpan.FromHours(1))).Reason)
			.IsEqualTo(QueueRejectionReason.OwnerLimit);
		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));

		var notices = notifications.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.NotifyLocalized))
			.Select(call => call.GetArguments())
			.Select(args => $"{args[1]}:{string.Join(",", (object[])args[3]!)}")
			.ToArray();
		await Assert.That(notices).IsEquivalentTo(["RunawayObjectFormat:Semaphore,#10", "HaltedNoticeFormat:Semaphore,#10"]);
	}

	/// <summary>
	/// <c>pay_queue</c> ends with <c>set_flag_internal(player, "HALT")</c> (<c>src/cque.c:312</c>) —
	/// no type test, so a runaway player is halted exactly as a runaway object is. The flag does not
	/// silence them: the queue exempts players (<c>insert_que</c>, <c>src/cque.c:530</c>) and
	/// <c>process_command</c> (<c>src/game.c:1181</c>) refuses only what it did not type. #1006.
	/// </summary>
	[Test]
	public async Task ARunawayPlayerIsHaltedLikeAnyOtherOffender()
	{
		var (mediator, _) = RunawayMediator();
		await using var queue = Create(global: 4, owner: 1, mediator: mediator);
		var state = ParserState.RootFor(new DBRef(10));

		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think pending"), state, TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think runaway"), state, TimeSpan.FromHours(1))).Reason)
			.IsEqualTo(QueueRejectionReason.OwnerLimit);
		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));

		await mediator.Received().Send(
			Arg.Is<SetObjectFlagCommand>(set => set.Flag.Name == "HALT" && set.Target.Object().DBRef.Number == 10),
			Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// The wipe is mandatory and unbounded. <c>pay_queue</c>'s <c>do_halt</c> and
	/// <c>set_flag_internal</c> sit outside <c>start_cpu_timer</c>, which bounds an entry's evaluation
	/// and not the queue's bookkeeping (<c>src/cque.c:303-313</c> against <c>:1141</c>). Capping it at
	/// one entry's deadline abandons a long wipe part-done, and nothing retries: the offender keeps its
	/// backlog and never gets the flag.
	/// </summary>
	[Test]
	public async Task TheRunawayHaltOutlivesOneEntrysExecutionBudget()
	{
		var (mediator, halted) = RunawayMediator();
		// Reads that take longer than one entry's deadline and honour cancellation, as the database
		// does. Under a per-entry budget the wipe's first read is already past it.
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(async ValueTask<AnyOptionalSharpObject> (call) =>
			{
				var node = await TargetMediator().Send(call.Arg<GetObjectNodeQuery>(), CancellationToken.None);
				await Task.Delay(TimeSpan.FromMilliseconds(40), CancellationToken.None);
				call.Arg<CancellationToken>().ThrowIfCancellationRequested();
				return node;
			});
		await using var queue = Create(global: 4, owner: 1, mediator: mediator, milliseconds: 1);
		var state = ParserState.RootFor(new DBRef(10));

		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think pending"), state, TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think runaway"), state, TimeSpan.FromHours(1))).Reason)
			.IsEqualTo(QueueRejectionReason.OwnerLimit);

		await halted.Task.WaitAsync(TimeSpan.FromSeconds(10))
			.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
	}

	/// <summary>
	/// The owner's runaway notices are best-effort — a transport that is down can make them throw.
	/// Telling nobody about a runaway is survivable; leaving one running is not, so the wipe and the
	/// HALT come before the notices, and the refusal itself sends none.
	/// </summary>
	[Test]
	public async Task TheRunawayIsHaltedEvenWhenItsNoticeFails()
	{
		var (mediator, halted) = RunawayMediator();
		var notifications = Substitute.For<INotifyService>();
		notifications.NotifyLocalized(Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<AnySharpObject?>(), Arg.Any<object[]>())
			.Returns(_ => throw new InvalidOperationException("transport down"));
		await using var queue = Create(global: 4, owner: 1, mediator: mediator, notifications: notifications);
		var state = ParserState.RootFor(new DBRef(10));

		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think pending"), state, TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think runaway"), state, TimeSpan.FromHours(1))).Reason)
			.IsEqualTo(QueueRejectionReason.OwnerLimit);

		await halted.Task.WaitAsync(TimeSpan.FromSeconds(10));
	}

	/// <summary>
	/// The wipe <c>pay_queue</c> performs (<c>do_halt(Owner(player), "", player)</c>,
	/// <c>src/cque.c:311</c>) walks the queue, and a typed line is not on it — <c>run_user_input</c>
	/// never inserts one. SharpMUSH does queue typed input, so the wipe has to spare that group or the
	/// halt would take back with one hand what the gate's player exemption gives with the other.
	/// </summary>
	[Test]
	public async Task TheRunawayWipeLeavesWhatTheOffenderAlreadyTyped()
	{
		// The flag is set after the wipe, so that signal is the wipe's completion signal. Without it
		// the test would race the detached halt and pass on a typed entry that simply ran first.
		var (mediator, wiped) = RunawayMediator();
		// One charged slot: the typed line below is uncharged (#1320), so the queued command takes the
		// slot and the third admission is the one that trips.
		await using var queue = Create(global: 6, owner: 1, mediator: mediator);
		var offender = new DBRef(10);
		var blocked = Signal(); var release = Signal(); var typedRan = false;

		// Park the consumer, so the typed entry below is admitted and still waiting when the wipe runs.
		await queue.AdmitWork(async () => { blocked.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			await Assert.That((await queue.AdmitWork(() => { typedRan = true; return ValueTask.FromResult<CallState?>(null); },
				"typed", Scheduler.DirectInputGroup, offender)).Accepted).IsTrue();
			await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think queued"),
				ParserState.RootFor(offender), TimeSpan.FromHours(1))).Accepted).IsTrue();
			await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think runaway"),
				ParserState.RootFor(offender), TimeSpan.FromHours(1))).Reason).IsEqualTo(QueueRejectionReason.OwnerLimit);
			await wiped.Task.WaitAsync(TimeSpan.FromSeconds(10));
		}
		finally { release.TrySetResult(); }

		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));

		await Assert.That(typedRan).IsTrue().Because("a halted player keeps the line they had already typed");
	}

	/// <summary>
	/// <c>run_user_input</c> builds its entry with <c>QUEUE_SOCKET</c> and hands it to <c>do_entry</c>
	/// directly (<c>src/cque.c:1076-1088</c>), so a typed line never passes through <c>insert_que</c>
	/// and never reaches <c>pay_queue</c>. It is therefore never refused by the owner quota, and
	/// whoever typed it is never the runaway — a player whose own objects filled the quota can still
	/// type, and is not halted for being at the keyboard. #1320.
	/// </summary>
	[Test]
	public async Task ATypedLineIsAdmittedOverTheOwnerQuotaAndHaltsNobody()
	{
		var (mediator, _) = RunawayMediator();
		await using var queue = Create(global: 4, owner: 1, mediator: mediator);
		var typist = new DBRef(10);

		await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think pending"),
			ParserState.RootFor(typist), TimeSpan.FromHours(1))).Accepted).IsTrue();
		await Assert.That((await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null),
			"typed", Scheduler.DirectInputGroup, typist)).Accepted).IsTrue()
			.Because("queue_limit reads the add_to tally, which run_user_input never touches");
		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));

		await mediator.DidNotReceive().Send(Arg.Any<SetObjectFlagCommand>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// The tally <c>queue_limit</c> reads is kept by <c>add_to</c> alone (<c>src/cque.c:226-235</c>),
	/// and only <c>insert_que</c> adds to it. A typed line is not in it, so it cannot be the reason
	/// the owner's next queued command is refused — nor, therefore, the reason the owner is halted as
	/// a runaway. #1320.
	/// </summary>
	[Test]
	public async Task TypedInputIsNotCountedAgainstTheOwnersNextQueuedCommand()
	{
		var (mediator, _) = RunawayMediator();
		await using var queue = Create(global: 6, owner: 1, mediator: mediator);
		var offender = new DBRef(10);
		var blocked = Signal(); var release = Signal();

		// Park the consumer, so the typed line below is still pending when the quota is tested.
		await queue.AdmitWork(async () => { blocked.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			await Assert.That((await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null),
				"typed", Scheduler.DirectInputGroup, offender)).Accepted).IsTrue();
			await Assert.That((await queue.AdmitCommandList(MarkupText.Plain("think queued"),
				ParserState.RootFor(offender), TimeSpan.FromHours(1))).Accepted).IsTrue()
				.Because("the owner's single slot is still free — the typed line does not occupy it");
		}
		finally { release.TrySetResult(); }

		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));

		await mediator.DidNotReceive().Send(Arg.Any<SetObjectFlagCommand>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// What bounds typed input in PennMUSH is the descriptor's own command quota — a burst of
	/// <c>COMMAND_BURST_SIZE</c> that replenishes at <c>COMMANDS_PER_SECOND</c>
	/// (<c>hdrs/conf.h:99-100</c>, <c>src/bsd.c:197,1000-1004</c>) — not the owner queue. SharpMUSH
	/// bounds the same thing by connection, as <c>command_burst_size</c> pending typed lines. One
	/// connection filling its burst leaves another connection's, and the owner's queue, untouched.
	/// </summary>
	[Test]
	public async Task OneConnectionsTypedBurstIsBoundedWithoutTouchingAnother()
	{
		await using var queue = Create(global: 20, owner: 10, burst: 2);
		var blocked = Signal(); var release = Signal();

		await queue.AdmitWork(async () => { blocked.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue();
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue();
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Reason)
				.IsEqualTo(QueueRejectionReason.ConnectionLimit);
			await Assert.That((await queue.AdmitUserCommand(21, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue()
				.Because("the burst is per connection, as Penn's descriptor quota is");
		}
		finally { release.TrySetResult(); }

		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));
	}

	/// <summary>
	/// A line typed at the login screen runs the server's own login work, not softcode, so
	/// <c>queue_entry_cpu_time</c> does not time it: a login slower than the limit (disk writes, bus
	/// publishes, a cold start) must not end in "CPU usage exceeded." for a player who ran nothing. Once
	/// the connection is bound to a player, its lines are timed as before.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OnlyALoggedInConnectionsTypedLineIsTimed(bool loggedIn)
	{
		var connections = Substitute.For<IConnectionService>();
		var connection = Incarnation(20);
		connections.Get(20).Returns(loggedIn
			? connection with { Ref = new DBRef(10), State = IConnectionService.ConnectionState.LoggedIn }
			: connection);
		var cutOff = false;
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		async ValueTask<CallState> SlowLogin()
		{
			try { await Task.Delay(TimeSpan.FromMilliseconds(200), ExecutionBudget.CurrentToken); }
			catch (OperationCanceledException) { cutOff = true; throw; }
			return CallState.Empty;
		}
		parser.CommandParse(20, Arg.Any<IConnectionService>(), Arg.Any<MString>()).Returns(_ => SlowLogin());
		var notifications = Substitute.For<INotifyService>();
		await using var queue = Create(milliseconds: 20, parser: parser, connections: connections, notifications: notifications);

		await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("connect Someone pw"), ParserState.Empty)).Accepted).IsTrue();
		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(5));

		var told = notifications.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(INotifyService.NotifyLocalized)
			&& call.GetArguments()[1] as string == "CpuUsageExceeded");
		await Assert.That(cutOff).IsEqualTo(loggedIn);
		await Assert.That(told).IsEqualTo(loggedIn ? 1 : 0);
	}

	private static IConnectionService.ConnectionData Incarnation(long handle) => new(handle, null,
		IConnectionService.ConnectionState.Connected, _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
		() => Encoding.UTF8, new ConcurrentDictionary<string, string>());

	/// <summary>
	/// A dropped socket's handle is reused by whoever registers on it next, and the lines the previous
	/// occupant left queued stay pending until the consumer reaches them and their own session check
	/// discards them. The burst is therefore counted per connection incarnation, not per handle
	/// number: a replacement connection starts with its whole allowance.
	/// </summary>
	[Test]
	public async Task AReplacedConnectionDoesNotInheritTheBurstOfTheSocketBeforeIt()
	{
		var connections = Substitute.For<IConnectionService>();
		var first = Incarnation(20);
		connections.Get(20).Returns(first);
		await using var queue = Create(global: 20, owner: 10, connections: connections, burst: 2);
		var blocked = Signal(); var release = Signal();

		await queue.AdmitWork(async () => { blocked.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue();
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue();
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Reason)
				.IsEqualTo(QueueRejectionReason.ConnectionLimit);

			connections.Get(20).Returns(Incarnation(20));
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue()
				.Because("the new socket is a different incarnation and owes nothing to the old one's backlog");
		}
		finally { release.TrySetResult(); }

		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));
	}

	/// <summary>
	/// The other half of that identity: <c>Bind</c>, <c>Unbind</c> and <c>BindAccount</c> replace the
	/// stored <c>ConnectionData</c> with a <c>with</c> copy on the same handle, so logging in mid-burst
	/// must not hand the same socket a second allowance.
	/// </summary>
	[Test]
	public async Task LoggingInDoesNotResetABurstAlreadyUnderWay()
	{
		var connections = Substitute.For<IConnectionService>();
		var connected = Incarnation(20);
		connections.Get(20).Returns(connected);
		await using var queue = Create(global: 20, owner: 10, connections: connections, burst: 2);
		var blocked = Signal(); var release = Signal();

		await queue.AdmitWork(async () => { blocked.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue();
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue();

			// What ConnectionService.Bind stores: a copy carrying the same Metadata instance.
			connections.Get(20).Returns(connected with { Ref = new DBRef(10), State = IConnectionService.ConnectionState.LoggedIn });
			await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Reason)
				.IsEqualTo(QueueRejectionReason.ConnectionLimit)
				.Because("binding a player to a socket does not make it a different socket");
		}
		finally { release.TrySetResult(); }

		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));
	}

	/// <summary>
	/// <c>@halt &lt;object&gt;</c> is <c>do_halt</c>, which walks the run, wait and semaphore queues
	/// (<c>src/cque.c:2179-2218</c>). A typed line is on none of them — <c>run_user_input</c> hands its
	/// <c>QUEUE_SOCKET</c> entry straight to <c>do_entry</c> (<c>:1076-1090</c>) — so no halt in Penn
	/// can take one back. SharpMUSH queues typed input, so the wipe has to spare that group.
	/// </summary>
	[Test]
	public async Task HaltingAnObjectLeavesTheLineItAlreadyTyped()
	{
		await using var queue = Create(global: 6, owner: 4);
		var target = new DBRef(10);
		var blocked = Signal(); var release = Signal();
		var typedRan = false; var queuedRan = false;

		// Park the consumer, so both entries below are admitted and still waiting when the halt runs.
		await queue.AdmitWork(async () => { blocked.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			await Assert.That((await queue.AdmitWork(() => { typedRan = true; return ValueTask.FromResult<CallState?>(null); },
				"typed", Scheduler.DirectInputGroup, target)).Accepted).IsTrue();
			await Assert.That((await queue.AdmitWork(() => { queuedRan = true; return ValueTask.FromResult<CallState?>(null); },
				"queued", Scheduler.EnqueueGroup, target)).Accepted).IsTrue();

			await queue.Halt(target);
		}
		finally { release.TrySetResult(); }

		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));

		await Assert.That(queuedRan).IsFalse().Because("@halt <object> wipes what the object had queued");
		await Assert.That(typedRan).IsTrue().Because("do_halt never reaches a line the player already typed");
	}

	private static async Task AssertTalliesMatchLedger(Scheduler queue, string because)
	{
		var (tallied, recounted) = queue.AdmissionTalliesAgainstLedger();
		await Assert.That(tallied).IsEqualTo(recounted).Because(because);
	}

	/// <summary>
	/// Admission reads the owner quota and the typed-line burst from tallies kept beside the ledger
	/// rather than counting the ledger on every admission (#1336). The tallies must say what a count of
	/// the ledger would at every step: after admission, after a halt by pid, an <c>@halt</c> of the
	/// object, an abandoned reservation, and once everything has run.
	/// </summary>
	[Test]
	public async Task AdmissionTalliesFollowAdmitHaltCancelAndCompletion()
	{
		await using var queue = Create(global: 20, owner: 10, burst: 5);
		var target = new DBRef(10);
		var blocked = Signal(); var release = Signal();

		await queue.AdmitWork(async () => { blocked.TrySetResult(); await release.Task; return null; }, "blocker", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var charged = new List<long>();
			for (var i = 0; i < 3; i++)
			{
				var admitted = await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), $"charged{i}", Scheduler.EnqueueGroup, target);
				await Assert.That(admitted.Accepted).IsTrue();
				charged.Add(admitted.Pid!.Value);
			}
			for (var i = 0; i < 2; i++)
				await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue();
			using var reserved = await queue.ReserveCommandList(MarkupText.Plain("think reserved"), ParserState.RootFor(target));
			await Assert.That(reserved.Admission.Accepted).IsTrue();

			var (admittedTallies, _) = queue.AdmissionTalliesAgainstLedger();
			await Assert.That(admittedTallies).Contains("=4").Because("three admitted and one reserved entry are charged to the owner");
			await Assert.That(admittedTallies).Contains("#20/=2").Because("two lines typed on handle 20 are pending");
			await AssertTalliesMatchLedger(queue, "after admission");

			await queue.HaltByPid(charged[0]);
			await AssertTalliesMatchLedger(queue, "after a halt by pid");

			reserved.Dispose();
			await AssertTalliesMatchLedger(queue, "after an abandoned reservation");

			await queue.Halt(target);
			await AssertTalliesMatchLedger(queue, "after @halt of the object");
		}
		finally { release.TrySetResult(); }

		await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));
		var (tallied, recounted) = queue.AdmissionTalliesAgainstLedger();
		await Assert.That(recounted).IsEqualTo("");
		await Assert.That(tallied).IsEqualTo("").Because("every entry has completed, been halted or been cancelled");
	}

	/// <summary>
	/// The limits read from those tallies still refuse at the configured count and admit again once
	/// what was pending has run: the quota and the burst are given back, not leaked. The work here has
	/// no executor, so it is charged to the system bucket and a refusal halts nothing.
	/// </summary>
	[Test]
	public async Task OwnerQuotaAndBurstAreGivenBackWhenEntriesComplete()
	{
		await using var queue = Create(global: 20, owner: 3, burst: 2);
		var ran = 0;
		ValueTask<CallState?> Work()
		{
			Interlocked.Increment(ref ran);
			return ValueTask.FromResult<CallState?>(null);
		}

		for (var round = 0; round < 2; round++)
		{
			var blocked = Signal(); var release = Signal();
			// The blocker is the first of the system bucket's three.
			await queue.AdmitWork(async () => { blocked.TrySetResult(); await release.Task; return null; }, "blocker", "test");
			await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
			try
			{
				await Assert.That((await queue.AdmitWork(Work, "one", "test")).Accepted).IsTrue();
				await Assert.That((await queue.AdmitWork(Work, "two", "test")).Accepted).IsTrue()
					.Because($"round {round}: the quota is back to three");
				await Assert.That((await queue.AdmitWork(Work, "three", "test")).Reason).IsEqualTo(QueueRejectionReason.OwnerLimit);

				await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue();
				await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Accepted).IsTrue()
					.Because($"round {round}: the burst is back to two");
				await Assert.That((await queue.AdmitUserCommand(20, MarkupText.Plain("look"), ParserState.Empty)).Reason)
					.IsEqualTo(QueueRejectionReason.ConnectionLimit);
				await AssertTalliesMatchLedger(queue, $"round {round}, loaded");
			}
			finally { release.TrySetResult(); }

			await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));
			await AssertTalliesMatchLedger(queue, $"round {round}, drained");
			await Assert.That(queue.AdmissionTalliesAgainstLedger().Tallied).IsEqualTo("");
		}

		await Assert.That(ran).IsEqualTo(4).Because("the admitted work ran rather than being dropped");
	}
}
