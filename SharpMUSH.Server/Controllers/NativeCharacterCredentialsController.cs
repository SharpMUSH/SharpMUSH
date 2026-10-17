using SharpMUSH.Server.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/account/characters")]
[EnableRateLimiting(PublicApiRateLimit.PolicyName)]
public sealed class NativeCharacterCredentialsController(
	BearerAccountResolver bearer,
	NativeLoginService native,
	INativeLoginSecretStore secrets,
	AccountCredentialGate gate,
	IConnectionService? connections = null) : ControllerBase
{
	public sealed record MintRequest(long CharacterCreationTime, string? ProofGrant);
	public sealed record PasswordRequest(long CharacterCreationTime, string NewPassword, string Confirmation, string? ProofGrant, bool DisconnectOtherClients = false);

	private async Task<IActionResult> AsAccountAsync(Func<string, string, Task<IActionResult>> action, bool mutation = true)
	{
		Response.Headers.CacheControl = "no-store";
		Response.Headers["Referrer-Policy"] = "no-referrer";
		if (mutation && !SameOrigin()) return StatusCode(StatusCodes.Status403Forbidden);
		var header = Request.Headers.Authorization.ToString();
		if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || header.Length <= "Bearer ".Length)
			return Unauthorized();
		return await bearer.ResolveAsync(this) switch
		{
			SignedInAccount account => await action(account.Id, header["Bearer ".Length..].Trim()),
			ActionResult refused => refused
		};
	}

	private bool SameOrigin()
	{
		if (Request.Headers["Sec-Fetch-Site"].ToString().Equals("cross-site", StringComparison.OrdinalIgnoreCase)) return false;
		var origin = Request.Headers.Origin.ToString();
		return origin.Length == 0 || Uri.TryCreate(origin, UriKind.Absolute, out var uri)
			&& uri.UserInfo.Length == 0 && uri.Scheme.Equals(Request.Scheme, StringComparison.OrdinalIgnoreCase)
			&& uri.Authority.Equals(Request.Host.Value, StringComparison.OrdinalIgnoreCase);
	}

	[HttpPost("{key:int}/native-login-secrets")]
	public Task<IActionResult> Mint(int key, [FromBody] MintRequest request, CancellationToken cancellationToken)
		=> AsAccountAsync(async (accountId, session) => await native.MintAsync(accountId, session,
			new DBRef(key, request.CharacterCreationTime), HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			request.ProofGrant, cancellationToken) switch
		{
			NativeLoginSecretMint mint => Ok(new NativeLoginSecretResponse(mint.Id, mint.Secret, mint.Character.Number,
				mint.Character.CreationMilliseconds.GetValueOrDefault(), mint.ExpiresAt)),
			Error<string> error => BadRequest(error.Value)
		});

	[HttpGet("/api/account/native-login-secrets")]
	public Task<IActionResult> List()
		=> AsAccountAsync((accountId, _) => Task.FromResult<IActionResult>(Ok(secrets.List(accountId)
			.Select(status => new NativeLoginStatusResponse(status.Id, status.Character.Number,
				status.Character.CreationMilliseconds.GetValueOrDefault(), status.ExpiresAt)))), mutation: false);

	[HttpDelete("/api/account/native-login-secrets/{id}")]
	public Task<IActionResult> Revoke(string id)
		=> AsAccountAsync((accountId, _) => gate.RunAsync(() => Task.FromResult<IActionResult>(
			secrets.Revoke(accountId, id) ? NoContent() : NotFound())));

	[HttpPut("{key:int}/password")]
	public Task<IActionResult> SetPassword(int key, [FromBody] PasswordRequest request, CancellationToken cancellationToken)
		=> AsAccountAsync(async (accountId, session) =>
		{
			var character = new DBRef(key, request.CharacterCreationTime);
			if (await native.SetCharacterPasswordAsync(accountId, session, character, request.NewPassword, request.Confirmation,
				request.ProofGrant, cancellationToken) is Error<string> error) return BadRequest(error.Value);
			// Closing transports can publish network notifications, so it follows the committed credential write outside its gate.
			if (request.DisconnectOtherClients && connections is { } active)
			{
				await foreach (var connection in active.Get(character))
					if (connection.Ref == character && ReferenceEquals(connection.Metadata, active.Get(connection.Handle)?.Metadata))
						await active.Disconnect(connection.Handle, connection.Metadata.GetValueOrDefault("SessionId"));
			}
			return NoContent();
		});
}
