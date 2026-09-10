using System.Runtime.CompilerServices;
using Mediator;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

public class QueueControlPermissionCancellationTests
{
	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task SourcePermissionReadsHonorRequestAndParentCancellation(bool ambient, bool cancelParent)
	{
		var factory = new TestObjectFactory();
		var player = factory.CreatePlayer(40, "actor");
		var target = factory.CreateThing(41, "target", owner: player.AsPlayer);
		var actor = new CapabilityActor("account", player.Object().DBRef, player.Object().DBRef);
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			ValueTask.FromResult<AnyOptionalSharpObject>(call.Arg<GetObjectNodeQuery>().DBRef.Number == 40 ? (AnyOptionalSharpObject)player.AsPlayer : target.AsThing));
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		options.CurrentValue.Returns(TestSharpMushOptions.Create());
		var permissions = new PermissionService(Substitute.For<ILockService>(), options);
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		async IAsyncEnumerable<SharpPower> Powers([EnumeratorCancellation] CancellationToken token = default)
		{
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			yield break;
		}
		player.Object().Powers = new(() => Powers());
		var service = new QueueControlService(Substitute.For<ITaskScheduler>(), Substitute.For<IAdministrativeCapabilityService>(), mediator, permissions);
		using var request = new CancellationTokenSource();
		using var parent = new CancellationTokenSource();
		using var budget = ambient ? new ExecutionBudget(TimeSpan.FromSeconds(30), parent.Token) : null;
		using var scope = budget?.Enter();
		var invocation = service.CanInspectAsync(new(actor, new HashSet<string> { PortalPermission.QueueInspectOwn }), actor.ActiveCharacter, target.Object().DBRef, request.Token);
		try
		{
			await Assert.That((await entered.Task.WaitAsync(TimeSpan.FromSeconds(2))).CanBeCanceled).IsTrue();
			(cancelParent ? parent : request).Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
		}
		finally
		{
			cleanup.Cancel();
			try { await invocation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SchedulerTransitionReceivesRequestCancellation(bool resume)
	{
		var actor = new CapabilityActor("account", new DBRef(40, 1), new DBRef(40, 1));
		var scheduler = Substitute.For<ITaskScheduler>();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGameActorAsync(actor.Executor!.Value, Arg.Any<CancellationToken>()).Returns(actor);
		capabilities.GetGrantedScopesAsync(actor, Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.QueueControl });
		scheduler.GetQueueEntry(12).Returns(new SharpMUSH.Library.Models.SchedulerModels.QueueEntrySnapshot(12, new DBRef(50, 1), new DBRef(50, 1), "delay", SharpMUSH.Library.Models.SchedulerModels.QueueEntryState.Pending, TimeSpan.FromSeconds(30), ""));
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		async ValueTask<SharpMUSH.Library.Models.SchedulerModels.QueueControlResult> Transition()
		{
			var token = ExecutionBudget.CurrentToken;
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			return SharpMUSH.Library.Models.SchedulerModels.QueueControlResult.Applied;
		}
		scheduler.PausePending(12, "").Returns(_ => Transition());
		scheduler.ResumePending(12).Returns(_ => Transition());
		var service = new QueueControlService(scheduler, capabilities, Substitute.For<IMediator>(), Substitute.For<IPermissionService>());
		using var request = new CancellationTokenSource();
		var invocation = service.ChangeAsync(actor, 12, resume, ct: request.Token);
		try
		{
			await Assert.That((await entered.Task.WaitAsync(TimeSpan.FromSeconds(2))).CanBeCanceled).IsTrue();
			request.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
		}
		finally
		{
			cleanup.Cancel();
			try { await invocation; } catch (OperationCanceledException) { }
		}
	}
}
