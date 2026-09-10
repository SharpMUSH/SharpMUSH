using Mediator;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class QueueControlAuthorizationTests
{
	private static readonly CapabilityActor Actor = new("operator", new DBRef(1, 100), new DBRef(1, 100));
	private static readonly QueueEntrySnapshot Other = new(10, new DBRef(8, 100), new DBRef(9, 100), "delay", QueueEntryState.Pending, TimeSpan.FromSeconds(30), "");

	[Test]
	public async Task GlobalControlIsFreshAndRevocationStopsTheNextMutation()
	{
		var queue = Substitute.For<ITaskScheduler>();
		var caps = Substitute.For<IAdministrativeCapabilityService>();
		queue.GetQueueEntry(10).Returns(Other);
		queue.PausePending(10, "inspection").Returns(QueueControlResult.Applied);
		caps.GetGameActorAsync(Actor.Executor!.Value, Arg.Any<CancellationToken>()).Returns(Actor);
		caps.GetGrantedScopesAsync(Actor, Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.QueueControl }, new HashSet<string>());
		var service = new QueueControlService(queue, caps, Substitute.For<IMediator>(), Substitute.For<IPermissionService>());
		await Assert.That(await service.ChangeAsync(Actor, 10, false, "inspection")).IsEqualTo(QueueControlResult.Applied);
		await Assert.That(await service.ChangeAsync(Actor, 10, false, "inspection")).IsEqualTo(QueueControlResult.NotFound);
		await queue.Received(1).PausePending(10, "inspection");
	}

	[Test]
	public async Task ExplicitOwnDenyCannotFallBackToGlobalGrant()
	{
		var queue = Substitute.For<ITaskScheduler>();
		var caps = Substitute.For<IAdministrativeCapabilityService>();
		queue.GetQueueEntry(10).Returns(Other with { Owner = Actor.ActiveCharacter });
		queue.GetQueueEntries().Returns([Other with { Owner = Actor.ActiveCharacter }, Other]);
		caps.GetGameActorAsync(Actor.Executor!.Value, Arg.Any<CancellationToken>()).Returns(Actor);
		caps.GetGrantedScopesAsync(Actor, Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.QueueControl, PortalPermission.QueueInspect });
		var service = new QueueControlService(queue, caps, Substitute.For<IMediator>(), Substitute.For<IPermissionService>());
		await Assert.That(await service.ChangeAsync(Actor, 10, false)).IsEqualTo(QueueControlResult.NotFound);
		await Assert.That((await service.ListAsync(Actor)).Single()).IsEqualTo(Other);
		await queue.DidNotReceiveWithAnyArgs().PausePending(default, default!);
	}

	[Test]
	public async Task AccountCannotSubstituteAnOwnedObjectOrUnlinkedPlayer()
	{
		var queue = Substitute.For<ITaskScheduler>();
		var caps = Substitute.For<IAdministrativeCapabilityService>();
		queue.GetQueueEntry(10).Returns(Other);
		caps.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.QueueControl });
		var service = new QueueControlService(queue, caps, Substitute.For<IMediator>(), Substitute.For<IPermissionService>());
		await Assert.That(await service.ChangeAsync(Actor with { Executor = new DBRef(8, 100) }, 10, false)).IsEqualTo(QueueControlResult.NotFound);
		await Assert.That(await service.ChangeAsync(Actor, 10, false)).IsEqualTo(QueueControlResult.NotFound);
		await queue.DidNotReceiveWithAnyArgs().PausePending(default, default!);
	}
	[Test]
	public async Task BoundedListingSkipsDeniedRowsAndStopsEnumeratingAfterVisibleLimit()
	{
		var queue = Substitute.For<ITaskScheduler>();
		var caps = Substitute.For<IAdministrativeCapabilityService>();
		var visits = 0;
		IEnumerable<QueueEntrySnapshot> Entries()
		{
			for (var i = 0; i < 300; i++)
			{
				visits++;
				if (visits > 201) throw new InvalidOperationException("Read past authorized row limit");
				yield return Other with { Pid = i, Owner = i < 100 ? Actor.ActiveCharacter : Other.Owner };
			}
		}
		queue.EnumerateQueueEntries().Returns(_ => Entries());
		// The complete snapshot must never be consulted by bounded inspection.
		queue.GetQueueEntries().Returns(_ => throw new InvalidOperationException("Materialized full ledger"));
		caps.GetGameActorAsync(Actor.Executor!.Value, Arg.Any<CancellationToken>()).Returns(Actor);
		caps.GetGrantedScopesAsync(Actor, Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.QueueInspect });
		var service = new QueueControlService(queue, caps, Substitute.For<IMediator>(), Substitute.For<IPermissionService>());
		var rows = await service.ListAsync(Actor, 101);
		await Assert.That(rows.Count).IsEqualTo(101);
		await Assert.That(rows[0].Pid).IsEqualTo(100L);
		await Assert.That(visits).IsEqualTo(201);
	}

}
