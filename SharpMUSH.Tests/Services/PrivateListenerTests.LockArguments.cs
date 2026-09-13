using MarkupString;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Handlers.ListenPattern;
using SharpMUSH.Library.Commands.ListenPattern;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	[Test]
	public async Task ActualSchedulerDoesNotCaptureSubmittingFilterScopeForHearActions()
	{
		var speaker = await Player();
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "QueuedScope");
		await SetRaw(target, "CHECK", "%0");
		var node = await Node(target);
		var actor = await Node(speaker.DbRef);
		var locks = Factory.Services.GetRequiredService<ILockService>();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		async ValueTask<CallState?> Execute()
		{
			entered.TrySetResult();
			await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
			try { result.TrySetResult(await locks.Evaluate("CHECK/outer", node, actor)); }
			catch (Exception error) { result.TrySetException(error); }
			return CallState.Empty;
		}
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => Execute());
		await using var scheduler = ActivatorUtilities.CreateInstance<SharpMUSH.Library.Services.TaskScheduler>(Factory.Services, parser);
		var handler = new ExecuteListenPatternCommandHandler(new Provider(Factory.Services, scheduler),
			Factory.Services.GetRequiredService<IAttributeService>(), NullLogger<ExecuteListenPatternCommandHandler>.Instance);
		try
		{
			using (LockEvaluationArguments.Enter(new Dictionary<string, MarkupText> { ["0"] = MarkupText.Plain("outer") }))
			{
				await handler.Handle(new ExecuteListenPatternCommand(node, actor, "AHEAR", [])
				{
					Action = MarkupText.Plain("think deferred")
				}, CancellationToken.None);
				await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
				await Assert.That(await locks.Evaluate("CHECK/outer", node, actor)).IsTrue();
				release.TrySetResult();
				await Assert.That(await result.Task.WaitAsync(TimeSpan.FromSeconds(10))).IsFalse();
			}
			await Assert.That(LockEvaluationArguments.CreateArguments()).IsEmpty();
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	public async Task CachedIndirectLocksKeepConcurrentMessageArgumentsIsolatedAndRestoreOuterScope()
	{
		var speaker = await Player();
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ScopedFilter");
		await SetRaw(target, "CHECK", "%0");
		await Admin($"@lock/infilter {target}=CHECK/allowed");
		var node = await Node(target);
		var actor = await Node(speaker.DbRef);
		var locks = Factory.Services.GetRequiredService<ILockService>();
		var expression = $"@{target}/InFilter";
		var original = new Dictionary<string, MarkupText> { ["0"] = MarkupText.Plain("outer") };
		using (LockEvaluationArguments.Enter(original))
		{
			original["0"] = MarkupText.Plain("changed");
			await Assert.That(await locks.Evaluate("CHECK/outer", node, actor)).IsTrue();
			var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			async Task<bool> Evaluate(string message)
			{
				using var inner = LockEvaluationArguments.Enter(new Dictionary<string, MarkupText> { ["0"] = MarkupText.Plain(message) });
				await gate.Task;
				return await locks.Evaluate(expression, node, actor);
			}
			var accepted = Evaluate("allowed");
			var rejected = Evaluate("blocked");
			gate.SetResult();
			await Assert.That(await accepted).IsTrue();
			await Assert.That(await rejected).IsFalse();
			await Assert.That(await locks.Evaluate("CHECK/outer", node, actor)).IsTrue();
		}
		await Assert.That(LockEvaluationArguments.CreateArguments()).IsEmpty();
		await Assert.That(await locks.Evaluate("CHECK/outer", node, actor)).IsFalse();
	}

	[Test]
	public async Task CancellationAndRestrictionDoNotLeakFilterArguments()
	{
		var speaker = await Player();
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "CancelledFilter");
		await SetRaw(target, "CHECK", "%0");
		var node = await Node(target);
		var actor = await Node(speaker.DbRef);
		var locks = Factory.Services.GetRequiredService<ILockService>();
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();
		async Task Cancelled()
		{
			using var arguments = LockEvaluationArguments.Enter(new Dictionary<string, MarkupText> { ["0"] = MarkupText.Plain("allowed") });
			using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
			using var scope = budget.Enter();
			await locks.Evaluate("CHECK/allowed", node, actor);
		}
		await Assert.That(Cancelled).Throws<OperationCanceledException>();
		await Assert.That(LockEvaluationArguments.CreateArguments()).IsEmpty();
		using (LockEvaluationArguments.Enter(new Dictionary<string, MarkupText> { ["0"] = MarkupText.Plain("allowed") }))
		using (new EvaluationRestrictions([]).Enter())
			await Assert.That(await locks.Evaluate("CHECK/allowed", node, actor)).IsFalse();
		await Assert.That(LockEvaluationArguments.CreateArguments()).IsEmpty();
	}
}
