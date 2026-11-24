using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The apps staff register so they may ask players for access, for <c>config.admin</c>.
///
/// Routes:
///   GET    api/oauth/clients              — every registered app
///   POST   api/oauth/clients              — registers one; a confidential one's secret is in the answer, this once
///   PUT    api/oauth/clients/{id}         — changes its name, description, homepage, addresses and scopes
///   POST   api/oauth/clients/{id}/secret  — gives it a new secret, this once; the old one stops working
///   DELETE api/oauth/clients/{id}         — removes it, every grant players gave it and every token it holds
/// </summary>
[ApiController]
[Route("api/oauth/clients")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public sealed class OAuthClientsController(IOAuthService oauth, IAuditLog audit) : ControllerBase
{
	[HttpGet]
	public async Task<IReadOnlyList<OAuthClientView>> List(CancellationToken ct)
		=> [.. (await oauth.GetClientsAsync(ct)).Select(View)];

	[HttpPost]
	public async Task<IActionResult> Register([FromBody] OAuthClientEdit edit, CancellationToken ct)
		=> await oauth.RegisterClientAsync(Draft(edit), edit.Confidential, ct) switch
		{
			RegisteredOAuthClient registered => await RegisteredAsync(registered, ct),
			Error<string> error => BadRequest(error.Value),
		};

	private async Task<IActionResult> RegisteredAsync(RegisteredOAuthClient registered, CancellationToken ct)
	{
		await RecordAsync(AuditActions.AppSave, registered.Client, "registered", ct);
		return Ok(new OAuthClientCreated(View(registered.Client), registered.Secret));
	}

	[HttpPut("{id}")]
	public async Task<IActionResult> Update(string id, [FromBody] OAuthClientEdit edit, CancellationToken ct)
		=> await oauth.UpdateClientAsync(id, Draft(edit), ct) switch
		{
			OAuthClient client => await UpdatedAsync(client, ct),
			Library.DiscriminatedUnions.NotFound => NotFound(),
			Error<string> error => BadRequest(error.Value),
		};

	private async Task<IActionResult> UpdatedAsync(OAuthClient client, CancellationToken ct)
	{
		await RecordAsync(AuditActions.AppSave, client, "changed", ct);
		return Ok(View(client));
	}

	[HttpPost("{id}/secret")]
	public async Task<IActionResult> ResetSecret(string id, CancellationToken ct)
		=> await oauth.ResetClientSecretAsync(id, ct) switch
		{
			RegisteredOAuthClient reset => await SecretResetAsync(reset, ct),
			Library.DiscriminatedUnions.NotFound => NotFound(),
		};

	private async Task<IActionResult> SecretResetAsync(RegisteredOAuthClient reset, CancellationToken ct)
	{
		await RecordAsync(AuditActions.AppSave, reset.Client, "new secret", ct);
		return Ok(new OAuthClientCreated(View(reset.Client), reset.Secret));
	}

	[HttpDelete("{id}")]
	public async Task<IActionResult> Delete(string id, CancellationToken ct)
	{
		if (await oauth.FindAppAsync(id, ct) is not OAuthApp { Hosted: false } app) return NotFound();
		if (!await oauth.DeleteClientAsync(id, ct)) return NotFound();
		await audit.RecordPortalAsync(AccountId, AuditActions.AppDelete, new AuditTarget(AuditTargetKinds.App, app.ClientId, app.Name), null, ct);
		return NoContent();
	}

	private string AccountId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

	private ValueTask RecordAsync(string action, OAuthClient client, string details, CancellationToken ct)
		=> audit.RecordPortalAsync(AccountId, action, new AuditTarget(AuditTargetKinds.App, client.Id, client.Name), details, ct);

	private static OAuthClientDraft Draft(OAuthClientEdit edit)
		=> new(edit.Name ?? string.Empty, edit.Description, edit.Homepage, edit.RedirectUris ?? [], edit.Scopes ?? []);

	public static OAuthClientView View(OAuthClient client) => new(client.Id, client.Name, client.Description, client.Homepage,
		client.RedirectUris, client.Scopes, client.SecretHash is not null, client.CreatedAt);
}
