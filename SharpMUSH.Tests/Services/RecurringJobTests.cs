using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpMUSH.Server.Services;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.RecurringJobs;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services.RecurringJobs;
using QueueScheduler = SharpMUSH.Library.Services.Interfaces.ITaskScheduler;

namespace SharpMUSH.Tests.Services;

public class RecurringJobTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now = DateTimeOffset.Parse("2026-09-14T12:00:00Z");
		public override DateTimeOffset GetUtcNow() => Now;
	}
	private sealed record Context(RecurringJobService Service, CapabilityActor Actor, DBRef Target, Clock Clock, QueueScheduler Queue,
		List<Func<ValueTask<CallState?>>> Callbacks, IAdministrativeCapabilityService Capabilities);
	private RecurringJobService Service(Clock clock, QueueScheduler queue, IAdministrativeCapabilityService capabilities) => new(
		Get<IExpandedDataStore>(), Get<IObjectStore>(), capabilities, Get<IPermissionService>(), Get<IAttributeService>(), queue, Factory.CommandParser, clock);
	private async Task<Context> Setup()
	{
		foreach (var runner in Factory.Services.GetServices<IHostedService>().OfType<RecurringJobRunner>()) await runner.StopAsync(default);
		await Get<IExpandedDataStore>().SetExpandedServerData(RecurringJobService.StorageKey, new RecurringJobDocument([]));
		var player = (await Get<IObjectStore>().GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var actor = await Get<IAdministrativeCapabilityService>().GetGameActorAsync(player.Object.DBRef);
		var target = await Get<IMediator>().Send(new CreateRoomCommand("job-test-" + Guid.NewGuid().ToString("N"), player));
		target = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().DBRef;
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["RUN"], MarkupText.Plain($"&FIRED {target}=yes"), player));
		var clock = new Clock();
		var callbacks = new List<Func<ValueTask<CallState?>>>();
		var queue = Substitute.For<QueueScheduler>();
		var pending = new HashSet<(string Trigger, string Group)>();
		queue.HasPendingWork(Arg.Any<string>(), Arg.Any<string>()).Returns(call => pending.Contains((call.ArgAt<string>(0), call.ArgAt<string>(1))));
		queue.EnqueueWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DBRef>())
			.Returns(call =>
			{
				var action = call.ArgAt<Func<ValueTask<CallState?>>>(0);
				var key = (call.ArgAt<string>(1), call.ArgAt<string>(2));
				pending.Add(key);
				callbacks.Add(async () => { try { return await action(); } finally { pending.Remove(key); } });
				return new QueueAdmissionResult(callbacks.Count, QueueRejectionReason.None);
			});
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
		var service = Service(clock, queue, capabilities);
		await service.InitializeAsync();
		return new(service, actor!, target, clock, queue, callbacks, capabilities);
	}
	private static Task<RecurringJob> Create(Context context) => context.Service.CreateAsync(context.Actor, new(context.Target.ToString(), "RUN", "* * * * *", "UTC"));

	[Test, NotInParallel]
	public async Task DelayedFiringDoesNotAccumulateQueueReservations()
	{
		var context = await Setup();
		await Create(context);
		for (var minute = 0; minute < 5; minute++)
		{
			context.Clock.Now = context.Clock.Now.AddMinutes(1);
			await context.Service.RunDueAsync();
		}
		await Assert.That(context.Callbacks.Count).IsEqualTo(1);
		await context.Callbacks.Single()();
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await context.Service.RunDueAsync();
		await Assert.That(context.Callbacks.Count).IsEqualTo(2);
	}

	[Test, NotInParallel]
	public async Task RealQueueKeepsOneFiringAndRecoversAfterExternalHalt()
	{
		var context = await Setup();
		var queue = Get<QueueScheduler>();
		var service = Service(context.Clock, queue, context.Capabilities);
		await service.InitializeAsync();
		var job = await service.CreateAsync(context.Actor, new(context.Target.ToString(), "RUN", "* * * * *", "UTC"));
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var actor = context.Actor.ActiveCharacter!.Value;
		var blocker = await queue.EnqueueWork(async () => { started.TrySetResult(); await release.Task; return CallState.Empty; },
			"recurring-test-blocker", "tests", actor);
		await Assert.That(blocker.Accepted).IsTrue();
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
			var baseline = queue.GetQueueUsage().Total;
			for (var minute = 0; minute < 5; minute++)
			{
				context.Clock.Now = context.Clock.Now.AddMinutes(1);
				await service.RunDueAsync();
			}
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(baseline + 1);
			await queue.Halt(actor);
			await Assert.That(queue.HasPendingWork("recurring:" + job.Id, "recurring")).IsTrue();
		}
		finally { release.TrySetResult(); }
		async Task Drain()
		{
			var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var admitted = await queue.EnqueueWork(() => { done.TrySetResult(); return ValueTask.FromResult<CallState?>(CallState.Empty); },
				"recurring-test-barrier", "tests", actor);
			await Assert.That(admitted.Accepted).IsTrue();
			await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
		}
		await Drain();
		await Assert.That(queue.HasPendingWork("recurring:" + job.Id, "recurring")).IsFalse();
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await service.RunDueAsync();
		await Drain();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).LastAsync()).Value.ToPlainText()).IsEqualTo("yes");
	}

	[Test, NotInParallel]
	public async Task ScheduledAttributeHasRootQRegisters()
	{
		var context = await Setup();
		var player = (await Get<IObjectStore>().GetObjectNodeAsync(context.Actor.ActiveCharacter!.Value)).AsPlayer;
		await Get<IMediator>().Send(new SetAttributeCommand(context.Target, ["RUN"],
			MarkupText.Plain($"@set {context.Target}=FIRED:[setq(0,stored)][r(0)]"), player));
		await Create(context);
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await context.Service.RunDueAsync();
		await context.Callbacks.Single()();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).LastAsync()).Value.ToPlainText())
			.IsEqualTo("stored");
	}

	[Test, NotInParallel]
	public async Task DurableClaimsPreventDuplicateTicksAndRestartReplay()
	{
		var context = await Setup();
		var job = await Create(context);
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await context.Service.RunDueAsync();
		await context.Service.RunDueAsync();
		await Assert.That(context.Callbacks.Count).IsEqualTo(1);
		var restarted = Service(context.Clock, context.Queue, context.Capabilities);
		await restarted.InitializeAsync();
		await restarted.InitializeAsync();
		await context.Callbacks[0]();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).ToArrayAsync()).Length).IsEqualTo(0);
		await Assert.That((await restarted.ListAsync(context.Actor)).Single().Id).IsEqualTo(job.Id);
		context.Clock.Now = context.Clock.Now.AddHours(12);
		await restarted.RunDueAsync();
		await Assert.That(context.Callbacks.Count).IsEqualTo(2);
		await context.Callbacks[1]();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).LastAsync()).Value.ToPlainText()).IsEqualTo("yes");
		await Assert.That((await restarted.ListAsync(context.Actor)).Single().Status).IsEqualTo("completed");
	}

	[Test, NotInParallel]
	[Arguments("disable")]
	[Arguments("delete")]
	[Arguments("revoke")]
	[Arguments("missing-attribute")]
	[Arguments("recycled-target")]
	[Arguments("halt-target")]
	[Arguments("destroying-target")]
	public async Task QueuedWorkRechecksLifecycleIdentityAndAuthority(string change)
	{
		var context = await Setup();
		var job = await Create(context);
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await context.Service.RunDueAsync();
		if (change == "disable") await context.Service.ConfigureAsync(context.Actor, job.Id, job.Schedule, job.TimeZone, false);
		else if (change == "delete") await context.Service.DeleteAsync(context.Actor, job.Id);
		else if (change == "revoke") context.Capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
		else if (change == "missing-attribute")
		{
			var player = (await Get<IObjectStore>().GetObjectNodeAsync(context.Actor.ActiveCharacter!.Value)).Known;
			var target = (await Get<IObjectStore>().GetObjectNodeAsync(context.Target)).Known;
			await Get<IAttributeService>().ClearAttributeAsync(player, target, "RUN", IAttributeService.AttributePatternMode.Exact);
		}
		else if (change is "halt-target" or "destroying-target")
		{
			var node = (await Get<IObjectStore>().GetObjectNodeAsync(context.Target)).Known;
			var flag = await Get<IMediator>().Send(new GetObjectFlagQuery(change == "halt-target" ? "HALT" : "GOING"));
			await Get<IMediator>().Send(new SetObjectFlagCommand(node, flag!));
		}
		else
		{
			var document = await Get<IExpandedDataStore>().GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey);
			await Get<IExpandedDataStore>().SetExpandedServerData(RecurringJobService.StorageKey, document! with { Jobs = [document.Jobs.Single() with { Target = new DBRef(context.Target.Number, context.Target.CreationMilliseconds + 1).ToString() }] });
		}
		await context.Callbacks.Single()();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).ToArrayAsync()).Length).IsEqualTo(0);
	}

	[Test, NotInParallel]
	public async Task QueueRejectionIsDurableAndDoesNotRetryTheSameFiring()
	{
		var context = await Setup();
		context.Queue.EnqueueWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DBRef>())
			.Returns(new QueueAdmissionResult(null, QueueRejectionReason.OwnerLimit));
		await Create(context);
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await context.Service.RunDueAsync();
		await context.Service.RunDueAsync();
		var job = (await context.Service.ListAsync(context.Actor)).Single();
		await Assert.That(job.Status).IsEqualTo("rejected");
		await Assert.That(job.LastError).Contains("OwnerLimit");
		await Assert.That(job.RunToken).IsNull();
		await context.Queue.Received(1).EnqueueWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), "recurring", context.Actor.ActiveCharacter!.Value);
	}

	[Test, NotInParallel]
	public async Task ReschedulingInvalidatesQueuedWorkAndKeepsTheOwner()
	{
		var context = await Setup();
		var job = await Create(context);
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await context.Service.RunDueAsync();
		var changed = await context.Service.ConfigureAsync(context.Actor, job.Id, "0 9 * * *", "Asia/Tokyo", true);
		await context.Callbacks.Single()();
		await Assert.That(changed.OwnerAccount).IsEqualTo(job.OwnerAccount);
		await Assert.That(changed.NextRun).IsEqualTo(DateTimeOffset.Parse("2026-09-15T00:00:00Z").ToUnixTimeMilliseconds());
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).ToArrayAsync()).Length).IsEqualTo(0);
	}
	[Test, NotInParallel]
	public async Task OwnCapabilityCannotMutateAnotherAccountsJob()
	{
		var context = await Setup();
		var job = await Create(context);
		context.Capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.JobsManage, Arg.Any<CancellationToken>()).Returns(false);
		var other = context.Actor with { AccountId = "other-account" };
		await Assert.That((await context.Service.ListAsync(other)).Length).IsEqualTo(0);
		await Assert.ThrowsAsync<RecurringJobException>(async () => await context.Service.DeleteAsync(other, job.Id));
		await Assert.ThrowsAsync<RecurringJobException>(async () => await context.Service.ConfigureAsync(other, job.Id, job.Schedule, job.TimeZone, false));
		await Assert.ThrowsAsync<RecurringJobException>(async () => await context.Service.ListAsync(other, true));
		context.Capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.JobsManage, Arg.Any<CancellationToken>()).Returns(true);
		var updated = await context.Service.ConfigureAsync(other, job.Id, job.Schedule, job.TimeZone, false);
		await Assert.That(updated.OwnerAccount).IsEqualTo(context.Actor.AccountId);
		await Assert.That(updated.Character).IsEqualTo(context.Actor.ActiveCharacter!.Value.ToString());
	}

	[Test, NotInParallel]
	public async Task ActualQueueSuppliesAFreshBudgetToAttributeEvaluation()
	{
		var context = await Setup();
		var parser = Substitute.For<IMUSHCodeParser>();
		ParserState? state = null;
		parser.FromState(Arg.Any<ParserState>()).Returns(call => { state = call.ArgAt<ParserState>(0); return parser; });
		var observed = new TaskCompletionSource<ExecutionBudget?>(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { observed.TrySetResult(ExecutionBudget.Current); return ValueTask.FromResult<CallState?>(CallState.Empty); });
		var service = new RecurringJobService(Get<IExpandedDataStore>(), Get<IObjectStore>(), context.Capabilities,
			Get<IPermissionService>(), Get<IAttributeService>(), Get<QueueScheduler>(), parser, context.Clock);
		await service.InitializeAsync();
		await service.CreateAsync(context.Actor, new(context.Target.ToString(), "RUN", "* * * * *", "UTC"));
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await service.RunDueAsync();
		var budget = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(budget).IsNotNull();
		await Assert.That(state!.ExecutionBudget).IsEqualTo(budget);
		await Assert.That(state.Executor).IsEqualTo(context.Actor.ActiveCharacter);
		for (var attempt = 0; attempt < 100 && (await service.ListAsync(context.Actor)).Single().Status != "completed"; attempt++) await Task.Delay(10);
		await Assert.That((await service.ListAsync(context.Actor)).Single().Status).IsEqualTo("completed");
	}

	[Test, NotInParallel]
	public async Task GameCommandsCreateDisableAndDeleteThroughTheSharedService()
	{
		var context = await Setup();
		await Factory.CommandParser.CommandParse(1, Get<IConnectionService>(), MarkupText.Plain($"@job/create {context.Target}/RUN=* * * * *|UTC|game job"));
		var job = (await context.Service.ListAsync(context.Actor)).Single();
		await Assert.That(job.Description).IsEqualTo("game job");
		await Factory.CommandParser.CommandParse(1, Get<IConnectionService>(), MarkupText.Plain($"@job/disable {job.Id}"));
		await Assert.That((await context.Service.ListAsync(context.Actor)).Single().Enabled).IsFalse();
		await Factory.CommandParser.CommandParse(1, Get<IConnectionService>(), MarkupText.Plain($"@job/delete {job.Id}"));
		await Assert.That((await context.Service.ListAsync(context.Actor)).Length).IsEqualTo(0);
	}

	[Test, NotInParallel]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ExecutionRechecksDisabledAndUnlinkedAccounts(bool unlink)
	{
		var context = await Setup();
		var account = new SharpAccount { Id = context.Actor.AccountId, Username = "job-owner", PasswordHash = "", Status = AccountStatus.Active };
		var player = (await Get<IObjectStore>().GetObjectNodeAsync(context.Actor.ActiveCharacter!.Value)).AsPlayer;
		var accounts = Substitute.For<IAccountService>();
		accounts.GetByIdAsync(account.Id!, Arg.Any<CancellationToken>()).Returns(account);
		accounts.GetCharactersAsync(account.Id!, Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([player]));
		var capabilities = new AdministrativeCapabilityService(accounts, Get<IRoleRegistryService>(), Get<IRoleDerivationService>(), Get<IPermissionResolver>());
		var service = Service(context.Clock, context.Queue, capabilities);
		await service.InitializeAsync();
		await service.CreateAsync(context.Actor, new(context.Target.ToString(), "RUN", "* * * * *", "UTC"));
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await service.RunDueAsync();
		if (unlink) accounts.GetCharactersAsync(account.Id!, Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([]));
		else account.Status = AccountStatus.Disabled;
		await context.Callbacks.Single()();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).ToArrayAsync()).Length).IsEqualTo(0);
		var document = await Get<IExpandedDataStore>().GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey);
		await Assert.That(document!.Jobs.Single().Status).IsEqualTo("failed");
	}

	[Test, NotInParallel]
	public async Task QueuedAuthorizationCancelsBuiltInRoleLookup()
	{
		var context = await Setup();
		var account = new SharpAccount { Id = context.Actor.AccountId, Username = "job-owner", PasswordHash = "", Status = AccountStatus.Active };
		var player = (await Get<IObjectStore>().GetObjectNodeAsync(context.Actor.ActiveCharacter!.Value)).AsPlayer;
		var accounts = Substitute.For<IAccountService>();
		accounts.GetByIdAsync(account.Id!, Arg.Any<CancellationToken>()).Returns(account);
		accounts.GetCharactersAsync(account.Id!, Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([player]));
		var registry = Substitute.For<IRoleRegistryService>();
		var backing = Get<IRoleRegistryService>();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var armed = false;
		registry.GetRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			if (armed) await release.Task.WaitAsync(call.Arg<CancellationToken>());
			return await backing.GetRoleAsync(call.Arg<string>(), call.Arg<CancellationToken>());
		});
		registry.GetRolesForAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(call => backing.GetRolesForAccountAsync(call.Arg<string>(), call.Arg<CancellationToken>()));
		var capabilities = new AdministrativeCapabilityService(accounts, registry, Get<IRoleDerivationService>(), Get<IPermissionResolver>());
		var service = Service(context.Clock, context.Queue, capabilities);
		await service.InitializeAsync();
		await service.CreateAsync(context.Actor, new(context.Target.ToString(), "RUN", "* * * * *", "UTC"));
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await service.RunDueAsync();
		armed = true;
		using var budget = new ExecutionBudget(TimeSpan.FromMilliseconds(100));
		using var scope = budget.Enter();
		var firing = context.Callbacks.Single()().AsTask();
		try
		{
			await firing.WaitAsync(TimeSpan.FromSeconds(2));
		}
		finally
		{
			release.TrySetResult();
			await firing;
		}
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).ToArrayAsync()).Length).IsEqualTo(0);
		var document = await Get<IExpandedDataStore>().GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey);
		await Assert.That(document!.Jobs.Single().Status).IsEqualTo("failed");
	}

	[Test, NotInParallel]
	[Arguments("read")]
	[Arguments("running-save")]
	[Arguments("authorize")]
	[Arguments("executor")]
	[Arguments("target")]
	public async Task QueuedPredispatchIoReceivesTheExecutionBudget(string stage)
	{
		var context = await Setup();
		var backing = Get<IExpandedDataStore>();
		var store = Substitute.For<IExpandedDataStore>();
		var objects = Substitute.For<IObjectStore>();
		var armed = false;
		var blocked = false;
		CancellationToken observed = default;
		async Task Block(string current, CancellationToken ct)
		{
			if (!armed || blocked || current != stage) return;
			blocked = true;
			observed = ct;
			using var watchdog = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
			await Task.Delay(Timeout.InfiniteTimeSpan, ct.CanBeCanceled ? ct : watchdog.Token);
		}
		async ValueTask<RecurringJobDocument?> Read(CancellationToken ct)
		{
			await Block("read", ct);
			return await backing.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey, ct);
		}
		async ValueTask Save(RecurringJobDocument document, CancellationToken ct)
		{
			if (document.Jobs.Any(job => job.Status == "running")) await Block("running-save", ct);
			await backing.SetExpandedServerData(RecurringJobService.StorageKey, document, ct);
		}
		async ValueTask<AnyOptionalSharpObject> Object(DBRef identity, CancellationToken ct)
		{
			await Block(identity == context.Target ? "target" : "executor", ct);
			return await Get<IObjectStore>().GetObjectNodeAsync(identity, ct);
		}
		async Task<bool> Authorize(CancellationToken ct) { await Block("authorize", ct); return true; }
		store.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey, Arg.Any<CancellationToken>())
			.Returns(call => Read(call.ArgAt<CancellationToken>(1)));
		store.SetExpandedServerData(RecurringJobService.StorageKey, Arg.Any<object>(), Arg.Any<CancellationToken>())
			.Returns(call => Save(call.ArgAt<RecurringJobDocument>(1), call.ArgAt<CancellationToken>(2)));
		objects.GetObjectNodeAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(call => Object(call.ArgAt<DBRef>(0), call.ArgAt<CancellationToken>(1)));
		context.Capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(call => Authorize(call.ArgAt<CancellationToken>(2)));
		var service = new RecurringJobService(store, objects, context.Capabilities, Get<IPermissionService>(),
			Get<IAttributeService>(), context.Queue, Factory.CommandParser, context.Clock);
		await service.InitializeAsync();
		await service.CreateAsync(context.Actor, new(context.Target.ToString(), "RUN", "* * * * *", "UTC"));
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await service.RunDueAsync();
		armed = true;
		using var budget = new ExecutionBudget(TimeSpan.FromMilliseconds(100));
		using (budget.Enter()) await context.Callbacks.Single()();
		await Assert.That(blocked).IsTrue();
		await Assert.That(observed).IsEqualTo(budget.Token);
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).ToArrayAsync()).Length).IsEqualTo(0);
		var saved = await backing.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey);
		await Assert.That(saved!.Jobs.Single().Status).IsEqualTo("failed");
		await Assert.That(saved.Jobs.Single().RunToken).IsNull();
	}

	[Test, NotInParallel]
	public async Task CancelledFiringDoesNotWaitIndefinitelyForTheDefinitionGate()
	{
		var context = await Setup();
		var job = await Create(context);
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await context.Service.RunDueAsync();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		context.Capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(async _ => { entered.TrySetResult(); await release.Task; return true; });
		var configure = context.Service.ConfigureAsync(context.Actor, job.Id, job.Schedule, job.TimeZone, false);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
		using var budget = new ExecutionBudget(TimeSpan.Zero);
		Task<CallState?>? firing = null;
		try
		{
			using var scope = budget.Enter();
			firing = context.Callbacks.Single()().AsTask();
			try { await firing.WaitAsync(TimeSpan.FromSeconds(3)); }
			catch (OperationCanceledException) { }
		}
		finally
		{
			release.TrySetResult();
			await configure;
			if (firing is not null)
			{
				try { await firing.WaitAsync(TimeSpan.FromSeconds(3)); }
				catch (OperationCanceledException) { }
			}
		}
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(context.Target, ["FIRED"]).ToArrayAsync()).Length).IsEqualTo(0);
	}

	[Test, NotInParallel]
	public async Task TerminalPersistenceHasOneFreshBoundedAttemptAndRetainsUnacknowledgedClaim()
	{
		var context = await Setup();
		var backing = Get<IExpandedDataStore>();
		var store = Substitute.For<IExpandedDataStore>();
		var armed = false;
		var attempts = 0;
		var activeWrites = 0;
		CancellationToken observed = default;
		store.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey, Arg.Any<CancellationToken>())
			.Returns(call => backing.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey, call.ArgAt<CancellationToken>(1)));
		async ValueTask Save(RecurringJobDocument document, CancellationToken ct)
		{
			if (armed && document.Jobs.Any(job => job.Status is "completed" or "failed"))
			{
				attempts++;
				activeWrites++;
				observed = ct;
				using var watchdog = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
				try { await Task.Delay(Timeout.InfiniteTimeSpan, ct.CanBeCanceled ? ct : watchdog.Token); }
				finally { activeWrites--; }
			}
			await backing.SetExpandedServerData(RecurringJobService.StorageKey, document, ct);
		}
		store.SetExpandedServerData(RecurringJobService.StorageKey, Arg.Any<object>(), Arg.Any<CancellationToken>())
			.Returns(call => Save(call.ArgAt<RecurringJobDocument>(1), call.ArgAt<CancellationToken>(2)));
		var service = new RecurringJobService(store, Get<IObjectStore>(), context.Capabilities, Get<IPermissionService>(),
			Get<IAttributeService>(), context.Queue, Factory.CommandParser, context.Clock);
		await service.InitializeAsync();
		await service.CreateAsync(context.Actor, new(context.Target.ToString(), "RUN", "* * * * *", "UTC"));
		context.Clock.Now = context.Clock.Now.AddMinutes(1);
		await service.RunDueAsync();
		armed = true;
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(3));
		try { using var scope = budget.Enter(); await context.Callbacks.Single()().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); }
		catch (OperationCanceledException) { }
		await Assert.That(attempts).IsEqualTo(1);
		await Assert.That(activeWrites).IsEqualTo(0);
		await Assert.That(observed.CanBeCanceled).IsTrue();
		await Assert.That(observed.IsCancellationRequested).IsTrue();
		await Assert.That(observed == budget.Token).IsFalse();
		var document = await backing.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey);
		await Assert.That(document!.Jobs.Single().RunToken).IsNotNull();
		await service.RunDueAsync();
		await Assert.That(context.Callbacks.Count).IsEqualTo(1);
	}

}
