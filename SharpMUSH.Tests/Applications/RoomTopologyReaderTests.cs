using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Tests.Server;
using Mediator;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Applications;

public class RoomTopologyReaderTests
{
	private readonly TestObjectFactory _objects = new();
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
	private readonly IAttributeService _attributes = Substitute.For<IAttributeService>();
	private readonly IRealityPolicy _reality = Substitute.For<IRealityPolicy>();
	private readonly SharpRoom _a;
	private readonly SharpRoom _b;
	private readonly AnySharpObject _actor;

	public RoomTopologyReaderTests()
	{
		_a = _objects.CreateRoom(10, "Same label"); _b = _objects.CreateRoom(20, "Same label");
		_objects.CreateRoom(30, "Disconnected");
		_actor = _objects.CreatePlayer(1, "Builder", _a);
		_mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			ValueTask.FromResult<AnyOptionalSharpObject>(_objects.GetAllRooms().Select(r => new AnySharpObject(r)).Concat(_objects.GetAllObjects()).FirstOrDefault(o => o.Object().DBRef.Number == call.Arg<GetObjectNodeQuery>().DBRef.Number) is { } found ? found : new None()));
		_permissions.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		_reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		_mediator.CreateStream(Arg.Any<GetExitsQuery>(), Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<SharpExit>());
		_attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "_LINKTYPE", IAttributeService.AttributeMode.Read, false).Returns(new None());
	}

	private RoomTopologyReader Reader => new(_mediator, _permissions, _reality, _attributes);
	private void Exits(params SharpExit[] exits) => _mediator.CreateStream(Arg.Any<GetExitsQuery>(), Arg.Any<CancellationToken>())
		.Returns(exits.ToAsyncEnumerable());

	[Test]
	public async Task DirectedAndDisconnectedRoomsUseDbrefsDespiteIdenticalNames()
	{
		var exit = _objects.CreateExit(11, "one way", [], _a, _b).Expect<SharpExit>();
		Exits(exit);
		var topology = await Reader.ReadAsync(_actor, [10, 20, 30], default);
		await Assert.That(topology.Rooms.Select(r => r.Dbref)).IsEquivalentTo(["#10", "#20", "#30"]);
		await Assert.That(topology.Exits.Count).IsEqualTo(1);
		await Assert.That(topology.Exits[0].Source).IsEqualTo("#10");
		await Assert.That(topology.Exits[0].Destination).IsEqualTo("#20");
	}

	[Test]
	public async Task UnreadableDestinationDoesNotDiscloseItsIdentity()
	{
		Exits(_objects.CreateExit(11, "secret", [], _a, _b).Expect<SharpExit>());
		_reality.CanPerceiveAsync(_actor.Object().DBRef, _b.Object.DBRef, Arg.Any<CancellationToken>()).Returns(false);
		var topology = await Reader.ReadAsync(_actor, [10, 20], default);
		await Assert.That(topology.Rooms.Count).IsEqualTo(1);
		await Assert.That(topology.Exits[0].Destination).IsNull();
		await Assert.That(topology.Exits[0].Status).IsEqualTo("unavailable");
	}

	[Test]
	public async Task DynamicExitDoesNotResolveItsDestinationRelationOrReadExecutableCode()
	{
		var exit = _objects.CreateExit(11, "generated", [], _a).Expect<SharpExit>();
		exit.Home = new(_ => throw new InvalidOperationException("Dynamic destination relation must not be resolved."));
		Exits(exit);
		var attribute = new SharpAttribute("a", "a", "_LINKTYPE", [], null, "_LINKTYPE", null!, null!, null!) { Value = MarkupText.Plain("variable") };
		_attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "_LINKTYPE", IAttributeService.AttributeMode.Read, false).Returns(new[] { attribute });
		var topology = await Reader.ReadAsync(_actor, [10], default);
		await Assert.That(topology.Exits[0].Status).IsEqualTo("dynamic");
		await Assert.That(topology.Exits[0].Destination).IsNull();
	}

	[Test]
	public async Task PrivateLinkMarkerDoesNotDiscloseOrResolveDestination()
	{
		var exit = _objects.CreateExit(11, "private marker", [], _a).Expect<SharpExit>();
		exit.Home = new(_ => throw new InvalidOperationException("An unreadable marker must leave the destination unresolved."));
		Exits(exit);
		_attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "_LINKTYPE", IAttributeService.AttributeMode.Read, false).Returns(new Error<string>("Permission denied"));
		var topology = await Reader.ReadAsync(_actor, [10], default);
		await Assert.That(topology.Exits[0].Status).IsEqualTo("unavailable");
		await Assert.That(topology.Exits[0].Destination).IsNull();
	}

	[Test]
	public async Task CandidateScanIsBoundedEvenWhenEveryExitIsUnreadable()
	{
		var exit = _objects.CreateExit(11, "hidden", [], _a).Expect<SharpExit>();
		_permissions.CanExamine(Arg.Any<AnySharpObject>(), Arg.Is<AnySharpObject>(o => o.Object().DBRef.Number == 11)).Returns(false);
		var scanned = 0;
		async IAsyncEnumerable<SharpExit> Many() { for (var i = 0; i < 5000; i++) { scanned++; yield return exit; } await Task.CompletedTask; }
		_mediator.CreateStream(Arg.Any<GetExitsQuery>(), Arg.Any<CancellationToken>()).Returns(Many());
		var topology = await Reader.ReadAsync(_actor, [10], default);
		await Assert.That(topology.Truncated).IsTrue();
		await Assert.That(scanned).IsEqualTo(RoomTopologyReader.MaxCandidates + 1);
	}
	[Test]
	public async Task ExaminationDenialHidesTheRoomAndItsExitInventory()
	{
		_permissions.CanExamine(Arg.Any<AnySharpObject>(), Arg.Is<AnySharpObject>(o => o.Object().DBRef.Number == 10)).Returns(false);
		Exits(_objects.CreateExit(11, "hidden inventory", [], _a, _b).Expect<SharpExit>());
		var topology = await Reader.ReadAsync(_actor, [10], default);
		await Assert.That(topology.Rooms.Count).IsEqualTo(0);
		await Assert.That(topology.Exits.Count).IsEqualTo(0);
	}

	[Test]
	public async Task CharacterRevokedDuringReadCannotReturnTheGraph()
	{
		var projection = Substitute.For<IVisibleWorldProjection>();
		var resolutions = 0;
		projection.ResolveCharacterAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>())
			.Returns(_ => new ValueTask<SharpPlayer?>(++resolutions == 1 ? _actor.Expect<SharpPlayer>() : null));
		var controller = new ObjectsController(_mediator, _attributes,
			new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create()), Substitute.For<IEngineCommandInvoker>(), _permissions, _reality, projection)
		{
			ControllerContext = new()
			{
				HttpContext = new DefaultHttpContext
				{
					User = new(new ClaimsIdentity([
				new Claim(ClaimTypes.NameIdentifier, "test-account"), new Claim(GameHub.CharacterDbrefClaim, _actor.Object().DBRef.ToString())], "test"))
				}
			}
		};
		await Assert.That(await controller.GetTopology([10], default)).IsTypeOf<UnauthorizedResult>();
	}

}
