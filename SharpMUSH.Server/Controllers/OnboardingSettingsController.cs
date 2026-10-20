using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/onboarding/settings")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
[RequestSizeLimit(16 * 1024)]
public sealed class OnboardingSettingsController(IOptionsWrapper<SharpMUSHOptions> options, IConfigOptionWriter writer,
	IAuditLog audit) : ControllerBase
{
	[HttpGet]
	public IActionResult Get() => Ok(Project(options.CurrentValue));

	[HttpPut]
	public async Task<IActionResult> Put(OnboardingSettings request, CancellationToken ct)
	{
		if (request is null || !Valid(request)) return BadRequest("Check the game name, genre values, guide links and artwork selection.");
		var saved = await writer.UpdateAsync(current =>
		{
			var variables = current.Mssp.Variables.Where(pair => !string.Equals(pair.Key, "GENRE", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(pair.Key, "SUBGENRE", StringComparison.OrdinalIgnoreCase)).ToDictionary(pair => pair.Key, pair => pair.Value);
			if (request.Genres.Length > 0) variables["GENRE"] = [.. request.Genres];
			if (request.Subgenres.Length > 0) variables["SUBGENRE"] = [.. request.Subgenres];
			return current with
			{
				Net = current.Net with { MudName = request.MudName.Trim() },
				Mssp = new(variables),
				Onboarding = new()
				{
					Introduction = request.Introduction, WelcomePage = request.WelcomePage, RulesPage = request.RulesPage,
					CharacterGuidePage = request.CharacterGuidePage, ArtStyle = request.ArtStyle, ArtOverrideId = request.ArtOverrideId
				}
			};
		}, ct);
		await audit.RecordPortalAsync(User, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, "onboarding"),
			JsonSerializer.Serialize(request));
		return Ok(Project(saved));
	}

	private static bool Valid(OnboardingSettings settings)
		=> Text(settings.MudName, 128) && !string.IsNullOrWhiteSpace(settings.MudName)
			&& settings.Introduction is { Length: <= 1000 } && !settings.Introduction.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')
			&& Values(settings.Genres) && Values(settings.Subgenres)
			&& Page(settings.WelcomePage) && Page(settings.RulesPage) && Page(settings.CharacterGuidePage)
			&& SharpieArtCatalog.Styles.Contains(settings.ArtStyle)
			&& (settings.ArtOverrideId == "" || SharpieArtCatalog.Approved.Any(art => art.Id == settings.ArtOverrideId && art.Style == settings.ArtStyle));

	private static bool Text(string? value, int max) => value is not null && value.Length <= max && !value.Any(char.IsControl);
	private static bool Values(string[]? values) => values is { Length: <= 16 } && values.All(value => Text(value, 128) && !string.IsNullOrWhiteSpace(value));
	private static bool Page(string? value) => value == "" || OnboardingPresentationService.TryPage(value, out _, out _);
	private static string[] ValuesOf(SharpMUSHOptions current, string key) => current.Mssp.Variables
		.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? [];
	private static OnboardingSettings Project(SharpMUSHOptions current)
	{
		var guide = current.Onboarding ?? new();
		return new(current.Net.MudName, ValuesOf(current, "GENRE"), ValuesOf(current, "SUBGENRE"), guide.Introduction,
			guide.WelcomePage, guide.RulesPage, guide.CharacterGuidePage, guide.ArtStyle, guide.ArtOverrideId);
	}
}
