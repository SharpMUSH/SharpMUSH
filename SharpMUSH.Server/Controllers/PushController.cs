using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Push;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Web push for the signed-in account: the key a browser subscribes with, the account's devices, and a test.
/// What notifies is decided in softcode with <c>@push</c>; this only manages who receives.
/// </summary>
[ApiController]
[Route("api/push")]
public class PushController(IWebPushService push, IPushSubscriptionStore subscriptions, BearerAccountResolver bearer)
	: ControllerBase
{
	private async Task<IActionResult> AsAccountAsync(Func<string, Task<IActionResult>> action)
		=> await bearer.ResolveAsync(this) switch
		{
			SignedInAccount account => await action(account.Id),
			ActionResult refused => refused
		};

	/// <summary>The public key to subscribe with.</summary>
	[HttpGet("key")]
	public Task<IActionResult> Key(CancellationToken ct)
		=> AsAccountAsync(async _ => Ok(new PushKeyResponse(await push.PublicKeyAsync(ct))));

	/// <summary>The account's devices, oldest first.</summary>
	[HttpGet("subscriptions")]
	public Task<IActionResult> List(CancellationToken ct)
		=> AsAccountAsync(async accountId =>
		{
			var keyId = PushEndpoints.KeyIdOf(VapidPublicKey(await push.PublicKeyAsync(ct)));
			return Ok((await subscriptions.GetPushSubscriptionsAsync(accountId, ct))
				.Select(subscription => Describe(subscription, keyId))
				.ToList());
		});

	/// <summary>Turns notifications on for the calling browser.</summary>
	[HttpPost("subscriptions")]
	public Task<IActionResult> Subscribe([FromBody] PushSubscribeRequest request, CancellationToken ct)
		=> AsAccountAsync(async accountId =>
			await push.SubscribeAsync(accountId, request.Endpoint, request.Keys.P256dh, request.Keys.Auth,
				Request.Headers.UserAgent.ToString(), ct) switch
			{
				PushSubscription subscription => Ok(Describe(subscription, subscription.KeyId)),
				Error<string> error => BadRequest(error.Value)
			});

	/// <summary>Turns notifications off for one of the account's devices.</summary>
	[HttpDelete("subscriptions/{id}")]
	public Task<IActionResult> Remove(string id, CancellationToken ct)
		=> AsAccountAsync(async accountId =>
			await subscriptions.RemovePushSubscriptionAsync(id, accountId, ct) ? NoContent() : NotFound());

	/// <summary>Sends a test notification to one of the account's devices, as the session's character.</summary>
	[HttpPost("subscriptions/{id}/test")]
	public Task<IActionResult> Test(string id, CancellationToken ct)
		=> AsAccountAsync(async accountId =>
			await push.SendTestAsync(accountId, id, AccountSessionAuthenticationHandler.ActingCharacter(User), ct) switch
			{
				Success => NoContent(),
				Library.DiscriminatedUnions.NotFound => NotFound(),
				Error<string> error => StatusCode(StatusCodes.Status502BadGateway, $"The push service did not take it: {error.Value}.")
			});

	/// <summary>
	/// The service worker moves a subscription the browser replaced to its new endpoint. No session: the worker has
	/// none, and the old endpoint is the proof.
	/// </summary>
	[HttpPost("renew")]
	[AllowAnonymous]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> Renew([FromBody] PushRenewRequest request, CancellationToken ct)
		=> await push.RenewAsync(request.OldEndpoint, request.Endpoint, request.Keys.P256dh, request.Keys.Auth, ct) switch
		{
			PushSubscription => NoContent(),
			Error<string> => NotFound()
		};

	private static byte[] VapidPublicKey(string text) => System.Buffers.Text.Base64Url.DecodeFromChars(text);

	private static PushDeviceResponse Describe(PushSubscription subscription, string currentKeyId) => new(
		subscription.Id, subscription.Device, subscription.Endpoint, subscription.CreatedAt, subscription.LastSuccessAt,
		subscription.LastFailureAt, subscription.LastFailure, subscription.KeyId == currentKeyId);
}
