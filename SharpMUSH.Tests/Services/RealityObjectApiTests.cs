using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

public class RealityObjectApiTests
{
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task ObjectMetadataRequiresCurrentLinkAndPerception(bool linked)
	{
		var objects = new TestObjectFactory();
		var viewer = objects.CreatePlayer(10, "Viewer").AsPlayer;
		var hidden = objects.CreateThing(11, "Secret", owner: viewer).AsThing;
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			ValueTask.FromResult<AnyOptionalSharpObject>(call.Arg<GetObjectNodeQuery>().DBRef.Number == 10 ? viewer : hidden));
		var permissions = Substitute.For<IPermissionService>();
		permissions.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(!linked);
		var projection = Substitute.For<IVisibleWorldProjection>();
		projection.ResolveCharacterAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(linked ? viewer : null);
		var controller = new ObjectsController(mediator, Substitute.For<IAttributeService>(),
			Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IEngineCommandInvoker>(), permissions, reality, projection)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity(new[]
					{
						new Claim(ClaimTypes.NameIdentifier, "account"),
						new Claim(GameHub.CharacterDbrefClaim, viewer.Object.DBRef.ToString())
					}, "test"))
				}
			}
		};
		var result = await controller.GetObject(11, CancellationToken.None);
		if (linked) await Assert.That(result).IsTypeOf<NotFoundResult>();
		else await Assert.That(result).IsTypeOf<UnauthorizedResult>();
	}
}
