using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Reflection;

namespace SharpMUSH.Tests.Services;

public class DebugOwnerCancellationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DebugOwnerReadUsesCurrentLifetime(bool substitution)
	{
		var executor = new TestObjectFactory().CreatePlayer(15, "debug executor");
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		executor.AsPlayer.Object.Owner = new(async token =>
		{
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			return executor.AsPlayer;
		});
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(executor.AsPlayer));
		var notify = Substitute.For<INotifyService>();
		var parser = Substitute.For<IMUSHCodeParser>();
		var state = ParserState.RootFor(executor.AsPlayer.Object.DBRef) with { Flags = ParserStateFlags.Debug };
		using var request = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, request.Token);
		using var scope = budget.Enter();
		ValueTask pending;
		if (substitution)
		{
			var method = typeof(MUSHCodeParser).GetMethod("EmitSubstitutionOnlyDebugTraceAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
			pending = (ValueTask)method.Invoke(null, [mediator, notify, state, "%#", MarkupText.Plain("#15"), false])!;
		}
		else
		{
			var visitor = new SharpMUSHParserVisitor(NullLogger.Instance, parser, Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
				mediator, notify, Substitute.For<IConnectionService>(), Substitute.For<ILocateService>(),
				Substitute.For<ICommandDiscoveryService>(), Substitute.For<IAttributeService>(), Substitute.For<IHookService>(),
				Substitute.For<ILockService>(), MarkupText.Empty);
			var method = typeof(SharpMUSHParserVisitor).GetMethod("SendDebugOrVerboseOutput", BindingFlags.Instance | BindingFlags.NonPublic)!;
			pending = (ValueTask)method.Invoke(visitor, [executor, "private trace"])!;
		}
		var task = pending.AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
			request.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(1)));
		}
		finally
		{
			release.Cancel();
			try { await task; } catch (OperationCanceledException) { }
		}
	}
}
