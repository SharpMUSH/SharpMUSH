using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// <c>api/admin/characters</c> against the shared world: the list finds a character by name with its
/// connection state, and the detail keeps the connecting site from a caller without server.admin.
/// </summary>
public class AdminCharactersControllerTests : ServerTestBase
{
	private AdminCharactersController Controller(bool serverAdmin)
	{
		var authorization = Substitute.For<IAuthorizationService>();
		authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>())
			.Returns(serverAdmin ? AuthorizationResult.Success() : AuthorizationResult.Failed());
		var services = WebAppFactoryArg.Services;
		return new AdminCharactersController(
			services.GetRequiredService<IMediator>(),
			services.GetRequiredService<IAccountService>(),
			services.GetRequiredService<IConnectionService>(),
			authorization,
			Substitute.For<IVisibleWorldProjection>(),
			Substitute.For<IEngineCommandInvoker>(),
			Substitute.For<IEventService>(),
			Substitute.For<IAuditLog>())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "Test")) }
			}
		};
	}

	[Test]
	public async Task TheListFindsACharacterByName_WithItsConnectionState()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AdmList");
		try
		{
			var result = await Controller(serverAdmin: false).List(search: player.Name, online: true);

			var page = (AdminCharacterPage)((OkObjectResult)result).Value!;
			await Assert.That(page.Total).IsEqualTo(1);
			await Assert.That(page.Characters[0].DbrefNumber).IsEqualTo(player.DbRef.Number);
			await Assert.That(page.Characters[0].Online).IsTrue();

			var offline = (AdminCharacterPage)((OkObjectResult)await Controller(serverAdmin: false)
				.List(search: player.Name, online: false)).Value!;
			await Assert.That(offline.Total).IsEqualTo(0);
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task TheDetailShowsTheSiteOnlyToServerAdmin(bool serverAdmin)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "AdmSite");
		await Cmd($"&LASTSITE #{player.Number}=site.example");

		var result = await Controller(serverAdmin).Get(player.Number, CancellationToken.None);

		var detail = (AdminCharacterDetail)((OkObjectResult)result).Value!;
		await Assert.That(detail.Character.Online).IsFalse();
		await Assert.That(detail.LastSite).IsEqualTo(serverAdmin ? "site.example" : null);
	}

	[Test]
	public async Task TheDetailOfAMissingCharacterIsNotFound()
		=> await Assert.That(await Controller(serverAdmin: false).Get(int.MaxValue - 7, CancellationToken.None))
			.IsTypeOf<NotFoundResult>();
}
