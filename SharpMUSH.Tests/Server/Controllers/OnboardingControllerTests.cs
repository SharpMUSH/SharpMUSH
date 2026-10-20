using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server.Controllers;

public class OnboardingControllerTests
{
	private static OnboardingController Create(IMediator mediator, IAdministrativeCapabilityService capabilities,
		string? account = "account-a")
	{
		var principal = account is null ? new ClaimsPrincipal() : new ClaimsPrincipal(new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, account)], AccountSessionAuthenticationHandler.SchemeName));
		return new OnboardingController(mediator,
			new BearerAccountResolver(Substitute.For<IAccountSessionStore>(), Substitute.For<IAccountService>()), capabilities,
			new OnboardingCapabilityService(Substitute.For<IPackageRegistryService>(), Substitute.For<IOnboardingCommandProbe>()))
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
		};
	}

	[Test]
	public async Task MissingAccountCannotReadOrWriteProgress()
	{
		var mediator = Substitute.For<IMediator>();
		var controller = Create(mediator, Substitute.For<IAdministrativeCapabilityService>(), null);
		await Assert.That(await controller.Get("player", default)).IsTypeOf<UnauthorizedObjectResult>();
		await Assert.That(await controller.Save("player", new(new(), 0), default)).IsTypeOf<UnauthorizedObjectResult>();
		await mediator.DidNotReceive().Send(Arg.Any<GetOnboardingProgressQuery>(), Arg.Any<CancellationToken>());
		await mediator.DidNotReceive().Send(Arg.Any<SaveOnboardingProgressCommand>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ReadsAreBoundToTheAuthenticatedAccount()
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetOnboardingProgressQuery>(), Arg.Any<CancellationToken>()).Returns(new OnboardingProgress());
		await Create(mediator, Substitute.For<IAdministrativeCapabilityService>()).Get("player", default);
		await mediator.Received(1).Send(Arg.Is<GetOnboardingProgressQuery>(q => q.AccountId == "account-a"
			&& q.Journey == GuideJourney.Player), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task AdministratorJourneyRequiresConfigurationPermission()
	{
		var mediator = Substitute.For<IMediator>();
		var controller = Create(mediator, Substitute.For<IAdministrativeCapabilityService>());
		await Assert.That(await controller.Get("administrator", default)).IsTypeOf<ForbidResult>();
		await Assert.That(await controller.Save("administrator", new(new(), 0), default)).IsTypeOf<ForbidResult>();
		await mediator.DidNotReceive().Send(Arg.Any<SaveOnboardingProgressCommand>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task StaleSaveReturnsConflictAndInvalidProgressDoesNotReachStore()
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<SaveOnboardingProgressCommand>(), Arg.Any<CancellationToken>())
			.Returns(new OnboardingSaveResult(new OnboardingConflict(2)));
		var controller = Create(mediator, Substitute.For<IAdministrativeCapabilityService>());
		await Assert.That(await controller.Save("player", new(new(), 0), default)).IsTypeOf<ConflictObjectResult>();
		await Assert.That(await controller.Save("player", new(new() { CurrentStepId = "made-up" }, 0), default)).IsTypeOf<BadRequestObjectResult>();
		await mediator.Received(1).Send(Arg.Any<SaveOnboardingProgressCommand>(), Arg.Any<CancellationToken>());
	}
}
