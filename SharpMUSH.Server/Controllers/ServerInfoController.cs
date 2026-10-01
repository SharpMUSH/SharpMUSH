using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Anonymous, read-only server facts the portal needs before a visitor authenticates —
/// e.g. whether guest logins are enabled, so the client can decide whether to offer a
/// "play as guest" affordance instead of connecting only to be refused, and which optional
/// applications the game has on, so it links only to pages that have something behind them.
/// </summary>
[ApiController]
[Route("api/server-info")]
public class ServerInfoController(IOptionsWrapper<SharpMUSHOptions> options, IGameFeatureReader features) : ControllerBase
{
	/// <param name="Features">The <c>GameFeatures</c> ids of the optional applications the game has on.</param>
	public record ServerInfoResponse(bool GuestsEnabled, string MudName, IReadOnlyList<string> Features);

	// Not rate-limited: every portal page load asks for this before it renders, and it reads two
	// options values and the installed-package registry. A limiter here only ever delayed the portal's boot.
	[HttpGet]
	public async Task<IActionResult> Get()
		=> Ok(new ServerInfoResponse(options.CurrentValue.Net.Guests, options.CurrentValue.Net.MudName,
			await features.EnabledAsync()));
}
