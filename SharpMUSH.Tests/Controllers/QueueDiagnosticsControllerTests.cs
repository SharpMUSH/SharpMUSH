using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Controllers;

public class QueueDiagnosticsControllerTests
{
	private static QueueDiagnosticsController Controller(IQueueDiagnosticsService service, string? account = "account")
		=> new(service)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity(account is null ? []
						: new[] { new Claim(ClaimTypes.NameIdentifier, account) }, "test"))
				}
			}
		};

	[Test]
	[Arguments("#1")]
	[Arguments("garbage")]
	[Arguments("")]
	public async Task BareOrInvalidCharacterNeverReachesService(string character)
	{
		var service = Substitute.For<IQueueDiagnosticsService>();
		var controller = Controller(service);
		await Assert.That(((StatusCodeResult)await controller.Inspect(character)).StatusCode).IsEqualTo(403);
		await Assert.That(((StatusCodeResult)await controller.Start(new(character))).StatusCode).IsEqualTo(403);
		await Assert.That(((StatusCodeResult)await controller.Stop(character)).StatusCode).IsEqualTo(403);
		await Assert.That(service.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	public async Task TrustedAccountAndFullSelectedCharacterReachSharedAuthorization()
	{
		var service = Substitute.For<IQueueDiagnosticsService>();
		var actor = new CapabilityActor("account", DBRef.Parse("#7:123"), DBRef.Parse("#7:123"));
		service.InspectAsync(actor, 10, 90, Arg.Any<CancellationToken>()).Returns(DiagnosticsError.PermissionDenied);
		var response = await Controller(service).Inspect("#7:123", 10, 90);
		await Assert.That(((StatusCodeResult)response).StatusCode).IsEqualTo(403);
		await service.Received(1).InspectAsync(actor, 10, 90, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task MissingAccountCannotInspectEvenWithFullCharacter()
	{
		var service = Substitute.For<IQueueDiagnosticsService>();
		var response = await Controller(service, null).Inspect("#7:123");
		await Assert.That(((StatusCodeResult)response).StatusCode).IsEqualTo(403);
		await Assert.That(service.ReceivedCalls().Any()).IsFalse();
	}
}
