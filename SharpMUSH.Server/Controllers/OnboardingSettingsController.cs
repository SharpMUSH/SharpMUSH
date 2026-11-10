using System.Text.Json;
using SharpMUSH.Configuration.Mssp;
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
// Retained MSSP arrays can contain 32 values of 512 characters, including JSON-escaped text.
[RequestSizeLimit(256 * 1024)]
public sealed class OnboardingSettingsController(IOptionsWrapper<SharpMUSHOptions> options, IConfigOptionWriter writer,
	IAuditLog audit) : ControllerBase
{
	[HttpGet]
	public IActionResult Get() => Ok(Project(options.CurrentValue));

	[HttpPut]
	public async Task<IActionResult> Put(OnboardingSettings request, CancellationToken ct)
	{
		var original = options.CurrentValue;
		if (request is null || !Valid(request, original)) return BadRequest("Check the game name, genre values, guide links and artwork selection.");
		var keepGenres = request.Genres.SequenceEqual(ValuesOf(original, "GENRE"));
		var keepSubgenres = request.Subgenres.SequenceEqual(ValuesOf(original, "SUBGENRE"));
		var saved = await writer.UpdateAsync(current =>
		{
			// Leave untouched metadata in the writer's current snapshot, including its original values
			// and spelling. Only an explicit new choice or custom subgenre replaces a field.
			var variables = current.Mssp.Variables.Where(pair => (keepGenres || !string.Equals(pair.Key, "GENRE", StringComparison.OrdinalIgnoreCase))
				&& (keepSubgenres || !string.Equals(pair.Key, "SUBGENRE", StringComparison.OrdinalIgnoreCase))).ToDictionary(pair => pair.Key, pair => pair.Value);
			if (!keepGenres) variables["GENRE"] = [.. request.Genres];
			if (!keepSubgenres) variables["SUBGENRE"] = [.. request.Subgenres];
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

	private static bool Valid(OnboardingSettings settings, SharpMUSHOptions current)
		=> Text(settings.MudName, 128) && !string.IsNullOrWhiteSpace(settings.MudName)
			&& settings.Introduction is { Length: <= 1000 } && !settings.Introduction.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')
			&& ChoiceOrUnchanged("GENRE", settings.Genres, current) && SubgenreOrUnchanged(settings.Subgenres, current)
			&& Page(settings.WelcomePage) && Page(settings.RulesPage) && Page(settings.CharacterGuidePage)
			&& SharpieArtCatalog.Styles.Contains(settings.ArtStyle)
			&& (settings.ArtOverrideId == "" || SharpieArtCatalog.Approved.Any(art => art.Id == settings.ArtOverrideId && art.Style == settings.ArtStyle));

	private static bool Text(string? value, int max) => value is not null && value.Length <= max && !value.Any(char.IsControl);
	private static bool ChoiceOrUnchanged(string variable, string[]? values, SharpMUSHOptions current)
		=> values is not null && (values.SequenceEqual(ValuesOf(current, variable))
			|| values is { Length: 1 } && MsspCatalog.Find(variable)!.Choices.Contains(values[0]));
	private static bool SubgenreOrUnchanged(string[]? values, SharpMUSHOptions current)
		=> values is not null && (values.SequenceEqual(ValuesOf(current, "SUBGENRE"))
			|| values is { Length: 1 } && Text(values[0], MsspCatalog.MaxValueLength) && !string.IsNullOrWhiteSpace(values[0]));
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
