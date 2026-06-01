using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Issues One-Time Tokens (OTTs) for web-based MUSH authentication.
/// <para>
/// The Blazor client POSTs MUSH credentials here over HTTPS.  On successful
/// validation the endpoint returns a short-lived OTT that the client uses to
/// authenticate the WebSocket connection via <c>connect token &lt;ott&gt;</c>.
/// The plain-text password never touches the WebSocket.
/// </para>
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
	IMediator mediator,
	IPasswordService passwordService,
	IOttStore ottStore,
	ILogger<AuthController> logger) : ControllerBase
{
	/// <summary>Request body for OTT issuance.</summary>
	public record MushTokenRequest(string PlayerName, string Password);

	/// <summary>Response body containing the one-time token.</summary>
	public record MushTokenResponse(string Token, int ExpiresIn);

	/// <summary>
	/// Validate MUSH credentials and issue a one-time login token.
	/// The token is valid for 60 seconds and can only be used once.
	/// </summary>
	[HttpPost("mush-token")]
	public async Task<IActionResult> GetMushToken([FromBody] MushTokenRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.PlayerName))
			return BadRequest("PlayerName is required.");

		var player = await mediator
			.CreateStream(new GetPlayerQuery(request.PlayerName))
			.FirstOrDefaultAsync();

		if (player is null)
		{
			logger.LogInformation("OTT request: player {Name} not found", request.PlayerName);
			return Unauthorized("Invalid credentials.");
		}

		var valid = passwordService.PasswordIsValid(
			$"#{player.Object.Key}:{player.Object.CreationTime}",
			request.Password ?? string.Empty,
			player.PasswordHash);

		if (!valid && !string.IsNullOrEmpty(player.PasswordHash))
		{
			logger.LogInformation("OTT request: invalid password for player {Name}", request.PlayerName);
			return Unauthorized("Invalid credentials.");
		}

		// Rehash legacy PennMUSH passwords on successful login
		if (valid && passwordService.NeedsRehash(player.PasswordHash))
		{
			await passwordService.RehashPasswordAsync(player, request.Password ?? string.Empty);
			logger.LogInformation("Rehashed legacy password for player #{Key} via OTT login", player.Object.Key);
		}

		const int ttlSeconds = 60;
		var playerRef = new DBRef(player.Object.Key, player.Object.CreationTime);
		var token = await ottStore.CreateTokenAsync(playerRef, TimeSpan.FromSeconds(ttlSeconds));

		logger.LogInformation("Issued OTT for player {Name} (#{Key})", player.Object.Name, player.Object.Key);
		return Ok(new MushTokenResponse(token, ttlSeconds));
	}
}
