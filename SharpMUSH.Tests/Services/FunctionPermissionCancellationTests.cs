using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class FunctionPermissionCancellationTests
{
	[Test]
	[Arguments(FunctionFlags.NoGagged, false)]
	[Arguments(FunctionFlags.NoFixed, false)]
	[Arguments(FunctionFlags.Deprecated, false)]
	[Arguments(FunctionFlags.NoGagged, true)]
	[Arguments(FunctionFlags.NoFixed, true)]
	[Arguments(FunctionFlags.Deprecated, true)]
	public async Task BlockedFunctionOwnerCannotStrandQueue(FunctionFlags flags, bool halt)
	{
		var executor = new TestObjectFactory().CreatePlayer(15, "function executor");
		var executorPlayer = executor.Expect<SharpPlayer>();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var following = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		executorPlayer.Object.Owner = new(async token =>
		{
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
			await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
			return executorPlayer;
		});
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(ParserState.RootFor(executor.Object().DBRef));
		var notify = Substitute.For<INotifyService>();
		var invoked = false;
		var definition = new FunctionDefinition(new SharpFunctionAttribute
		{
			Name = "ownerprobe", Flags = flags, MinArgs = 0, MaxArgs = 0
		}, _ =>
		{
			invoked = true;
			return ValueTask.FromResult(CallState.Empty);
		});
		var baseline = TestSharpMushOptions.Create();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with { Limit = baseline.Limit with { QueueEntryCpuTime = halt ? 30000u : 1000u } });
		var queue = new Scheduler(parser, Substitute.For<IConnectionService>(), Substitute.For<Quartz.ISchedulerFactory>(),
			Substitute.For<IAttributeService>(), QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance, options, notify);
		try
		{
			var blocked = await queue.AdmitWork(async () => await FunctionDispatcher.InvokeAsync(parser, definition,
				executor, true, notify, NullLogger.Instance), "owner-read", "test");
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await queue.AdmitWork(() => { following.TrySetResult(); return ValueTask.FromResult<CallState?>(CallState.Empty); }, "following", "test");
			if (halt) await queue.HaltByPid(blocked.Pid!.Value);
			await following.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await Assert.That(invoked).IsFalse();
		}
		finally
		{
			cleanup.Cancel();
			await queue.DisposeAsync();
		}
	}
}
