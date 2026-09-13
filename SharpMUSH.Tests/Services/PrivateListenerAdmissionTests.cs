using MarkupString;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Handlers.ListenPattern;
using SharpMUSH.Library.Commands.ListenPattern;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	[Test]
	public async Task ActualUnframedDependencyInjectionParserAdmitsAndExecutesAction()
	{
		var actor = await Player();
		var listener = await Node(actor.DbRef);
		await Admin($"@ahear {actor.DbRef}=&HEARD me=actual DI");
		var pipeline = await Build(actor, actor.DbRef);
		var parser = Factory.Services.GetRequiredService<IMUSHCodeParser>();
		await Assert.That(parser.State.IsEmpty).IsTrue();
		var handler = new ExecuteListenPatternCommandHandler(new Provider(Factory.Services, pipeline.Scheduler),
			Factory.Services.GetRequiredService<IAttributeService>(), NullLogger<ExecuteListenPatternCommandHandler>.Instance);
		await handler.Handle(new ExecuteListenPatternCommand(listener, listener, "AHEAR", []), CancellationToken.None);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(1);
		await parser.FromState(pipeline.Queue[0].State).CommandListParse(pipeline.Queue[0].Command);
		var attribute = await Factory.Services.GetRequiredService<IAttributeService>().GetAttributeAsync(listener, listener, "HEARD", IAttributeService.AttributeMode.Read, false);
		await Assert.That(attribute.Expect<SharpMUSH.Library.Models.SharpAttribute[]>().Last().Value.ToPlainText()).IsEqualTo("actual DI");
	}

	[Test]
	public async Task NestedMonitorActionRetainsFullAttributeIdentity()
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "NestedMonitor");
		await Admin($"@set {listener}=MONITOR");
		await Admin($"&TREE`PATTERN {listener}=^*:&HEARD me=%0");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(listener, "hello", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(1);
		await Assert.That(pipeline.Queue[0].State.CurrentEvaluation!.Name).IsEqualTo("TREE`PATTERN");
	}

	[Test]
	[Arguments(QueueRejectionReason.None)]
	[Arguments(QueueRejectionReason.OwnerLimit)]
	[Arguments(QueueRejectionReason.GlobalLimit)]
	public async Task SelfReactionWaitsOnlyForAdmissionAndHonorsRejection(QueueRejectionReason reason)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "DeferredListen");
		await Admin($"@chown {listener}={actor.DbRef}");
		await Admin($"@listen {listener}=*");
		await Admin($"@amhear {listener}=@pemit/silent me=again");
		var pipeline = await Build(actor, actor.DbRef);
		if (reason != QueueRejectionReason.None)
			pipeline.Scheduler.AdmitCommandList(Arg.Any<MarkupText>(), Arg.Any<ParserState>()).Returns(new QueueAdmissionResult(null, reason));
		await pipeline.Notify.Notify(listener, "first", await Node(listener), INotifyService.NotificationType.PrivateEmit);
		await pipeline.Scheduler.Received(1).AdmitCommandList(Arg.Any<MarkupText>(), Arg.Is<ParserState>(state => state.Executor == listener));
		await Assert.That(pipeline.Queue.Count).IsEqualTo(reason == QueueRejectionReason.None ? 1 : 0);
	}

	[Test]
	public async Task ActionAdmissionCopiesArgumentsAndStartsFreshExecutionState()
	{
		var actor = await Player();
		var listener = await Node(actor.DbRef);
		var pipeline = await Build(actor, actor.DbRef);
		var handler = new ExecuteListenPatternCommandHandler(new Provider(Factory.Services, pipeline.Scheduler, pipeline.Parser),
			Factory.Services.GetRequiredService<IAttributeService>(), NullLogger<ExecuteListenPatternCommandHandler>.Instance);
		var arguments = new Dictionary<string, CallState> { ["0"] = new CallState("original") };
		await handler.Handle(new ExecuteListenPatternCommand(listener, listener, "AHEAR", arguments)
		{
			Action = MarkupText.Plain("&RESULT me=%0")
		}, CancellationToken.None);
		arguments["0"] = new CallState("changed");
		var state = pipeline.Queue.Single().State;
		await Assert.That(state.EnvironmentRegisters["0"].Message!.ToPlainText()).IsEqualTo("original");
		state.Arguments["0"] = new CallState("changed queued argument");
		await Assert.That(state.EnvironmentRegisters["0"].Message!.ToPlainText()).IsEqualTo("original");
		await Assert.That(state.ExecutionBudget).IsNull();
		await Assert.That(state.CommandHistory).IsNull();
		await Assert.That(state.CommandModifierDepth).IsEqualTo(0u);
		await Assert.That(state.Registers.Single()).IsEmpty();
	}

	[Test]
	public async Task RestrictedActionCannotQueueAnUnrestrictedCallback()
	{
		var actor = await Player();
		var pipeline = await Build(actor, actor.DbRef);
		var listener = await Node(actor.DbRef);
		var handler = new ExecuteListenPatternCommandHandler(new Provider(Factory.Services, pipeline.Scheduler, pipeline.Parser),
			Factory.Services.GetRequiredService<IAttributeService>(), NullLogger<ExecuteListenPatternCommandHandler>.Instance);
		using var scope = new EvaluationRestrictions([]).Enter();
		await Assert.ThrowsAsync<RestrictedExpressionException>(async () => await handler.Handle(
			new ExecuteListenPatternCommand(listener, listener, "AHEAR", []) { Action = MarkupText.Plain("@pemit me=forbidden") }, CancellationToken.None));
		await pipeline.Scheduler.DidNotReceive().AdmitCommandList(Arg.Any<MarkupText>(), Arg.Any<ParserState>());
	}

	[Test]
	public async Task ActionAdmissionPropagatesRequestCancellation()
	{
		var actor = await Player();
		var pipeline = await Build(actor, actor.DbRef);
		var listener = await Node(actor.DbRef);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		async ValueTask<QueueAdmissionResult> WaitForCancellation()
		{
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cleanup.Token, ExecutionBudget.CurrentToken);
			await Task.Delay(Timeout.Infinite, linked.Token);
			return new QueueAdmissionResult(null, QueueRejectionReason.ShuttingDown);
		}
		pipeline.Scheduler.AdmitCommandList(Arg.Any<MarkupText>(), Arg.Any<ParserState>()).Returns(_ => WaitForCancellation());
		var handler = new ExecuteListenPatternCommandHandler(new Provider(Factory.Services, pipeline.Scheduler, pipeline.Parser),
			Factory.Services.GetRequiredService<IAttributeService>(), NullLogger<ExecuteListenPatternCommandHandler>.Instance);
		using var request = new CancellationTokenSource();
		var pending = handler.Handle(new ExecuteListenPatternCommand(listener, listener, "AHEAR", [])
		{
			Action = MarkupText.Plain("think queued")
		}, request.Token).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			request.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
		}
		finally
		{
			cleanup.Cancel();
			try { await pending; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments("")]
	[Arguments(" ")]
	public async Task PresentBlankListenPatternCanMatchPrompt(string pattern)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "BlankListen");
		await Admin($"@listen {listener}={pattern}");
		await Admin($"@ahear {listener}=&HEARD me=1");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Prompt(listener, pattern, await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(1);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LocalListenUsesInheritedActionUnlessLocallyEmpty(bool emptyLocal)
	{
		var actor = await Player();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ParentListen");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ChildListen");
		await Admin($"@parent {child}={parent}");
		await Admin($"@listen {child}=*");
		await Admin($"@ahear {parent}=&HEARD me=parent action");
		if (emptyLocal) await Admin($"@ahear {child}=");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(child, "hello", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(emptyLocal ? 0 : 1);
		if (!emptyLocal) await Assert.That(pipeline.Queue.Single().State.Executor).IsEqualTo(child);
	}
}
