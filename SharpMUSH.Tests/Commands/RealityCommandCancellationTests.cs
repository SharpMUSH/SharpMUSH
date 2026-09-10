using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Commands;

public class RealityCommandCancellationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("actor")]
	[Arguments("authorization")]
	[Arguments("identity")]
	[Arguments("configuration-read")]
	[Arguments("configuration-write")]
	[Arguments("profile-read")]
	[Arguments("profile-write")]
	[Arguments("gate")]
	public async Task RealityCommandBoundsAdministrationReadsWritesAndGate(string stage)
	{
		var player = new TestObjectFactory().CreatePlayer(40, "admin");
		player.Object().Id = "admin";
		var actor = new CapabilityActor("admin", player.Object().DBRef, player.Object().DBRef);
		var objects = Substitute.For<IObjectStore>();
		objects.GetObjectNodeAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(player.AsPlayer));
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGameActorAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(actor);
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.RealityAdmin, Arg.Any<CancellationToken>()).Returns(true);
		var store = Substitute.For<IExpandedDataStore>();
		store.GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, Arg.Any<CancellationToken>())
			.Returns(new RealityConfiguration(1, true, ["normal"]));
		var permissions = Substitute.For<IPermissionService>();
		permissions.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		var gateHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		async Task<T> Block<T>(CancellationToken token)
		{
			if (stage == "gate") gateHeld.TrySetResult();
			else entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			throw new InvalidOperationException("Blocked administration must cancel.");
		}
		switch (stage)
		{
			case "actor": capabilities.GetGameActorAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(call => Block<CapabilityActor?>(call.Arg<CancellationToken>())); break;
			case "authorization":
			case "gate": capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.RealityAdmin, Arg.Any<CancellationToken>()).Returns(call => Block<bool>(call.Arg<CancellationToken>())); break;
			case "identity": objects.GetObjectNodeAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(call => new ValueTask<AnyOptionalSharpObject>(Block<AnyOptionalSharpObject>(call.Arg<CancellationToken>()))); break;
			case "configuration-read": store.GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, Arg.Any<CancellationToken>()).Returns(call => new ValueTask<RealityConfiguration?>(Block<RealityConfiguration?>(call.Arg<CancellationToken>()))); break;
			case "configuration-write": store.SetExpandedServerData(RealityPolicy.ConfigurationKey, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(call => new ValueTask(Block<bool>(call.Arg<CancellationToken>()))); break;
			case "profile-read": store.GetExpandedObjectData<ObjectReality>("admin", RealityPolicy.ObjectKey, Arg.Any<CancellationToken>()).Returns(call => new ValueTask<ObjectReality?>(Block<ObjectReality?>(call.Arg<CancellationToken>()))); break;
			case "profile-write": store.SetExpandedObjectData("admin", RealityPolicy.ObjectKey, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(call => new ValueTask(Block<bool>(call.Arg<CancellationToken>()))); break;
		}
		var administration = new RealityAdministration(new(store, objects), capabilities, objects, permissions, Substitute.For<IValidateService>());
		using var services = new ServiceCollection().AddSingleton(capabilities).AddSingleton(administration).BuildServiceProvider();
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(player.AsPlayer));
		var state = ParserState.RootFor(player.Object().DBRef) with
		{
			Switches = [stage.StartsWith("profile", StringComparison.Ordinal) ? "RX" : "ENABLE"],
			Arguments = new() { ["0"] = new CallState(player.Object().DBRef.ToString()), ["1"] = new CallState("normal") }
		};
		await state.KnownExecutorObject(mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);
		parser.ServiceProvider.Returns(services);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(Factory.Services, mediator);
		Task<string>? holder = null;
		if (stage == "gate")
		{
			holder = administration.ExecuteAsync(actor, "list", "", "");
			await gateHeld.Task.WaitAsync(TimeSpan.FromSeconds(2));
			capabilities.GetGameActorAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(call =>
			{
				entered.TrySetResult(call.Arg<CancellationToken>());
				return actor;
			});
		}
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
		using var scope = budget.Enter();
		var invocation = commands.Reality(parser, new SharpCommandAttribute { Name = "@REALITY" }).AsTask();
		try
		{
			await Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsEqualTo(budget.Token);
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
		}
		finally
		{
			release.Cancel();
			if (holder is not null) try { await holder; } catch (OperationCanceledException) { }
			try { await invocation; } catch (OperationCanceledException) { }
		}
	}
}
