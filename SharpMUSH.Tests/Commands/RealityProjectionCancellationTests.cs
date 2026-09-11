using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Commands;

public class RealityProjectionCancellationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task ScanForwardsTheCommandLifetimeToALegacyRealityPolicy()
	{
		var objects = new TestObjectFactory();
		var room = objects.CreateRoom(40, "room");
		var actor = objects.CreatePlayer(41, "actor", room);
		var target = objects.CreateThing(42, "target", room);
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(actor));
		mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(new[] { target.MinusRoom() }.ToAsyncEnumerable());
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		var policy = Substitute.For<IRealityPolicy>();
		async Task<bool> Block(CancellationToken token)
		{
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			return false;
		}
		policy.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(call => new ValueTask<bool>(Block(call.Arg<CancellationToken>())));
		using var services = new ServiceCollection().AddSingleton(policy).BuildServiceProvider();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(services);
		parser.CurrentState.Returns(ParserState.RootFor(actor.Object().DBRef) with
		{
			Switches = ["ROOM"],
			Arguments = new() { ["0"] = new CallState("probe") }
		});
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			Factory.Services, mediator, new CommandDiscoveryService(mediator));
		using var request = new CancellationTokenSource();
		using var budget = ExecutionBudget.FromMilliseconds(0, request.Token);
		using var scope = budget.Enter();
		var pending = commands.Scan(parser, new SharpCommandAttribute { Name = "@SCAN" }).AsTask();
		try
		{
			var forwarded = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			request.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(1)));
			await Assert.That(forwarded).IsEqualTo(budget.Token);
		}
		finally
		{
			cleanup.Cancel();
			try { await pending; } catch (OperationCanceledException) { }
		}
	}
}
