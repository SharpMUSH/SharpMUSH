using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server.Controllers;

public class OnboardingControllerTests
{
	private static OnboardingController Create(IMediator mediator, IAdministrativeCapabilityService capabilities,
		string? account = "account-a", IWorldBackupService? backups = null, string introduction = "")
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default() with { Onboarding = new() { Introduction = introduction } });
		var characters = Substitute.For<IAccountService>();
		characters.GetCharactersAsync(default!, default).ReturnsForAnyArgs(new ValueTask<IReadOnlyList<SharpPlayer>>([]));
		var principal = account is null ? new ClaimsPrincipal() : new ClaimsPrincipal(new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, account)], AccountSessionAuthenticationHandler.SchemeName));
		return new OnboardingController(mediator,
			new BearerAccountResolver(Substitute.For<IAccountSessionStore>(), Substitute.For<IAccountService>()), capabilities,
			new OnboardingCapabilityService(Substitute.For<IPackageRegistryService>(), Substitute.For<IOnboardingCommandProbe>()),
			new AdministratorChecklistDetector(mediator, options, backups ?? Substitute.For<IWorldBackupService>(),
				Substitute.For<IAttributeService>(), characters))
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

	[Test]
	public async Task DetectionRequiresConfigurationPermission()
	{
		var backups = Substitute.For<IWorldBackupService>();
		var controller = Create(Substitute.For<IMediator>(), Substitute.For<IAdministrativeCapabilityService>(), backups: backups);
		await Assert.That(await controller.Detected(default)).IsTypeOf<ForbidResult>();
		backups.DidNotReceive().List();
	}

	/// <summary>
	/// A saved introduction and a listed backup are reported; a step this account already verified is not
	/// looked at again.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DetectionReportsWhatTheGameShowsAndSkipsVerifiedSteps(bool backupVerified)
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetOnboardingProgressQuery>(), Arg.Any<CancellationToken>())
			.Returns(new OnboardingProgress { CompletedStepIds = backupVerified ? ["backups"] : [] });
		mediator.CreateStream(Arg.Any<GetChannelListQuery>(), Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<SharpChannel>());
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.ConfigAdmin, Arg.Any<CancellationToken>()).Returns(true);
		var backups = Substitute.For<IWorldBackupService>();
		backups.List().Returns([new WorldBackup("copy", "/backups/copy", DateTimeOffset.UnixEpoch, 1)]);
		var controller = Create(mediator, capabilities, backups: backups, introduction: "Welcome to the harbour.");

		var detected = ((IEnumerable<string>)((OkObjectResult)await controller.Detected(default)).Value!).ToArray();

		await Assert.That(detected).IsEquivalentTo(backupVerified ? ["identity"] : new[] { "identity", "backups" });
		backups.Received(backupVerified ? 0 : 1).List();
	}
}
