using Mediator;
using SharpMUSH.Tests.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Parser;

public class DebugCompletionBudgetTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("identity")]
	[Arguments("owner")]
	[Arguments("notification")]
	public async Task StandaloneSubstitutionDebugSharesTheParseDeadline(string stage)
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		async Task Block(string current, CancellationToken token)
		{
			if (current != stage) return;
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
		}
		var (parser, _) = Create(Block);
		var pending = parser.FunctionParse(MarkupText.Plain("%0"), true).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
			await Assert.That(result?.Message?.ToPlainText()).IsEqualTo(ExecutionBudget.Error);
			await Assert.That(ExecutionBudget.Current is null).IsTrue();
			await Assert.That((await parser.FunctionParse(MarkupText.Plain("ordinary")))?.Message?.ToPlainText()).IsEqualTo("ordinary");
		}
		finally
		{
			release.Cancel();
			try { await pending; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	public async Task ExpiredParseDoesNotBeginDebugCompletion()
	{
		var (parser, mediator) = Create((_, _) => Task.CompletedTask);
		using var budget = new ExecutionBudget(TimeSpan.Zero);
		var result = await parser.FromState(parser.CurrentState with { ExecutionBudget = budget }).FunctionParse(MarkupText.Plain("%0"), true);
		await Assert.That(result?.Message?.ToPlainText()).IsEqualTo(ExecutionBudget.Error);
		await mediator.DidNotReceive().Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task BorrowedCancellationReachesDebugCompletion(bool ambient)
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		var (parser, _) = Create(async (stage, token) =>
		{
			if (stage != "notification") return;
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
		});
		using var request = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, request.Token);
		using var scope = ambient ? budget.Enter() : null;
		if (!ambient) parser = parser.FromState(parser.CurrentState with { ExecutionBudget = budget });
		var pending = parser.FunctionParse(MarkupText.Plain("%0"), true).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
			request.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(1)));
		}
		finally
		{
			release.Cancel();
			try { await pending; } catch (OperationCanceledException) { }
		}
	}

	private (IMUSHCodeParser Parser, IMediator Mediator) Create(Func<string, CancellationToken, Task> read)
	{
		var executor = new TestObjectFactory().CreatePlayer(15, "debug executor");
		var executorPlayer = executor.Expect<SharpPlayer>();
		executorPlayer.Object.Owner = new(async token => { await read("owner", token); return executorPlayer; });
		var mediator = Substitute.For<IMediator>();
		async Task<AnyOptionalSharpObject> ReadObject(CancellationToken token)
		{
			await read("identity", token);
			return new AnyOptionalSharpObject(executor);
		}
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call => new ValueTask<AnyOptionalSharpObject>(ReadObject(call.Arg<CancellationToken>())));
		var notify = Substitute.For<INotifyService>();
		notify.Notify(Arg.Any<AnySharpObject>(), Arg.Any<SharpMessage>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>())
			.Returns(_ => new ValueTask(read("notification", ExecutionBudget.CurrentToken)));
		var services = Substitute.For<IServiceProvider>();
		services.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IMediator) ? mediator
			: call.Arg<Type>() == typeof(INotifyService) ? notify : Factory.Services.GetService(call.Arg<Type>()));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var baseline = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		options.CurrentValue.Returns(baseline with { Limit = baseline.Limit with { QueueEntryCpuTime = 250 } });
		var parser = new MUSHCodeParser(Factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MUSHCodeParser>>(),
			Factory.Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			Factory.Services.GetRequiredService<LibraryService<string, CommandDefinition>>(), options, services,
			ParserState.RootFor(executor.Object().DBRef) with
			{
				Flags = ParserStateFlags.Debug,
				EnvironmentRegisters = new() { ["0"] = new("private-substitution") }
			});
		return (parser, mediator);
	}
}
