using Mediator;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/onboarding/progress")]
[RequestSizeLimit(16 * 1024)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OnboardingController(IMediator mediator, BearerAccountResolver accounts,
	IAdministrativeCapabilityService capabilities, OnboardingCapabilityService guideCapabilities) : ControllerBase
{
	[HttpGet("/api/onboarding/capabilities")]
	public async Task<IActionResult> Capabilities(CancellationToken cancellationToken)
		=> await accounts.ResolveCharacterAsync(this) switch
		{
			SignedInCharacter account => Ok(await guideCapabilities.ResolveAsync(account.Character.Object.DBRef, cancellationToken)),
			ActionResult refused => refused
		};

	[HttpGet("{journey}")]
	public async Task<IActionResult> Get(string journey, CancellationToken cancellationToken)
		=> await accounts.ResolveAsync(this) switch
		{
			SignedInAccount account => await ReadAsync(account, journey, cancellationToken),
			ActionResult refused => refused
		};

	[HttpPut("{journey}")]
	public async Task<IActionResult> Save(string journey, SaveOnboardingRequest request, CancellationToken cancellationToken)
		=> await accounts.ResolveAsync(this) switch
		{
			SignedInAccount account => await WriteAsync(account, journey, request, cancellationToken),
			ActionResult refused => refused
		};

	private async Task<IActionResult> ReadAsync(SignedInAccount account, string journey, CancellationToken ct)
	{
		if (!TryJourney(journey, out var selected)) return BadRequest("Unknown guide journey.");
		if (!await CanAccessAsync(account, selected, ct)) return Forbid();
		return Ok(await mediator.Send(new GetOnboardingProgressQuery(account.Id, selected), ct));
	}

	private async Task<IActionResult> WriteAsync(SignedInAccount account, string journey, SaveOnboardingRequest request, CancellationToken ct)
	{
		if (!TryJourney(journey, out var selected)) return BadRequest("Unknown guide journey.");
		if (!await CanAccessAsync(account, selected, ct)) return Forbid();
		if (request is null || !GuideStepIds.IsValid(selected, request.Progress, request.ExpectedRevision))
			return BadRequest("Invalid onboarding progress or unsupported version.");
		return await mediator.Send(new SaveOnboardingProgressCommand(account.Id, selected, request.Progress, request.ExpectedRevision), ct) switch
		{
			OnboardingProgress saved => Ok(saved),
			OnboardingConflict conflict => Conflict(conflict),
			Error<string> error => BadRequest(error.Value)
		};
	}

	private Task<bool> CanAccessAsync(SignedInAccount account, GuideJourney journey, CancellationToken ct)
		=> journey == GuideJourney.Player ? Task.FromResult(true)
			: capabilities.AuthorizeAsync(new CapabilityActor(account.Id), PortalPermission.ConfigAdmin, ct);

	private static bool TryJourney(string value, out GuideJourney journey)
	{
		journey = value == "administrator" ? GuideJourney.Administrator : GuideJourney.Player;
		return value is "player" or "administrator";
	}
}
