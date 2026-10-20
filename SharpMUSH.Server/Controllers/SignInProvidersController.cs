using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.External;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The services players can sign in with (Discord, Google, ...), as staff set them up. A provider's client secret
/// goes in once and is never sent back; it is kept as the secret <c>signin.&lt;id&gt;.secret</c>.
/// </summary>
[ApiController]
[Route("api/admin/sign-in-providers")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public class SignInProvidersController(
	ExternalSignInProviders providers,
	ExternalSignInService externalSignIn,
	ISecretStore secrets,
	IAuditLog audit) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<SignInProviderSettings>>> List()
	{
		List<SignInProviderSettings> listed = [];
		foreach (var provider in await providers.ListAsync())
		{
			listed.Add(await SettingsAsync(provider));
		}

		return Ok(listed);
	}

	/// <summary>The address to register with a service for a provider not added yet, as this page reaches the server.</summary>
	[HttpGet("redirect-uri/{id}")]
	public ActionResult<SignInProviderRedirect> RedirectUri(string id)
		=> ExternalSignInProviders.IsValidId(id)
			? Ok(new SignInProviderRedirect(externalSignIn.RedirectUriFor(id, Request) ?? string.Empty))
			: BadRequest(new { error = "A provider's id is lower-case letters and digits." });

	[HttpPut("{id}")]
	public async Task<ActionResult<SignInProviderSettings>> Save(string id, [FromBody] SignInProviderUpdate update)
	{
		var isNew = await providers.GetAsync(id) is NotFound;
		if (isNew && string.IsNullOrEmpty(update.ClientSecret))
		{
			return BadRequest(new { error = "A new provider needs its client secret." });
		}

		return await providers.SaveAsync(id, update) switch
		{
			ExternalSignInProvider saved => await SavedAsync(saved, update.ClientSecret),
			Error<string> error => BadRequest(new { error = error.Value }),
		};
	}

	private async Task<ActionResult<SignInProviderSettings>> SavedAsync(ExternalSignInProvider saved, string? clientSecret)
	{
		var id = saved.Id;
		if (!string.IsNullOrEmpty(clientSecret)
			&& await secrets.SetAsync(ExternalSignInProviders.SecretName(id), clientSecret, PortalSecretAuthor.Of(User),
				HttpContext.RequestAborted) is Error<string> refused)
		{
			return BadRequest(new { error = refused.Value });
		}

		await audit.RecordPortalAsync(User, AuditActions.SignInProviderSave, AuditTargets.Of(AuditTargetKinds.SignInProvider, id),
			$"{saved.Kind}, {(saved.Enabled ? "enabled" : "disabled")}");
		return Ok(await SettingsAsync(saved));
	}

	[HttpDelete("{id}")]
	public async Task<IActionResult> Remove(string id)
	{
		if (!await providers.RemoveAsync(id))
		{
			return NotFound();
		}

		await audit.RecordPortalAsync(User, AuditActions.SignInProviderRemove, AuditTargets.Of(AuditTargetKinds.SignInProvider, id));
		return NoContent();
	}

	private async Task<SignInProviderSettings> SettingsAsync(ExternalSignInProvider provider)
		=> new(provider.Id, provider.Kind, provider.Name, provider.ClientId, provider.Enabled,
			await providers.HasSecretAsync(provider.Id), await providers.IsReadyAsync(provider),
			externalSignIn.RedirectUriFor(provider.Id, Request) ?? string.Empty,
			ExternalSignInProviders.SecretName(provider.Id),
			ExternalSignInProviders.EndpointsOf(provider)!);
}
