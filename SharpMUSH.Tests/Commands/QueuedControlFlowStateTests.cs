using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using CommandLibrary = SharpMUSH.Implementation.Commands.Commands;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class QueuedControlFlowStateTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task QueuedActionStartsIndependentExecutionButRetainsRestrictions()
	{
		using var budget = ExecutionBudget.FromMilliseconds(30000);
		var parent = ParserState.RootFor(Factory.ExecutorDBRef) with
		{
			ExecutionBudget = budget,
			CommandHistory = new(),
			BreakPropagation = new() { Broke = true },
			Restrictions = new EvaluationRestrictions(["add"])
		};
		parent.CallDepth!.Increment();
		parent.TotalInvocations!.Increment();
		parent.MoveDepth!.Increment();
		parent.FunctionRecursionDepths!["add"] = 1;
		parent.LimitExceeded!.IsExceeded = true;
		parent.ExecutionStack.Push(new(CommandListBreak: true));
		var child = parent.SnapshotForQueuedAction();
		await Assert.That(child.CallDepth!.Count).IsEqualTo(0);
		await Assert.That(child.TotalInvocations!.Count).IsEqualTo(0);
		await Assert.That(child.MoveDepth!.Count).IsEqualTo(0);
		await Assert.That(child.FunctionRecursionDepths!.Count).IsEqualTo(0);
		await Assert.That(child.LimitExceeded!.IsExceeded).IsFalse();
		await Assert.That(child.ExecutionStack.IsEmpty).IsTrue();
		await Assert.That(child.CommandHistory).IsNull();
		await Assert.That(child.BreakPropagation).IsNull();
		await Assert.That(child.ExecutionBudget).IsNull();
		child.CallDepth.Increment();
		child.CallDepth.Increment();
		child.TotalInvocations.Increment();
		child.TotalInvocations.Increment();
		child.MoveDepth.Increment();
		child.MoveDepth.Increment();
		child.FunctionRecursionDepths["add"] = 2;
		await Assert.That(parent.CallDepth.Count).IsEqualTo(1);
		await Assert.That(parent.TotalInvocations.Count).IsEqualTo(1);
		await Assert.That(parent.MoveDepth.Count).IsEqualTo(1);
		await Assert.That(parent.FunctionRecursionDepths["add"]).IsEqualTo(1);
		var result = await Factory.FunctionParser.FromState(child).FunctionParse(MarkupText.Plain("[mul(2,3)]"));
		await Assert.That(result?.Message?.ToPlainText()).IsEqualTo(EvaluationRestrictions.Error);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task QueuedActionRetainsNestedRegistersAfterCallerUnwinds(bool select)
	{
		var state = ParserState.RootFor(Factory.ExecutorDBRef) with
		{
			Arguments = new() { ["0"] = new("match"), ["1"] = new("match"), ["2"] = new("think %i0|%i1|[r(NAME,regexp)]") }
		};
		await state.KnownExecutorObject(Factory.Services.GetRequiredService<IMediator>());
		var outer = new IterationWrapper<MString> { Value = MarkupText.Plain("outer"), Iteration = 2, Break = false, NoBreak = true };
		var inner = new IterationWrapper<MString> { Value = MarkupText.Plain("inner"), Iteration = 3, Break = false, NoBreak = false };
		state.IterationRegisters.Push(outer);
		state.IterationRegisters.Push(inner);
		var outerRegex = new Dictionary<string, MString>(StringComparer.OrdinalIgnoreCase) { ["name"] = MarkupText.Plain("outer capture") };
		var innerRegex = new Dictionary<string, MString>(StringComparer.OrdinalIgnoreCase) { ["name"] = MarkupText.Plain("inner capture") };
		state.RegexRegisters.Push(outerRegex);
		state.RegexRegisters.Push(innerRegex);
		var admission = Substitute.For<IMediator>();
		AdmitCommandListRequest? queued = null;
		admission.Send(Arg.Any<AdmitCommandListRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			queued = call.Arg<AdmitCommandListRequest>();
			return new QueueAdmissionResult(1, QueueRejectionReason.None);
		});
		var commands = ActivatorUtilities.CreateInstance<CommandLibrary>(Factory.Services, admission);
		var parser = Factory.CommandParser.FromState(state);
		var definition = new SharpCommandAttribute { Name = select ? "@SELECT" : "@SWITCH", MinArgs = 2 };
		if (select) await commands.Select(parser, definition);
		else await commands.Switch(parser, definition);
		await Assert.That(queued).IsNotNull();
		var saved = queued!.State;
		inner.Value = MarkupText.Plain("changed");
		inner.Iteration = 99;
		inner.Break = true;
		innerRegex["name"] = MarkupText.Plain("changed");
		state.IterationRegisters.Clear();
		state.RegexRegisters.Clear();

		var output = await Factory.FunctionParser.FromState(saved).FunctionParse(MarkupText.Plain("%i0|%i1|[r(NAME,regexp)]"));
		await Assert.That(output?.Message?.ToPlainText()).IsEqualTo("inner|outer|inner capture");
		await Factory.CommandParser.FromState(saved).CommandListParse(queued.Command);
		await Factory.Services.GetRequiredService<INotifyService>().Received().Notify(
			TestHelpers.MatchingObject(Factory.ExecutorDBRef), TestHelpers.MatchingMessage("inner|outer|inner capture"),
			TestHelpers.MatchingObject(Factory.ExecutorDBRef), INotifyService.NotificationType.Announce);
		await Assert.That(saved.IterationRegisters.First().Iteration).IsEqualTo(3u);
		await Assert.That(saved.IterationRegisters.First().Break).IsFalse();
		await Assert.That(saved.IterationRegisters.Last().NoBreak).IsTrue();
		await Assert.That(saved.RegexRegisters.First()["NAME"].ToPlainText()).IsEqualTo("inner capture");
		await Assert.That(saved.RegexRegisters.Last()["NAME"].ToPlainText()).IsEqualTo("outer capture");
		saved.IterationRegisters.Last().Value = MarkupText.Plain("queued mutation");
		saved.RegexRegisters.Last()["name"] = MarkupText.Plain("queued mutation");
		await Assert.That(outer.Value.ToPlainText()).IsEqualTo("outer");
		await Assert.That(outerRegex["name"].ToPlainText()).IsEqualTo("outer capture");
	}
}
