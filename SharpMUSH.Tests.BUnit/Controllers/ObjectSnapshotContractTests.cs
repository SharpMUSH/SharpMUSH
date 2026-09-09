using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Snapshots;
using SharpMUSH.Library.Services.Snapshots;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.BUnit.Controllers;

public class ObjectSnapshotContractTests
{
	[Test]
	public async Task RestoreForwardsAuthenticatedActiveActorAndConcretePreview()
	{
		var service = Substitute.For<IObjectSnapshotService>();
		var selection = new SnapshotSelection(["DESC"], Name: true);
		service.RestoreAsync(Arg.Any<CapabilityActor>(), Arg.Any<DBRef>(), "saved", selection, "preview", Arg.Any<CancellationToken>())
			.Returns(new SnapshotRestoreResult(true, "recovery", null));
		var controller = Controller(service, true);
		var result = await controller.Restore(new("#10:123", "saved", selection, "preview"), default);
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		await service.Received(1).RestoreAsync(new("account", new DBRef(7, 1), new DBRef(7, 1)), new(10, 123), "saved", selection, "preview", default);
	}

	[Test]
	public async Task MissingActiveCharacterFailsBeforeServiceInvocation()
	{
		var service = Substitute.For<IObjectSnapshotService>();
		var result = await Controller(service, false).Capture(new("#10:123", "capture"), default);
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(403);
		await service.DidNotReceive().CaptureAsync(Arg.Any<CapabilityActor>(), Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
	}

	private static ObjectSnapshotsController Controller(IObjectSnapshotService service, bool active)
	{
		var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "account") };
		if (active) claims.Add(new(GameHub.CharacterDbrefClaim, "#7:1"));
		return new(service)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{ User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
			}
		};
	}
}
