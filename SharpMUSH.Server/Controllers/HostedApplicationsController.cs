using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Applications;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Server.Authentication;
using Microsoft.AspNetCore.Http;

namespace SharpMUSH.Server.Controllers;

/// <summary>The reusable host's public code assets and authenticated instance bootstrap.</summary>
[ApiController]
public sealed class HostedApplicationsController(
	UiPackageStore packages, IApplicationRegistryService registry, IOptionsWrapper<SharpMUSHOptions> options,
	IOAuthService oauth, BearerAccountResolver bearer) : ControllerBase
{
	[AllowAnonymous]
	[HttpGet("api/ui/{id}/assets/{file}")]
	public async Task<IActionResult> Asset(string id, string file, CancellationToken ct)
	{
		if (!options.CurrentValue.Database.AllowBrowserCode || file == UiDefinition.FileName) return NotFound();
		if (await packages.ReadAsync(id, file, ct) is not { } bytes || UiDefinition.ContentType(file) is not { } contentType)
			return NotFound();
		Response.Headers.CacheControl = "no-cache";
		return File(bytes, contentType);
	}

	[Authorize]
	[HttpGet("api/applications/{slug}/host")]
	public async Task<IActionResult> Bootstrap(string slug, CancellationToken ct)
		=> await HostedAsync(slug, ct) switch
		{
			HostedInstance hosted => Booted(hosted),
			ActionResult refused => refused,
		};

	private OkObjectResult Booted(HostedInstance hosted)
	{
		var (app, definition, config) = hosted;
		Response.Headers.CacheControl = "no-store";
		return Ok(new
		{
			instanceId = app.Slug,
			definitionId = app.DefinitionId,
			configVersion = app.ConfigVersion,
			config,
			scopes = definition.RequestedScopes,
			entryUrl = $"api/ui/{Uri.EscapeDataString(app.DefinitionId!)}/assets/{Uri.EscapeDataString(definition.Entry)}"
		});
	}

	/// <summary>
	/// An access token for the instance's package, acting as the session's character, kept by the portal host and never
	/// handed to the package's frame. 403 with what to ask when the character has not allowed what the package asks for.
	/// </summary>
	[Authorize]
	[HttpPost("api/applications/{slug}/token")]
	public async Task<IActionResult> Token(string slug, CancellationToken ct)
		=> await HostedAsync(slug, ct) switch
		{
			HostedInstance hosted => await bearer.ResolveCharacterAsync(this) switch
			{
				SignedInCharacter signedIn => await TokenAsync(hosted, signedIn, ct),
				ActionResult refused => refused,
			},
			ActionResult refused => refused,
		};

	private async Task<IActionResult> TokenAsync(HostedInstance hosted, SignedInCharacter signedIn, CancellationToken ct)
	{
		Response.Headers.CacheControl = "no-store";
		return await oauth.IssueHostedTokenAsync(signedIn.AccountId, signedIn.Character.Object.DBRef, hosted.Definition.Id, ct) switch
		{
			IssuedTokens tokens => Ok(new OAuthHostedToken(tokens.AccessToken, tokens.ExpiresIn, tokens.Scopes)),
			ConsentRequired consent => StatusCode(StatusCodes.Status403Forbidden,
				new OAuthHostedConsent(OAuthController.View(consent.App), consent.Scopes, signedIn.Character.Object.Name)),
			Library.DiscriminatedUnions.NotFound => NotFound(),
		};
	}

	/// <summary>The session's character allows the instance's package what it asks for; the answer is its token.</summary>
	[Authorize]
	[HttpPost("api/applications/{slug}/allow")]
	public async Task<IActionResult> Allow(string slug, CancellationToken ct)
		=> await HostedAsync(slug, ct) switch
		{
			HostedInstance hosted => await bearer.ResolveCharacterAsync(this) switch
			{
				SignedInCharacter signedIn => await oauth.AllowHostedAsync(signedIn.AccountId, signedIn.Character.Object.DBRef,
						hosted.Definition.Id, ct) is OAuthGrant
					? await TokenAsync(hosted, signedIn, ct)
					: NotFound(),
				ActionResult refused => refused,
			},
			ActionResult refused => refused,
		};

	private sealed record HostedInstance(RegisteredApplication App, UiDefinition Definition, System.Text.Json.JsonElement Config);

	private union Hosted(HostedInstance, ActionResult);

	/// <summary>The hosted instance at <paramref name="slug"/>, when browser code is on and the caller may open it.</summary>
	private async Task<Hosted> HostedAsync(string slug, CancellationToken ct)
	{
		if (!options.CurrentValue.Database.AllowBrowserCode
			|| await registry.GetApplicationAsync(slug) is not RegisteredApplication app
			|| app.RenderKind != ApplicationRenderKind.Hosted || app.DefinitionId is null) return NotFound();
		var role = User.FindAll(ClaimTypes.Role)
			.Select(c => Enum.TryParse<PortalRole>(c.Value, true, out var r) ? r : PortalRole.Guest)
			.DefaultIfEmpty(PortalRole.Guest).Max();
		if (role < app.MinimumRole || (app.Permission is { Length: > 0 } scope
			&& !User.FindAll(PortalPermission.ClaimType).Any(c => string.Equals(c.Value, scope, StringComparison.OrdinalIgnoreCase))))
			return Forbid();
		if (await packages.GetAsync(app.DefinitionId, ct) is not { } definition) return NotFound();
		if (app.Config is not { } config || definition.ValidateConfig(app.ConfigVersion, config) is { })
			return Conflict(new { error = "Instance configuration does not match the installed definition; update it explicitly." });
		return new HostedInstance(app, definition, config);
	}
}
