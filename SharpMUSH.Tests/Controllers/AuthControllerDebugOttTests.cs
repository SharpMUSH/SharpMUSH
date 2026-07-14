using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Controllers;

public class AuthControllerDebugOttTests
{
	private static AuthController CreateController(string environmentName)
	{
		var env = Substitute.For<IHostEnvironment>();
		env.EnvironmentName.Returns(environmentName);

		return new AuthController(
			Substitute.For<Mediator.IMediator>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IPasswordService>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IOttStore>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IAccountService>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IAccountSessionStore>(),
			Substitute.For<SharpMUSH.Library.Authorization.IRoleDerivationService>(),
			env,
			Substitute.For<Microsoft.Extensions.Logging.ILogger<AuthController>>());
	}

	[Test]
	public async Task DebugOtt_InProduction_Returns404()
	{
		var controller = CreateController(Environments.Production);
		var result = await controller.GetDebugOtt();
		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}
}
