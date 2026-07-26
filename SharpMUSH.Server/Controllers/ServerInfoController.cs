using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Anonymous, read-only server facts the portal needs before a visitor authenticates —
/// e.g. whether guest logins are enabled, so the client can decide whether to offer a
/// "play as guest" affordance instead of connecting only to be refused.
/// </summary>
[ApiController]
[Route("api/server-info")]
public class ServerInfoController(IOptionsWrapper<SharpMUSHOptions> options) : ControllerBase
{
	public record ServerInfoResponse(bool GuestsEnabled, string MudName);

	[HttpGet]
	[EnableRateLimiting("public-api")]
	public IActionResult Get()
		=> Ok(new ServerInfoResponse(options.CurrentValue.Net.Guests, options.CurrentValue.Net.MudName));
}
