using Mediator;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

public class VisibleWorldProjectionTests
{
	private readonly TestObjectFactory _objects = new();
	private readonly IAdministrativeCapabilityService _capabilities = Substitute.For<IAdministrativeCapabilityService>();
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
	private readonly IRealityPolicy _reality = Substitute.For<IRealityPolicy>();
	private readonly SharpRoom _room;
	private readonly SharpPlayer _player;
	private readonly CapabilityActor _actor;
	private readonly VisibleWorldProjection _projection;

	public VisibleWorldProjectionTests()
	{
		_room = _objects.CreateRoom(10, "Visible room");
		_player = _objects.CreatePlayer(11, "Viewer", _room).AsPlayer;
		_actor = new("account", _player.Object.DBRef, _player.Object.DBRef);
		_capabilities.GetGameActorAsync(_player.Object.DBRef, Arg.Any<CancellationToken>()).Returns(_actor);
		_mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			ValueTask.FromResult<AnyOptionalSharpObject>(call.Arg<GetObjectNodeQuery>().DBRef == _player.Object.DBRef
				? _player : new None()));
		_mediator.Send(Arg.Any<GetLocationQuery>(), Arg.Any<CancellationToken>()).Returns(_room);
		_mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<AnySharpContent>());
		_reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		_permissions.CanSee(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		_projection = new(_capabilities, _mediator, _permissions, _reality, Substitute.For<IConnectionService>());
	}

	[Test]
	public async Task LinkedCurrentCharacterCanObserveOnlyItsCurrentRoom()
	{
		await Assert.That(await _projection.CanObserveRoomAsync(_actor, _room.Object.DBRef)).IsTrue();
		await Assert.That(await _projection.CanObserveRoomAsync(_actor, new DBRef(20, 0))).IsFalse();
		await Assert.That(await _projection.CanObserveRoomAsync(_actor, new DBRef(10))).IsFalse();
	}

	[Test]
	public async Task StateExcludesRealityHiddenAndNormallyInvisibleContents()
	{
		var visible = _objects.CreateThing(12, "Visible", _room).AsThing;
		var hidden = _objects.CreateThing(13, "Other layer", _room).AsThing;
		var dark = _objects.CreateThing(14, "Dark", _room).AsThing;
		_mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(new AnySharpContent[] { visible, hidden, dark }.ToAsyncEnumerable());
		_reality.CanPerceiveAsync(_player.Object.DBRef, hidden.Object.DBRef, Arg.Any<CancellationToken>()).Returns(false);
		WorldVisibilityTests.Flags(dark.Object, "DARK");
		var state = await _projection.GetStateAsync(_actor);
		await Assert.That(state).IsNotNull();
		await Assert.That(state!.RoomName).IsEqualTo("Visible room");
		await Assert.That(state.VisibleObjectDbrefs).IsEquivalentTo(new[] { visible.Object.DBRef.ToString() });
	}

	[Test]
	public async Task RoomEventUsesCurrentReceiverLocationAndSourceIdentity()
	{
		var source = _objects.CreatePlayer(12, "Speaker", _room).AsPlayer;
		_mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == source.Object.DBRef), Arg.Any<CancellationToken>()).Returns(source);
		_permissions.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<IPermissionService.InteractType>()).Returns(true);
		await Assert.That(await _projection.CanReceiveRoomEventAsync(_actor, _room.Object.DBRef, source.Object.DBRef, RoomEventType.Say)).IsTrue();
		await Assert.That(await _projection.CanReceiveRoomEventAsync(_actor, new DBRef(99, 0), source.Object.DBRef, RoomEventType.Say)).IsFalse();
		await Assert.That(await _projection.CanReceiveRoomEventAsync(_actor, _room.Object.DBRef, new DBRef(12, 1), RoomEventType.Say)).IsFalse();
		_reality.CanPerceiveAsync(_player.Object.DBRef, source.Object.DBRef, Arg.Any<CancellationToken>()).Returns(false);
		await Assert.That(await _projection.CanReceiveRoomEventAsync(_actor, _room.Object.DBRef, source.Object.DBRef, RoomEventType.Say)).IsFalse();
	}

	[Test]
	public async Task HiddenRoomDoesNotSuppressAPerceivedSpeaker()
	{
		var source = _objects.CreatePlayer(12, "Speaker", _room).AsPlayer;
		_mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == source.Object.DBRef), Arg.Any<CancellationToken>()).Returns(source);
		_permissions.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), IPermissionService.InteractType.Hear).Returns(true);
		_reality.CanPerceiveAsync(_player.Object.DBRef, _room.Object.DBRef, Arg.Any<CancellationToken>()).Returns(false);
		await Assert.That(await _projection.CanObserveRoomAsync(_actor, _room.Object.DBRef)).IsFalse();
		await Assert.That(await _projection.CanSubscribeRoomAsync(_actor, _room.Object.DBRef)).IsTrue();
		await Assert.That(await _projection.CanReceiveRoomEventAsync(_actor, _room.Object.DBRef, source.Object.DBRef, RoomEventType.Say)).IsTrue();
		await Assert.That(await _projection.GetStateAsync(_actor)).IsNull();
	}

	[Test]
	public async Task AccountRevocationOrHiddenRoomCannotReturnRoomMetadata()
	{
		_reality.CanPerceiveAsync(_player.Object.DBRef, _room.Object.DBRef, Arg.Any<CancellationToken>()).Returns(false);
		await Assert.That(await _projection.GetStateAsync(_actor)).IsNull();
		_reality.CanPerceiveAsync(_player.Object.DBRef, _room.Object.DBRef, Arg.Any<CancellationToken>()).Returns(true);
		_capabilities.GetGameActorAsync(_player.Object.DBRef, Arg.Any<CancellationToken>()).Returns((CapabilityActor?)null);
		await Assert.That(await _projection.GetStateAsync(_actor)).IsNull();
		await Assert.That(await _projection.ResolveCharacterAsync(_actor)).IsNull();
	}

	[Test]
	public async Task RevocationDuringContentsScanSuppressesTheWholeSnapshot()
	{
		_mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>()).Returns(Revoke());
		async IAsyncEnumerable<AnySharpContent> Revoke()
		{
			yield return _objects.CreateThing(12, "Visible", _room).AsThing;
			_capabilities.GetGameActorAsync(_player.Object.DBRef, Arg.Any<CancellationToken>()).Returns((CapabilityActor?)null);
			await Task.CompletedTask;
		}
		await Assert.That(await _projection.GetStateAsync(_actor)).IsNull();
	}

	[Test]
	public async Task StateCapsVisibleContentsWithoutMaterializingTheRoom()
	{
		var scanned = 0;
		var item = _objects.CreateThing(12, "Visible", _room).AsThing;
		_mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>()).Returns(Contents());
		async IAsyncEnumerable<AnySharpContent> Contents()
		{
			for (var i = 0; i < 2000; i++) { scanned++; yield return item; }
			await Task.CompletedTask;
		}
		var state = await _projection.GetStateAsync(_actor);
		await Assert.That(state!.VisibleObjectDbrefs.Count).IsEqualTo(VisibleWorldProjection.MaxVisibleObjects);
		await Assert.That(state.Truncated).IsTrue();
		await Assert.That(scanned).IsEqualTo(VisibleWorldProjection.MaxVisibleObjects + 1);
	}

	[Test]
	public async Task RecycledOrSubstitutedCharacterCannotUseCapturedAuthority()
	{
		var stale = _actor with { ActiveCharacter = new DBRef(11, 1), Executor = new DBRef(11, 1) };
		_capabilities.GetGameActorAsync(stale.ActiveCharacter!.Value, Arg.Any<CancellationToken>()).Returns(stale);
		await Assert.That(await _projection.ResolveCharacterAsync(stale)).IsNull();
		await Assert.That(await _projection.ResolveCharacterAsync(_actor with { Executor = new DBRef(99, 0) })).IsNull();
	}
}
