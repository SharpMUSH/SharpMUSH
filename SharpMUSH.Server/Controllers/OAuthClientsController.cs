using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

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
///   POST   api/oauth/clients/review       — allows, blocks or forgets an app that described itself at an https client id
///   GET    api/oauth/policy               — how apps that describe themselves are treated, and the blocked domains
///   PUT    api/oauth/policy               — changes both
/// </summary>
[ApiController]
[Route("api/oauth/clients")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public sealed class OAuthClientsController(IOAuthService oauth, IOAuthAppReview review, IAuditLog audit,
	IOptionsWrapper<SharpMUSHOptions> options, IConfigOptionWriter writer) : ControllerBase
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

	[HttpPost("review")]
	public async Task<IActionResult> Review([FromBody] OAuthClientReview decision, CancellationToken ct)
	{
		if (decision is null || !ClientIdMetadata.IsClientIdUrl(decision.ClientId)) return BadRequest("Only an app that describes itself at an https address can be reviewed.");
		if (decision.Status == OAuthClientStatuses.Forget) return await ForgetAsync(decision.ClientId, ct);
		OAuthClientStatus? status = decision.Status switch
		{
			OAuthClientStatuses.Allowed => OAuthClientStatus.Allowed,
			OAuthClientStatuses.Unreviewed => OAuthClientStatus.Unreviewed,
			OAuthClientStatuses.Blocked => OAuthClientStatus.Blocked,
			_ => null
		};
		if (status is not { } chosen) return BadRequest("The status is allowed, unreviewed, blocked or forget.");
		return await review.SetClientStatusAsync(decision.ClientId, chosen, ct) switch
		{
			OAuthClient client => await ReviewedAsync(client, decision.Status, ct),
			Library.DiscriminatedUnions.NotFound => NotFound(),
		};
	}

	private async Task<IActionResult> ReviewedAsync(OAuthClient client, string status, CancellationToken ct)
	{
		await RecordAsync(AuditActions.AppSave, client, status, ct);
		return Ok(View(client));
	}

	private async Task<IActionResult> ForgetAsync(string clientId, CancellationToken ct)
	{
		if (await oauth.FindAppAsync(clientId, ct) is not OAuthApp app || !await oauth.DeleteClientAsync(clientId, ct)) return NotFound();
		await audit.RecordPortalAsync(AccountId, AuditActions.AppDelete, new AuditTarget(AuditTargetKinds.App, app.ClientId, app.Name), "forgotten", ct);
		return NoContent();
	}

	[HttpGet("~/api/oauth/policy")]
	public OAuthAppPolicyView Policy() => PolicyView(options.CurrentValue.OAuth);

	[HttpPut("~/api/oauth/policy")]
	public async Task<IActionResult> SetPolicy([FromBody] OAuthAppPolicyView request, CancellationToken ct)
	{
		if (request is null || request.Policy is not (OAuthOptions.Registered or OAuthOptions.Review or OAuthOptions.Open))
			return BadRequest("The policy is registered, review or open.");
		var domains = (request.BlockedDomains ?? []).Select(ClientIdMetadata.DomainOf).Where(domain => domain.Length > 0)
			.Distinct(StringComparer.Ordinal).ToArray();
		if (domains.Length > MaxBlockedDomains) return BadRequest($"At most {MaxBlockedDomains} domains can be blocked.");
		if (domains.FirstOrDefault(domain => !IsDomain(domain)) is { } bad) return BadRequest($"'{bad}' is not a domain name.");
		var saved = await writer.UpdateAsync(current => current with
		{
			OAuth = (current.OAuth ?? new()) with { AppPolicy = request.Policy, BlockedDomains = domains }
		}, ct);
		var view = PolicyView(saved.OAuth);
		await audit.RecordPortalAsync(AccountId, AuditActions.ConfigSet, new AuditTarget(AuditTargetKinds.Setting, "oauth", "oauth"),
			$"{view.Policy}; blocked: {string.Join(", ", view.BlockedDomains)}", ct);
		return Ok(view);
	}

	/// <summary>The most domains the block list holds.</summary>
	public const int MaxBlockedDomains = 200;

	private static bool IsDomain(string domain)
		=> domain.Length <= 253 && Uri.CheckHostName(domain) == UriHostNameType.Dns;

	private static OAuthAppPolicyView PolicyView(OAuthOptions? oauth)
		=> new(OAuthOptions.PolicyOf(oauth), [.. oauth?.BlockedDomains ?? []]);

	private string AccountId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

	private ValueTask RecordAsync(string action, OAuthClient client, string details, CancellationToken ct)
		=> audit.RecordPortalAsync(AccountId, action, new AuditTarget(AuditTargetKinds.App, client.Id, client.Name), details, ct);

	private static OAuthClientDraft Draft(OAuthClientEdit edit)
		=> new(edit.Name ?? string.Empty, edit.Description, edit.Homepage, edit.RedirectUris ?? [], edit.Scopes ?? []);

	public static OAuthClientView View(OAuthClient client) => new(client.Id, client.Name, client.Description, client.Homepage,
		client.RedirectUris, client.Scopes, client.SecretHash is not null, client.CreatedAt)
	{
		Status = client.Status switch
		{
			OAuthClientStatus.Unreviewed => OAuthClientStatuses.Unreviewed,
			OAuthClientStatus.Blocked => OAuthClientStatuses.Blocked,
			_ => OAuthClientStatuses.Allowed
		},
		DescribedAt = ClientIdMetadata.IsClientIdUrl(client.Id) ? ClientIdMetadata.HostOf(client.Id) : null,
		MetadataFetchedAt = client.MetadataFetchedAt
	};
}
