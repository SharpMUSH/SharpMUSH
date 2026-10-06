using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Anonymous, read-only server facts the portal needs before a visitor authenticates —
/// e.g. whether guest logins are enabled, so the client can decide whether to offer a
/// "play as guest" affordance instead of connecting only to be refused, and which optional
/// applications the game has on, so it links only to pages that have something behind them; and which
/// portal build it serves, so a tab left open across a deploy can offer a reload.
/// </summary>
[ApiController]
[Route("api/server-info")]
public class ServerInfoController(IOptionsWrapper<SharpMUSHOptions> options, IGameFeatureReader features,
	PortalBuild build, IGuestAvailability guests) : ControllerBase
{
	/// <param name="GuestsEnabled">Whether a visitor can play as a guest now (<see cref="IGuestAvailability"/>):
	/// guest logins on and a guest character to hand out, not <c>Net.Guests</c> alone.</param>
	/// <param name="Features">The <c>GameFeatures</c> ids of the optional applications the game has on.</param>
	/// <param name="BuildId">The portal build this server serves (<see cref="PortalBuild"/>).</param>
	/// <param name="ImageHosts">The <c>image_hosts</c> option, which the portal holds layout pictures to.</param>
	/// <param name="ImageHostList">The <c>image_host_list</c> option.</param>
	public record ServerInfoResponse(bool GuestsEnabled, string MudName, IReadOnlyList<string> Features, string BuildId,
		string ImageHosts, string ImageHostList);

	// Not rate-limited: every portal page load asks for this before it renders, and it reads options values,
	// the installed-package registry and the (cached) guest roster. A limiter here only ever delayed the portal's boot.
	[HttpGet]
	public async Task<IActionResult> Get(CancellationToken ct = default)
		=> Ok(new ServerInfoResponse(await guests.CanLogInAsync(ct), options.CurrentValue.Net.MudName,
			await features.EnabledAsync(), build.Id,
			options.CurrentValue.Cosmetic.ImageHosts ?? "any", options.CurrentValue.Cosmetic.ImageHostList ?? string.Empty));
}
