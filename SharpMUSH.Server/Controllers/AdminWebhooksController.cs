using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Webhooks;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Webhook subscriptions, behind <c>config.admin</c>: the same as <c>@webhook</c> in the game. The secret is
/// only ever in the answer to a create, a rotation or <c>GET …/secret</c>.
///
/// Routes:
///   GET    api/admin/webhooks               — every subscription, with its delivery status
///   GET    api/admin/webhooks/{name}        — one subscription, with its recent tries
///   POST   api/admin/webhooks               — create one; answers its secret
///   PATCH  api/admin/webhooks/{name}        — change its address, events or state
///   GET    api/admin/webhooks/{name}/secret — its secret
///   POST   api/admin/webhooks/{name}/rotate — give it a new secret
///   POST   api/admin/webhooks/{name}/test   — send it a WEBHOOK`TEST event
///   DELETE api/admin/webhooks/{name}        — delete it
/// </summary>
[ApiController]
[Route("api/admin/webhooks")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public sealed class AdminWebhooksController(
	IWebhookService webhooks,
	IAuditLog audit,
	IAdministrativeCapabilityService capabilities) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<WebhookView>>> List()
		=> Ok((await webhooks.ListAsync()).Select(View).ToList());

	[HttpGet("{name}")]
	public async Task<ActionResult<WebhookView>> Get(string name)
		=> await webhooks.GetAsync(name) switch
		{
			WebhookSubscription found => Ok(View(found)),
			Library.DiscriminatedUnions.NotFound => NotFound(),
		};

	[HttpPost]
	public async Task<ActionResult<WebhookSecret>> Create([FromBody] WebhookCreateRequest request)
		=> await webhooks.CreateAsync(request.Name ?? "", request.Url, request.Events ?? [], await ReachAsync(), Creator()) switch
		{
			WebhookSubscription created => await CreatedAsync(created),
			Error<string> error => BadRequest(error.Value),
		};

	[HttpPatch("{name}")]
	public async Task<ActionResult<WebhookView>> Update(string name, [FromBody] WebhookUpdateRequest request)
		=> await ApplyAsync(name, request) switch
		{
			WebhookSubscription changed => await ChangedAsync(changed, Describe(request)),
			Library.DiscriminatedUnions.NotFound => NotFound(),
			Error<string> error => BadRequest(error.Value),
		};

	/// <summary>The request's changes, one after another, stopping at the first that fails.</summary>
	private async ValueTask<FoundResult<WebhookSubscription>> ApplyAsync(string name, WebhookUpdateRequest request)
	{
		if (await webhooks.GetAsync(name) is not WebhookSubscription current) return new Library.DiscriminatedUnions.NotFound();
		if (request.Url is not null)
		{
			var result = await webhooks.SetUrlAsync(name, request.Url);
			if (result is not WebhookSubscription) return result;
		}

		if (request.Events is not null)
		{
			var result = await webhooks.SetEventsAsync(name, request.Events, await ReachAsync());
			if (result is not WebhookSubscription) return result;
		}

		return request.Enabled is { } enabled
			? await webhooks.SetEnabledAsync(name, enabled)
			: await webhooks.GetAsync(name) switch
			{
				WebhookSubscription found => found,
				Library.DiscriminatedUnions.NotFound => current,
			};
	}

	[HttpGet("{name}/secret")]
	public async Task<ActionResult<WebhookSecret>> Secret(string name)
		=> await webhooks.GetAsync(name) switch
		{
			WebhookSubscription found => Ok(new WebhookSecret(found.Name, found.Secret)),
			Library.DiscriminatedUnions.NotFound => NotFound(),
		};

	[HttpPost("{name}/rotate")]
	public async Task<ActionResult<WebhookSecret>> Rotate(string name)
		=> await webhooks.RotateSecretAsync(name) switch
		{
			WebhookSubscription changed => await RotatedAsync(changed),
			Library.DiscriminatedUnions.NotFound => NotFound(),
			Error<string> error => BadRequest(error.Value),
		};

	[HttpPost("{name}/test")]
	public async Task<ActionResult<WebhookTestSent>> Test(string name)
		=> await webhooks.TestAsync(name, null) switch
		{
			WebhookMessage message => Ok(new WebhookTestSent(name, message.Id)),
			Library.DiscriminatedUnions.NotFound => NotFound(),
			Error<string> error => BadRequest(error.Value),
		};

	[HttpDelete("{name}")]
	public async Task<IActionResult> Delete(string name)
		=> await webhooks.DeleteAsync(name) switch
		{
			WebhookSubscription removed => await DeletedAsync(removed),
			Library.DiscriminatedUnions.NotFound => NotFound(),
		};

	private WebhookView View(WebhookSubscription subscription)
		=> new(subscription.Name, subscription.Url, subscription.Events, subscription.Reach.ToString().ToLowerInvariant(), subscription.Enabled, subscription.DisabledReason,
			subscription.CreatedBy, subscription.CreatedAt, webhooks.Status(subscription.Name));

	/// <summary>
	/// The most private events a webhook whose events this account sets may be sent, from the account's own
	/// grants as <c>@webhook/events</c> takes them from the character's.
	/// </summary>
	private async Task<EventPrivacy> ReachAsync()
	{
		if (User.FindFirstValue(ClaimTypes.NameIdentifier) is not { Length: > 0 } accountId) return EventPrivacy.Public;
		var actor = new CapabilityActor(accountId, null, null);
		return WebhookSubscription.ReachOf(
			await capabilities.AuthorizeAsync(actor, PortalPermission.GamePower("See_All"), HttpContext.RequestAborted),
			await capabilities.AuthorizeAsync(actor, PortalPermission.PlayersModerate, HttpContext.RequestAborted));
	}

	private string? Creator()
		=> User.FindFirstValue(ClaimTypes.Name) is { Length: > 0 } name ? $"account {name}" : null;

	private static AuditTarget Target(WebhookSubscription subscription)
		=> new(AuditTargetKinds.Webhook, subscription.Name, subscription.Name);

	private static string Describe(WebhookUpdateRequest request)
		=> string.Join("; ", new[]
		{
			request.Url is { } url ? $"url {WebhookAddress.Redacted(url)}" : null,
			request.Events is { } events ? $"events {string.Join(' ', events)}" : null,
			request.Enabled is { } enabled ? enabled ? "enabled" : "disabled" : null,
		}.OfType<string>());

	private async Task<ActionResult<WebhookSecret>> CreatedAsync(WebhookSubscription created)
	{
		await audit.RecordPortalAsync(User, AuditActions.WebhookCreate, Target(created), $"url {WebhookAddress.Redacted(created.Url)}");
		return CreatedAtAction(nameof(Get), new { name = created.Name }, new WebhookSecret(created.Name, created.Secret));
	}

	private async Task<ActionResult<WebhookView>> ChangedAsync(WebhookSubscription changed, string details)
	{
		await audit.RecordPortalAsync(User, AuditActions.WebhookChange, Target(changed), details);
		return Ok(View(changed));
	}

	private async Task<ActionResult<WebhookSecret>> RotatedAsync(WebhookSubscription changed)
	{
		await audit.RecordPortalAsync(User, AuditActions.WebhookChange, Target(changed), "secret rotated");
		return Ok(new WebhookSecret(changed.Name, changed.Secret));
	}

	private async Task<IActionResult> DeletedAsync(WebhookSubscription removed)
	{
		await audit.RecordPortalAsync(User, AuditActions.WebhookDelete, Target(removed));
		return NoContent();
	}
}
