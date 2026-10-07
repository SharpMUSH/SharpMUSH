using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Controllers;

public class AuthControllerDebugOttTests
{
	private static AuthController CreateController(string environmentName)
	{
		var env = Substitute.For<IHostEnvironment>();
		env.EnvironmentName.Returns(environmentName);

		var options = Substitute.For<SharpMUSH.Library.Services.Interfaces.IOptionsWrapper<SharpMUSHOptions>>();

		var claimsCache = new ZiggyCreatures.Caching.Fusion.FusionCache(
			new Microsoft.Extensions.Options.OptionsWrapper<ZiggyCreatures.Caching.Fusion.FusionCacheOptions>(
				new ZiggyCreatures.Caching.Fusion.FusionCacheOptions()));
		return new AuthController(
			Substitute.For<Mediator.IMediator>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IPasswordService>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IOttStore>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IAccountService>(),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IAccountSessionStore>(),
			new SharpMUSH.Server.Authentication.AccountClaimsService(
				Substitute.For<SharpMUSH.Library.Authorization.IAdministrativeCapabilityService>(),
				claimsCache,
				new SharpMUSH.Server.Authentication.AccountClaimsInvalidator(claimsCache),
				Substitute.For<Microsoft.Extensions.Logging.ILogger<SharpMUSH.Server.Authentication.AccountClaimsService>>()),
			options,
			env,
			new SharpMUSH.Server.Authentication.SitelockGuard(options),
			new SharpMUSH.Server.Authentication.Passkeys.PasskeyService(
				Substitute.For<SharpMUSH.Library.IAccountStore>(),
				new SharpMUSH.Server.Authentication.Passkeys.PasskeyRelyingParty(
					new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), options),
				new SharpMUSH.Server.Authentication.Passkeys.PasskeyCeremonyStore(TimeProvider.System),
				TimeProvider.System,
				Substitute.For<Microsoft.Extensions.Logging.ILogger<SharpMUSH.Server.Authentication.Passkeys.PasskeyService>>()),
			Substitute.For<SharpMUSH.Library.Services.Interfaces.IPortalThemeService>(),
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
