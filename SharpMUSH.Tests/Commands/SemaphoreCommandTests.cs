using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class SemaphoreCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	[Arguments("Notify")]
	[Arguments("Drain")]
	public async Task SemaphoreLinkPermissionReadHonorsCancellation(string command)
	{
		var objects = new SharpMUSH.Tests.Services.TestObjectFactory();
		var actor = objects.CreatePlayer(40, "actor");
		var target = objects.CreateThing(41, "semaphore");
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		async IAsyncEnumerable<SharpMUSH.Library.Models.SharpObjectFlag> Flags(
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
		{
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			yield break;
		}
		target.Object().Flags = new(() => Flags());
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObject>(actor.AsPlayer));
		var locate = Substitute.For<ILocateService>();
		locate.LocateAndNotifyIfInvalidWithCallState(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(),
			Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<LocateFlags>())
			.Returns(ValueTask.FromResult<AnySharpObjectOrErrorCallState>(target));
		locate.LocateAndNotifyIfInvalid(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(),
			Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<LocateFlags>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObjectOrError>(target.AsThing));
		var permissions = Substitute.For<IPermissionService>();
		permissions.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(false);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			WebAppFactoryArg.Services, mediator, locate, permissions);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(WebAppFactoryArg.Services);
		parser.CurrentState.Returns(ParserState.RootFor(actor.Object().DBRef) with
		{
			Arguments = new() { ["0"] = new("#41/SEMAPHORE") }
		});
		var metadata = (SharpMUSH.Library.Attributes.SharpCommandAttribute)Attribute.GetCustomAttribute(
			typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod(command)!, typeof(SharpMUSH.Library.Attributes.SharpCommandAttribute))!;
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
		using var scope = budget.Enter();
		var operation = command == "Notify" ? commands.Notify(parser, metadata).AsTask() : commands.Drain(parser, metadata).AsTask();
		try
		{
			var observed = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(1)));
			await Assert.That(observed).IsEqualTo(budget.Token);
		}
		finally
		{
			cleanup.Cancel();
			try { await operation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments("preflight")]
	[Arguments("admission")]
	[Arguments("delay")]
	public async Task WaitCancellationReachesBlockedPreflightAndQueueRequest(string phase)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(1)))).AsPlayer;
		var target = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand("wait-cancel-" + Guid.NewGuid().ToString("N"), player));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var admissions = 0;
		async Task Block(CancellationToken token)
		{
			entered.TrySetResult();
			await release.Task.WaitAsync(token);
		}
		async IAsyncEnumerable<SharpMUSH.Library.Models.SharpAttribute> Read(
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
		{
			if (phase == "preflight") await Block(token);
			yield break;
		}
		async ValueTask<SharpMUSH.Library.Models.SchedulerModels.QueueAdmissionResult> Admit(CancellationToken token)
		{
			admissions++;
			await Block(token);
			return new(1, SharpMUSH.Library.Models.SchedulerModels.QueueRejectionReason.None);
		}
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.Send(call.ArgAt<GetObjectNodeQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Read(call.ArgAt<CancellationToken>(1)));
		mediator.Send(Arg.Any<SharpMUSH.Library.Requests.AdmitCommandListWithTimeoutRequest>(), Arg.Any<CancellationToken>())
			.Returns(call => Admit(call.ArgAt<CancellationToken>(1)));
		mediator.Send(Arg.Any<SharpMUSH.Library.Requests.AdmitDelayedCommandListRequest>(), Arg.Any<CancellationToken>())
			.Returns(call => Admit(call.ArgAt<CancellationToken>(1)));
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(WebAppFactoryArg.Services, mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(WebAppFactoryArg.Services);
		parser.CurrentState.Returns(ParserState.RootFor(player.Object.DBRef) with
		{
			Arguments = new() { ["0"] = new(phase == "delay" ? "3600" : target + "/3600"), ["1"] = new("think cancelled") }
		});
		var metadata = (SharpMUSH.Library.Attributes.SharpCommandAttribute)Attribute.GetCustomAttribute(
			typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod("Wait")!, typeof(SharpMUSH.Library.Attributes.SharpCommandAttribute))!;
		using var cancellation = new CancellationTokenSource();
		using var budget = ExecutionBudget.FromMilliseconds(30000, cancellation.Token);
		using var scope = budget.Enter();
		var operation = commands.Wait(parser, metadata).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(2)));
			await Assert.That(admissions).IsEqualTo(phase == "preflight" ? 0 : 1);
		}
		finally
		{
			release.TrySetResult();
			try { await operation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments(0, "none")]
	[Arguments(0, "flag")]
	[Arguments(0, "child")]
	[Arguments(1, "none")]
	[Arguments(2, "none")]
	[Arguments(3, "none")]
	[Arguments(2, "flag")]
	[Arguments(2, "child")]
	public async Task NotifyRepairsPartialCustomCounterInitialization(int failAt, string concurrentEdit)
	{
		using var budget = ExecutionBudget.FromMilliseconds(30000);
		using var scope = budget.Enter();
		var player = (await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(1)))).AsPlayer;
		var target = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand("notify-repair-" + Guid.NewGuid().ToString("N"), player));
		string[] path = ["CUSTOM_COUNTER"];
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.Send(call.ArgAt<GetObjectNodeQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.CreateStream(call.ArgAt<GetAttributeQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.CreateStream(Arg.Any<GetAllAttributeEntriesQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.CreateStream(call.ArgAt<GetAllAttributeEntriesQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.CreateStream(Arg.Any<GetAttributeFlagsQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.CreateStream(call.ArgAt<GetAttributeFlagsQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>())
			.Returns(call => SetCount(call));
		async ValueTask<bool> SetCount(NSubstitute.Core.CallInfo call)
		{
			var written = await Mediator.Send(call.ArgAt<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(0), call.ArgAt<CancellationToken>(1));
			if (failAt != 0) return written;
			var current = await Mediator.CreateStream(new GetAttributeQuery(target, path)).LastAsync();
			if (concurrentEdit == "flag")
			{
				var flag = await Mediator.CreateStream(new GetAttributeFlagsQuery()).FirstAsync(x => x.Name.Equals("wizard", StringComparison.OrdinalIgnoreCase));
				await Mediator.Send(new SharpMUSH.Library.Commands.Database.SetAttributeFlagCommand(target, current, flag));
			}
			else if (concurrentEdit == "child")
				await Mediator.Send(new SharpMUSH.Library.Commands.Database.SetAttributeCommand(target, ["CUSTOM_COUNTER", "CHILD"], MarkupText.Plain("preserve"), player));
			return false; // The count committed, but no creation identity or flag was captured yet.
		}
		var flagCalls = 0;
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeFlagCommand>(), Arg.Any<CancellationToken>())
			.Returns(call => FailFlag(call));
		async ValueTask<bool> FailFlag(NSubstitute.Core.CallInfo call)
		{
			var command = call.ArgAt<SharpMUSH.Library.Commands.Database.SetAttributeFlagCommand>(0);
			if (++flagCalls != failAt) return await Mediator.Send(command, call.ArgAt<CancellationToken>(1));
			if (concurrentEdit == "flag")
			{
				var flag = await Mediator.CreateStream(new GetAttributeFlagsQuery()).FirstAsync(x => x.Name.Equals("wizard", StringComparison.OrdinalIgnoreCase));
				await Mediator.Send(new SharpMUSH.Library.Commands.Database.SetAttributeFlagCommand(target, command.Target, flag));
			}
			else if (concurrentEdit == "child")
				await Mediator.Send(new SharpMUSH.Library.Commands.Database.SetAttributeCommand(target, ["CUSTOM_COUNTER", "CHILD"], MarkupText.Plain("preserve"), player));
			return false;
		}
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(WebAppFactoryArg.Services, mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(WebAppFactoryArg.Services);
		parser.CurrentState.Returns(ParserState.RootFor(player.Object.DBRef) with { Arguments = new() { ["0"] = new(target + "/CUSTOM_COUNTER") } });
		var metadata = (SharpMUSH.Library.Attributes.SharpCommandAttribute)Attribute.GetCustomAttribute(
			typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod("Notify")!, typeof(SharpMUSH.Library.Attributes.SharpCommandAttribute))!;
		try
		{
			try { await commands.Notify(parser, metadata); }
			catch (InvalidOperationException) { }
			catch (AggregateException) { }
			if (concurrentEdit == "none")
			{
				using var lease = await Scheduler.EnterSemaphoreMutationAsync();
				var current = await Mediator.CreateStream(new GetAttributeQuery(target, path)).LastAsync();
				await Assert.That(current.Value.ToPlainText()).IsEqualTo("-1");
				await Assert.That(current.Flags.Select(x => x.Name.ToLowerInvariant()).Order().ToArray())
					.IsEquivalentTo(new[] { "locked", "no_clone", "no_inherit" });
			}
			else
			{
				await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var lease = await Scheduler.EnterSemaphoreMutationAsync(); });
				await Assert.That(flagCalls).IsEqualTo(failAt);
				var current = await Mediator.CreateStream(new GetAttributeQuery(target, path)).LastAsync();
				await Assert.That(current.Value.ToPlainText()).IsEqualTo("-1");
				if (concurrentEdit == "flag") await Assert.That(current.Flags.Any(x => x.Name.Equals("wizard", StringComparison.OrdinalIgnoreCase))).IsTrue();
				else await Assert.That((await Mediator.CreateStream(new GetAttributeQuery(target, ["CUSTOM_COUNTER", "CHILD"])).LastAsync()).Value.ToPlainText()).IsEqualTo("preserve");
			}
		}
		finally
		{
			await Mediator.Send(new SharpMUSH.Library.Commands.Database.WipeAttributeCommand(target, path));
			using var lease = await Scheduler.EnterSemaphoreMutationAsync();
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task QueuedMalformedSemaphoreCommandNotifiesTheExecutor(bool drain)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(1)))).AsPlayer;
		var target = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand("invalid-notice-" + Guid.NewGuid().ToString("N"), player));
		var value = "invalid-" + Guid.NewGuid().ToString("N");
		await Assert.That(await Mediator.Send(new SharpMUSH.Library.Commands.Database.SetAttributeCommand(target, ["SEMAPHORE"], MarkupText.Plain(value), player))).IsTrue();
		var command = (drain ? "@drain " : "@notify ") + target + "/SEMAPHORE";
		var admitted = await Scheduler.AdmitCommandList(MarkupText.Plain(command), ParserState.RootFor(player.Object.DBRef));
		await Assert.That(admitted.Accepted).IsTrue();
		var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var sentinel = await Scheduler.AdmitWork(() => { completed.TrySetResult(); return ValueTask.FromResult<CallState?>(null); }, "notice-sentinel", "test");
		await Assert.That(sentinel.Accepted).IsTrue();
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(player.Object.DBRef),
			TestHelpers.MatchingMessage($"Semaphore attribute must have a numeric or empty value. Current value: {value}"),
			TestHelpers.MatchingObject(player.Object.DBRef));
		await Assert.That((await Mediator.CreateStream(new GetAttributeQuery(target, ["SEMAPHORE"])).LastAsync()).Value.ToPlainText()).IsEqualTo(value);
	}

	[Test]
	[Arguments(false, "invalid")]
	[Arguments(true, "invalid")]
	[Arguments(false, "2147483648")]
	[Arguments(true, "2147483648")]
	public async Task InvalidDefaultCounterReturnsACommandErrorWithoutChangingQueue(bool drain, string value)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(1)))).AsPlayer;
		var target = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand("invalid-counter-" + Guid.NewGuid().ToString("N"), player));
		var semaphore = new SharpMUSH.Library.Models.DbRefAttribute(target, ["SEMAPHORE"]);
		var state = ParserState.RootFor(player.Object.DBRef);
		var admitted = await Scheduler.AdmitCommandList(MarkupText.Plain("think must-remain-pending"), state, semaphore, 0, TimeSpan.FromHours(1), manageSemaphoreCount: true);
		await Assert.That(admitted.Accepted).IsTrue();
		try
		{
			await Mediator.Send(new SharpMUSH.Library.Commands.Database.SetAttributeCommand(target, ["SEMAPHORE"], MarkupText.Plain(value), player));
			var usage = Scheduler.GetQueueUsage().Total;
			var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(WebAppFactoryArg.Services);
			var parser = Substitute.For<IMUSHCodeParser>();
			parser.ServiceProvider.Returns(WebAppFactoryArg.Services);
			parser.CurrentState.Returns(state with { Arguments = new() { ["0"] = new(target + "/SEMAPHORE") } });
			var metadata = (SharpMUSH.Library.Attributes.SharpCommandAttribute)Attribute.GetCustomAttribute(
				typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod(drain ? "Drain" : "Notify")!, typeof(SharpMUSH.Library.Attributes.SharpCommandAttribute))!;
			var result = drain ? await commands.Drain(parser, metadata) : await commands.Notify(parser, metadata);
			await Assert.That(result.IsSome()).IsTrue();
			await Assert.That(result.AsValue().Message!.ToPlainText()).Contains("Semaphore attribute must have a numeric or empty value");
			await Assert.That(Scheduler.GetQueueUsage().Total).IsEqualTo(usage);
			await Assert.That((await Mediator.CreateStream(new GetAttributeQuery(target, ["SEMAPHORE"])).LastAsync()).Value.ToPlainText()).IsEqualTo(value);
			using var lease = await Scheduler.EnterSemaphoreMutationAsync();
			await Assert.That(await Scheduler.DrainCounted(semaphore)).IsEqualTo(1);
		}
		finally { await Scheduler.HaltByPid(admitted.Pid!.Value); }
	}

	[Test]
	public async Task DrainOfAnAbsentSemaphoreIsANoOp()
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(1)))).AsPlayer;
		var target = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand("empty-drain-" + Guid.NewGuid().ToString("N"), player));
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.Send(call.ArgAt<GetObjectNodeQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.CreateStream(call.ArgAt<GetAttributeQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.ClearAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(false);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(WebAppFactoryArg.Services, mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(WebAppFactoryArg.Services);
		parser.CurrentState.Returns(ParserState.RootFor(player.Object.DBRef) with { Arguments = new() { ["0"] = new(target + "/SEMAPHORE") } });
		var metadata = (SharpMUSH.Library.Attributes.SharpCommandAttribute)Attribute.GetCustomAttribute(
			typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod("Drain")!, typeof(SharpMUSH.Library.Attributes.SharpCommandAttribute))!;
		await commands.Drain(parser, metadata);
		await mediator.DidNotReceive().Send(Arg.Any<SharpMUSH.Library.Commands.Database.ClearAttributeCommand>(), Arg.Any<CancellationToken>());
		await Assert.That(await Mediator.CreateStream(new GetAttributeQuery(target, ["SEMAPHORE"])).CountAsync()).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RejectedCommandCounterWriteRetainsTheWaitingEntry(bool drain)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(1)))).AsPlayer;
		var target = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand("command-accounting-" + Guid.NewGuid().ToString("N"), player));
		var semaphore = new SharpMUSH.Library.Models.DbRefAttribute(target, ["SEMAPHORE"]);
		var state = ParserState.RootFor(player.Object.DBRef);
		var admitted = await Scheduler.AdmitCommandList(MarkupText.Plain("think accounting-finished"), state,
			semaphore, 0, TimeSpan.FromHours(1), manageSemaphoreCount: true);
		await Assert.That(admitted.Accepted).IsTrue();
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.Send(call.ArgAt<GetObjectNodeQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.CreateStream(call.ArgAt<GetAttributeQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(false);
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.ClearAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(false);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(WebAppFactoryArg.Services, mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(WebAppFactoryArg.Services);
		parser.CurrentState.Returns(state with { Arguments = new() { ["0"] = new(target + "/SEMAPHORE") } });
		var metadata = (SharpMUSH.Library.Attributes.SharpCommandAttribute)Attribute.GetCustomAttribute(
			typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod(drain ? "Drain" : "Notify")!, typeof(SharpMUSH.Library.Attributes.SharpCommandAttribute))!;
		try
		{
			using var budget = ExecutionBudget.FromMilliseconds(30000);
			using var scope = budget.Enter();
			await Assert.ThrowsAsync<InvalidOperationException>(async () =>
			{
				if (drain) await commands.Drain(parser, metadata); else await commands.Notify(parser, metadata);
			});
			var writeTokens = mediator.ReceivedCalls().Where(call => call.GetArguments().FirstOrDefault() is
				SharpMUSH.Library.Commands.Database.SetAttributeCommand or SharpMUSH.Library.Commands.Database.ClearAttributeCommand)
				.SelectMany(call => call.GetArguments().OfType<CancellationToken>()).ToArray();
			await Assert.That(writeTokens.Length).IsEqualTo(1);
			await Assert.That(writeTokens[0]).IsEqualTo(budget.Token);
			await Assert.That((await Mediator.CreateStream(new GetAttributeQuery(target, ["SEMAPHORE"])).LastAsync()).Value.ToPlainText()).IsEqualTo("1");
			using (await Scheduler.EnterSemaphoreMutationAsync())
				await Assert.That(await Scheduler.DrainCounted(semaphore)).IsEqualTo(1);
		}
		finally { await Scheduler.HaltByPid(admitted.Pid!.Value); }
	}

	[Test]
	public async ValueTask NotifyCommand_ShouldWakeWaitingTask()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemNotify");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		var testMessage = $"TaskExecuted_{uniqueId}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait {semObj}/{uniqueAttr}=think {testMessage}"));

		await Task.Delay(200);

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@notify {semObj}/{uniqueAttr}"));

		await Task.Delay(2000);

		await NotifyService.Received(1).Notify(
			TestHelpers.MatchingObject(executor),
			TestHelpers.MatchingMessage(testMessage), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DolistInline_ShouldExecuteImmediately()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var uniqueId = Guid.NewGuid().ToString("N");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dolist/inline a b c=@pemit #1=Inline{uniqueId}"));

		// @dolist/inline a b c fires 3 iterations, each @pemit emits the same unique string.
		// Received(3) is the exact count: one for element "a", one for "b", one for "c".
		await NotifyService.Received(3).Notify(
			TestHelpers.MatchingObject(executor),
			TestHelpers.MatchingMessage($"Inline{uniqueId}"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test, Skip("Needs a better way of testing. This is too timing sensitive.")]
	public async ValueTask DolistDefault_ShouldQueueCommands()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var uniqueId = Guid.NewGuid().ToString("N");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dolist a b c=@pemit #1=Queued{uniqueId}"));

		// @dolist (without /inline) queues commands; they must NOT execute synchronously.
		await NotifyService
			.DidNotReceive()
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"Queued{uniqueId}")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NotifySetQ_CommandShouldAcceptParameters()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Guards against CB.RSArgs interfering with comma parsing of qreg parameters.
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemSetQParam");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";

		var result = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@notify/setq {semObj}/{uniqueAttr}=0,TestValue"));

		// The command should not generate a parsing error about pairs.
		// It might say "no queue entry" but must NOT say the pairs-error message.
		await NotifyService.DidNotReceive().Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Q-register assignments must be in pairs: qreg,value[,qreg,value...]")),
			TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NotifySetQ_ShouldSetQRegisterForWaitingTask()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemSetQWait");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		var testValue = $"TestValue_{uniqueId.Substring(0, 8)}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait {semObj}/{uniqueAttr}=think QRegValue:%q0"));

		await Task.Delay(200);

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@notify/setq {semObj}/{uniqueAttr}=0,{testValue}"));

		await Task.Delay(2000);

		await NotifyService.Received(1).Notify(
			TestHelpers.MatchingObject(executor),
			TestHelpers.MatchingMessage($"QRegValue:{testValue}"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DrainCommand_Basic()
	{
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemDrain");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";

		// drain (with nothing queued) - should not throw exception
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@drain {semObj}/{uniqueAttr}"));

		// No assertion - just verify no exceptions
	}

	[Test]
	public async ValueTask DrainCommandSubtractsActualWaitersAndClearsCredits()
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CountedDrain");
		var attribute = $"SEM_{Guid.NewGuid():N}";
		async ValueTask Command(string command) => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		async ValueTask<string> Count() => (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({target}/{attribute})")))!.Message!.ToPlainText();
		await Command($"@wait {target}/{attribute}=think first");
		await Command($"@wait {target}/{attribute}=think second");
		await Assert.That(await Count()).IsEqualTo("2");
		await Command($"@drain {target}/{attribute}=1");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command($"@drain {target}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("");
		await Command($"@notify {target}/{attribute}=2");
		await Assert.That(await Count()).IsEqualTo("-2");
		await Command($"@drain {target}/{attribute}=1");
		await Assert.That(await Count()).IsEqualTo("-2");
		await Command($"@drain/all {target}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("");
		await Command($"@wait {target}/{attribute}=think reused");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command($"@drain {target}/{attribute}");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async ValueTask ReleaseAllPreservesPublishedTimeoutAccountingBeforeNewWait(bool notifyAll)
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "DrainTimeout");
		var attribute = $"SEM_{Guid.NewGuid():N}";
		var semaphore = new SharpMUSH.Library.Models.DbRefAttribute(
			target, [attribute]);
		var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		async ValueTask Command(string command) => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		async ValueTask<string> Count() => (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({target}/{attribute})")))!.Message!.ToPlainText();
		await Scheduler.AdmitWork(async () => { blocked.SetResult(); await release.Task; return null; }, "drain-block", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var timeout = await Scheduler.AdmitCommandList(MarkupText.Plain("think timeout"), WebAppFactoryArg.FunctionParser.CurrentState,
				semaphore, 0, manageSemaphoreCount: true);
			await Command($"@wait {target}/{attribute}=think pending");
			await Scheduler.ReleaseScheduledWork(timeout.Pid!.Value, semaphoreTimeout: true);
			await Command($"@{(notifyAll ? "notify" : "drain")}/all {target}/{attribute}");
			await Assert.That(await Count()).IsIn("", "0");
			await Command($"@wait {target}/{attribute}=think later");
			await Assert.That(await Count()).IsEqualTo("1");
			await Scheduler.AdmitWork(() => { completed.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "drain-complete", "test");
		}
		finally { release.SetResult(); }
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(await Count()).IsEqualTo("1");
		await Command($"@drain {target}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("");
	}

	[Test]
	public async ValueTask NotifyCreditSurvivesAlreadyPublishedManagedTimeout()
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TimeoutCredit");
		var attribute = $"SEM_{Guid.NewGuid():N}";
		var semaphore = new SharpMUSH.Library.Models.DbRefAttribute(target, [attribute]);
		var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await Scheduler.AdmitWork(async () => { blocked.SetResult(); await release.Task; return null; }, "credit-block", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var timeout = await Scheduler.AdmitCommandList(MarkupText.Plain("think timeout"),
				WebAppFactoryArg.FunctionParser.CurrentState, semaphore, 0, manageSemaphoreCount: true);
			await Scheduler.ReleaseScheduledWork(timeout.Pid!.Value, semaphoreTimeout: true);
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@notify {target}/{attribute}"));
			await Scheduler.AdmitWork(() => { completed.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "credit-complete", "test");
		}
		finally { release.SetResult(); }
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var result = (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({target}/{attribute})")))!.Message!.ToPlainText();
		await Assert.That(result).IsEqualTo("-1");
	}

	[Test]
	public async ValueTask OrdinaryOwnerCanCreateAndConsumeCustomSemaphoreCredits()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "SemaphoreOwner");
		var outsider = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "SemaphoreOutsider");
		var attribute = $"SEM_{Guid.NewGuid():N}";
		async ValueTask Command(long handle, string command) => await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		async ValueTask<string> Count() => (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({player.DbRef}/{attribute})")))!.Message!.ToPlainText();
		await Command(player.Handle, $"@notify me/{attribute}=2");
		await Assert.That(await Count()).IsEqualTo("-2");
		var created = await Mediator.CreateStream(new GetAttributeQuery(player.DbRef, [attribute])).LastAsync();
		await Assert.That((await created.Owner.WithCancellation(CancellationToken.None))!.Object.Key).IsEqualTo(1);
		await Assert.That(created.Flags.Select(x => x.Name).ToArray()).Contains("locked");
		await Command(player.Handle, $"@wait me/{attribute}=think first credit");
		await Assert.That(await Count()).IsEqualTo("-1");
		await Command(player.Handle, $"@wait me/{attribute}=think second credit");
		await Command(player.Handle, $"@wait me/{attribute}=think pending");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command(outsider.Handle, $"@notify {player.DbRef}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command(outsider.Handle, $"@drain {player.DbRef}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command(player.Handle, $"@notify me/{attribute}");
		await Assert.That(await Count()).IsEqualTo("0");
		await Command(1, $"@set {player.DbRef}=LINK_OK");
		await Command(outsider.Handle, $"@notify {player.DbRef}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("-1");
		await Command(outsider.Handle, $"@drain {player.DbRef}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("");
	}

	[Test]
	public async ValueTask WaitCommand_WithTime_CanExecute()
	{
		var uniqueId = Guid.NewGuid().ToString("N");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait 1=@pemit #1=Wait{uniqueId}"));

		// No assertion - just verify no exceptions and command parses.
		// We don't wait for execution as this tests command parsing, not scheduler execution.
	}

	/// <summary>
	/// Verifies that @wait callbacks with multiple semicolon-separated commands in braces
	/// execute ALL commands, not just the first one.
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_MultipleCommandsInBraces()
	{
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitMulti");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var attrA = $"WAITMULTI_A_{uniqueId}";
		var attrB = $"WAITMULTI_B_{uniqueId}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait 1={{&{attrA} {testObj}=valueA; &{attrB} {testObj}=valueB}}"));

		await Task.Delay(3000);

		var obj = await Mediator.Send(new GetObjectNodeQuery(testObj));

		var attrResultA = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, attrA,
			IAttributeService.AttributeMode.Read, false);
		var attrResultB = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, attrB,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attrResultA.IsAttribute).IsTrue()
			.Because($"First command in @wait callback should set {attrA}");
		await Assert.That(attrResultA.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("valueA");

		await Assert.That(attrResultB.IsAttribute).IsTrue()
			.Because($"Second command in @wait callback should set {attrB}");
		await Assert.That(attrResultB.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("valueB");
	}

	/// <summary>
	/// PennMUSH compatibility regression test.
	///
	/// On PennMUSH, <c>@wait 1={&amp;attr obj=[add(1,1)]}</c> evaluates the function call
	/// <c>[add(1,1)]</c> when the callback fires, resulting in the attribute being set to the
	/// string <c>"2"</c> (the computed result), NOT the literal text <c>"[add(1,1)]"</c>.
	///
	/// ## PennMUSH source proof (command.c lines 1424-1432, PennMUSH 1.8.8)
	///
	/// The ATTRIB_SET internal command (which handles both <c>&amp;</c> and <c>@</c>-style
	/// attribute setting) is registered as:
	/// <code>
	///   {"ATTRIB_SET", NULL, command_atrset,
	///    CMD_T_ANY | CMD_T_EQSPLIT | CMD_T_NOGAGGED | CMD_T_INTERNAL, 0, 0}
	/// </code>
	/// Crucially, it has <em>neither</em> <c>CMD_T_NOPARSE</c> nor <c>CMD_T_RS_NOPARSE</c>,
	/// so the normal path evaluates both sides of the <c>=</c>.
	///
	/// However, there is a special-case for direct player input (command.c ~line 1425):
	/// <code>
	///   if ((cmd->func == command_atrset) &amp;&amp;
	///       (queue_entry->queue_type &amp; QUEUE_NOLIST)) {
	///     // Special case: eqsplit, noeval of rhs only
	///     command_argparse(..., rs, ..., noeval=1, ...);  // RHS NOT evaluated
	///     SW_SET(sw, SWITCH_NOEVAL);
	///   } else {
	///     // Normal path: both sides evaluated (noeval=false)
	///     command_argparse(..., rs, ..., noeval=0, ...);  // RHS IS evaluated
	///   }
	/// </code>
	/// When typed at the player prompt, <c>QUEUE_NOLIST</c> is set → RHS stored as-is (code).
	/// When run from a command queue (<c>@wait</c> callback), <c>QUEUE_NOLIST</c> is NOT set →
	/// RHS is evaluated and the result is stored.
	///
	/// ## Empirical proof (live PennMUSH 1.8.8 session)
	/// <code>
	///   &amp;DIRECT_TEST testobject=[add(1,1)]
	///   think DIRECT_RESULT:[get(testobject/DIRECT_TEST)]  →  [add(1,1)]  (literal, not evaluated)
	///
	///   @wait 0={&amp;WAIT_TEST testobject=[add(1,1)]}
	///   think WAIT_RESULT:[get(testobject/WAIT_TEST)]      →  2           (evaluated!)
	///
	///   @wait 0={&amp;WAIT_MATH testobject=[add(3,4)]}
	///   think MATH_RESULT:[get(testobject/WAIT_MATH)]      →  7           (3+4=7, evaluated!)
	/// </code>
	///
	/// ## Root cause in SharpMUSH
	/// SharpMUSH declares <c>&amp;</c> (SetAttribute) with <see cref="CommandBehavior.NoParse"/>
	/// unconditionally. In <c>ArgumentSplit</c> (SharpMUSHParserVisitor), NoParse commands place
	/// their RHS into a <see cref="CallState"/> whose <c>Message</c> is the raw unevaluated
	/// string; the deferred <c>ParsedMessage</c> lambda is never consumed by
	/// <c>SetAttribute</c>, which reads <c>args["2"].Message!</c> directly.
	///
	/// The correct fix must evaluate the RHS when <c>&amp;</c> runs from a command queue context
	/// (equivalent to PennMUSH's non-QUEUE_NOLIST path) without evaluating it during direct
	/// player input or when storing <c>$pattern:code</c> attribute values.
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_EvaluatesFunctionsInAmpersandCallback()
	{
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitEvalAttr");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"EVALTEST_{uniqueId[..8].ToUpper()}";

		// Unix timestamp "1" is in the far past, so Quartz fires the job immediately.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait 1={{&{uniqueAttr} {testObj}=[add(1,1)]}}"));

		await Task.Delay(2000);

		// PennMUSH evaluates [add(1,1)] → "2" before storing the attribute.
		// SharpMUSH currently stores the literal "[add(1,1)]" instead (the bug).
		var obj = await Mediator.Send(new GetObjectNodeQuery(testObj));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, uniqueAttr,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue();
		await Assert.That(attr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("2");
	}

	/// <summary>
	/// Tests the BBS-style pattern: user-defined command creates an object, then @wait
	/// callback uses num() + setr() to get its dbref and store it in an attribute.
	/// Simulates the +bbnewgroup flow: $cmd *:@create %0; @wait 1={&amp;groups store=[num(%0)]}
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_PatternMatchPreservesPercentZero()
	{
		var storeObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitStore");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var uniqueAttr = $"WAITSTOR_{uniqueId}";
		var cmdAttr = $"CMD_WAITST_{uniqueId}";

		var cmdPattern = $"$+waitstore_{uniqueId.ToLower()} *:@create %0; @wait 1={{&{uniqueAttr} {storeObj}=[num(%0)]}}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&{cmdAttr} {storeObj}={cmdPattern}"));

		var targetName = $"WaitTgt_{uniqueId}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"+waitstore_{uniqueId.ToLower()} {targetName}"));

		await Task.Delay(3000);

		var storeObjNode = await Mediator.Send(new GetObjectNodeQuery(storeObj));
		var attr = await AttributeService.GetAttributeAsync(storeObjNode.Known, storeObjNode.Known, uniqueAttr,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue()
			.Because($"&{uniqueAttr} should have been set by the @wait callback");

		var attrValue = attr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(attrValue).StartsWith("#")
			.Because($"num(%0) in the @wait callback should resolve to a dbref like #N, but got: {attrValue}");
		await Assert.That(attrValue).DoesNotContain("-1")
			.Because($"num(%0) should find the created object, not return #-1. Got: {attrValue}");
	}

	/// <summary>
	/// Simulates the BBS +bbnewgroup flow more closely:
	/// $pattern *:@switch hasflag(%#,wizard)=1, {@create %0; @wait 1={@switch [setr(0,num(%0))]=#-1,...,{&groups store=%q0}}}
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_BBSNewGroupFlow()
	{
		var storeObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "BBSFlow");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var grpAttr = $"GROUPS_{uniqueId}";
		var cmdAttr = $"CMD_BBSFL_{uniqueId}";

		var cmdPattern = $"$+bbsflow_{uniqueId.ToLower()} *:@switch hasflag(%#,wizard)=1,{{@create %0; @wait 1={{@switch [setr(0,num(%0))]=#-1,{{@pemit %#=Bad name}},{{&{grpAttr} {storeObj}=%q0}}}}}}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&{cmdAttr} {storeObj}={cmdPattern}"));

		var targetName = $"BBSTgt_{uniqueId}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"+bbsflow_{uniqueId.ToLower()} {targetName}"));

		await Task.Delay(3000);

		var storeObjNode = await Mediator.Send(new GetObjectNodeQuery(storeObj));
		var attr = await AttributeService.GetAttributeAsync(storeObjNode.Known, storeObjNode.Known, grpAttr,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue()
			.Because($"&{grpAttr} should have been set by the @wait callback's @switch non-#-1 branch");

		var attrValue = attr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(attrValue).StartsWith("#")
			.Because($"The groups attribute should contain a dbref, but got: {attrValue}");
		await Assert.That(attrValue).DoesNotContain("-1")
			.Because($"num(%0) should find the created object, not return #-1. Got: {attrValue}");
	}
	[Test]
	public async Task FirstWaiterAndTimeoutKeepSemaphoreCountConsistent()
	{
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemBudgetCount");
		var name = "COUNT_" + Guid.NewGuid().ToString("N");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wait {semObj}/{name}=think timeout"));
		var obj = await Mediator.Send(new GetObjectNodeQuery(semObj));
		var initial = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, name, IAttributeService.AttributeMode.Read, false);
		await Assert.That(initial.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("1");
		var tasks = await Scheduler.GetSemaphoreTasks(new SharpMUSH.Library.Models.DbRefAttribute(semObj, [name])).ToArrayAsync();
		await Scheduler.RescheduleSemaphoreTask(tasks.Single().Pid, TimeSpan.Zero);
		var count = "1";
		for (var attempt = 0; attempt < 50 && count != "0"; attempt++)
		{
			await Task.Delay(100);
			var current = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, name, IAttributeService.AttributeMode.Read, false);
			count = current.AsAttribute.Last().Value.ToPlainText();
		}
		await Assert.That(count).IsEqualTo("0");
	}

	[Test]
	public async Task TimeoutAfterSemaphoreResetCannotCreateNotifyCredit()
	{
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemResetCount");
		var name = "COUNT_" + Guid.NewGuid().ToString("N");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wait {semObj}/{name}=think timeout"));
		var tasks = await Scheduler.GetSemaphoreTasks(new SharpMUSH.Library.Models.DbRefAttribute(semObj, [name])).ToArrayAsync();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{name} {semObj}=0"));
		await Scheduler.ReleaseScheduledWork(tasks.Single().Pid, semaphoreTimeout: true);
		var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await Scheduler.AdmitWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "reset-drained", "test");
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var obj = await Mediator.Send(new GetObjectNodeQuery(semObj));
		var current = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, name, IAttributeService.AttributeMode.Read, false);
		await Assert.That(current.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("0");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HaltingExecutorOrSemaphoreTargetReleasesPendingReservation(bool haltTarget)
	{
		var executor = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemHaltExecutor");
		var semaphore = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemHaltTarget");
		var name = "COUNT_" + Guid.NewGuid().ToString("N");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{name} {semaphore}=1"));
		var attribute = new SharpMUSH.Library.Models.DbRefAttribute(semaphore, [name]);
		var before = Scheduler.GetQueueUsage().Total;
		var admitted = await Scheduler.AdmitCommandList(MarkupText.Plain("think ignored"), ParserState.RootFor(executor), attribute, 1, TimeSpan.FromHours(1));
		await Assert.That(admitted.Accepted).IsTrue();
		var haltedObject = haltTarget ? semaphore : executor;
		var incarnation = (await Mediator.Send(new GetObjectNodeQuery(haltedObject))).AsThing.Object.DBRef;
		await Scheduler.Halt(new SharpMUSH.Library.Models.DBRef(haltedObject.Number, incarnation.CreationMilliseconds + 1));
		await Assert.That(Scheduler.GetQueueUsage().Total).IsEqualTo(before + 1);
		await Scheduler.Halt(new SharpMUSH.Library.Models.DBRef(haltedObject.Number));
		await Assert.That(Scheduler.GetQueueUsage().Total).IsEqualTo(before);
		await Assert.That((await Scheduler.GetSemaphoreTasks(attribute).ToArrayAsync()).Length).IsEqualTo(0);
		var obj = (await Mediator.Send(new GetObjectNodeQuery(semaphore))).Known;
		var current = await AttributeService.GetAttributeAsync(obj, obj, name, IAttributeService.AttributeMode.Read, false);
		await Assert.That(current.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("0");
	}


	private async Task<string> SemaphoreCountAsync(object semObj, string attr)
		=> (await Parser.FunctionParse(MarkupText.Plain($"get({semObj}/{attr})")))?.Message?.ToPlainText() ?? string.Empty;

	/// <summary>
	/// PennMUSH's semaphore attribute holds the number of tasks waiting on it. A parking @wait is
	/// <c>add_to_sem(thing, 1, aname)</c> (<c>src/cque.c:1615</c>), and <c>add_to_generic</c> reads a
	/// missing attribute as zero before adding — so the first @wait on a fresh object leaves 1, not 0.
	/// </summary>
	[Test]
	public async ValueTask FirstWaitOnAFreshSemaphore_LeavesTheCountAtOne()
	{
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemFresh");
		var attr = $"SEM_{Guid.NewGuid():N}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait {semObj}/{attr}=think ignored"));
		await Task.Delay(400);

		await Assert.That(await SemaphoreCountAsync(semObj, attr)).IsEqualTo("1")
			.Because("the semaphore attribute counts waiting tasks, and exactly one task is waiting");
	}

	/// <summary>
	/// One @notify releases the one waiter and takes the count back to zero. It may only go negative
	/// when a notify finds fewer waiters than it was asked to release — PennMUSH does that
	/// deliberately (<c>src/cque.c:1438-1441</c>: "If @notify and count was higher than the number of
	/// queue entries, make the semaphore go negative") — which is not this case.
	/// </summary>
	[Test]
	public async ValueTask NotifyingTheOnlyWaiter_LeavesTheCountAtZero()
	{
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemZero");
		var attr = $"SEM_{Guid.NewGuid():N}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait {semObj}/{attr}=think ignored"));
		await Task.Delay(400);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@notify {semObj}/{attr}"));
		await Task.Delay(800);

		await Assert.That(await SemaphoreCountAsync(semObj, attr)).IsEqualTo("0")
			.Because("releasing the only waiter returns the count to zero, not below it");
	}

	/// <summary>
	/// The symptom the miscount produces: a semaphore driven negative by its first wait/notify cycle
	/// reads as "a notify is already banked", so the NEXT @wait on that object runs its command
	/// immediately instead of parking. One stray @wait poisons the semaphore for every later use.
	/// </summary>
	[Test]
	public async ValueTask AWaitAfterACompletedCycle_StillParksInsteadOfFiringImmediately()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemCycle");
		var attr = $"SEM_{Guid.NewGuid():N}";
		var token = $"SecondWait_{Guid.NewGuid():N}";

		// First cycle: park a task, then release it.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait {semObj}/{attr}=think first"));
		await Task.Delay(400);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@notify {semObj}/{attr}"));
		await Task.Delay(800);

		// Second cycle: this must PARK, not run.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait {semObj}/{attr}=think {token}"));
		await Task.Delay(800);

		await NotifyService.DidNotReceive().Notify(
			TestHelpers.MatchingObject(executor),
			TestHelpers.MatchingMessage(token), TestHelpers.MatchingObject(executor),
			INotifyService.NotificationType.Announce);

		// ...and still runs once released, so the test cannot pass by the task being lost.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@notify {semObj}/{attr}"));
		await Task.Delay(1200);

		await NotifyService.Received(1).Notify(
			TestHelpers.MatchingObject(executor),
			TestHelpers.MatchingMessage(token), TestHelpers.MatchingObject(executor),
			INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// The semaphore attribute is stamped LOCKED and owned by God, so a write to it through the
	/// permission-checked service is refused for an ordinary player. @NOTIFY updates the count that
	/// way and discards the result, which would leave the count stuck while the task was released —
	/// every later @wait then inflating a stale count. Exercised as a non-wizard, because the rest of
	/// this suite runs as #1 and cannot see it.
	/// </summary>
	[Test]
	public async ValueTask NonWizardNotify_ActuallyDecrementsTheStoredCount()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SemMortalOwner");
		var attr = $"SEM_{Guid.NewGuid():N}";

		await Parser.CommandParse(owner.Handle, ConnectionService,
			MarkupText.Plain($"@wait me/{attr}=think ignored"));
		await Task.Delay(400);

		await Assert.That(await MortalSemaphoreCountAsync(owner, attr)).IsEqualTo("1")
			.Because("the mortal's own first @wait counts itself, exactly as God's does");

		await Parser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@notify me/{attr}"));
		await Task.Delay(800);

		await Assert.That(await MortalSemaphoreCountAsync(owner, attr)).IsEqualTo("0")
			.Because("@notify must write the decremented count back, not silently fail the permission check");
	}

	private async Task<string> MortalSemaphoreCountAsync(TestIsolationHelpers.TestPlayer who, string attr)
		=> (await Parser.FunctionParse(MarkupText.Plain($"get({who.DbRef}/{attr})")))?.Message?.ToPlainText() ?? string.Empty;
}
