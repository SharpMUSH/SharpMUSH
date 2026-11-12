using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;

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
	PortalBuild build, IGuestAvailability guests, OnboardingPresentationService onboarding) : ControllerBase
{
	/// <param name="GuestsEnabled">Whether a visitor can play as a guest now (<see cref="IGuestAvailability"/>):
	/// guest logins on and a guest character to hand out, not <c>Net.Guests</c> alone.</param>
	/// <param name="Features">The <c>GameFeatures</c> ids of the optional applications the game has on.</param>
	/// <param name="BuildId">The portal build this server serves (<see cref="PortalBuild"/>).</param>
	/// <param name="ImageHosts">The <c>image_hosts</c> option, which the portal holds layout pictures to.</param>
	/// <param name="ImageHostList">The <c>image_host_list</c> option.</param>
	/// <param name="Logo">The <c>portal_logo</c> option; empty for the SharpMUSH logo.</param>
	/// <param name="Banner">The <c>portal_banner</c> option; empty for none.</param>
	/// <param name="Tagline">The <c>portal_tagline</c> option.</param>
	/// <param name="Description">The <c>portal_description</c> option.</param>
	/// <param name="SideArt">The <c>portal_side_art</c> option; empty for Sharpie.</param>
	/// <param name="Sharpie">The <c>portal_sharpie</c> option.</param>
	public record ServerInfoResponse(bool GuestsEnabled, string MudName, IReadOnlyList<string> Features, string BuildId,
		string ImageHosts, string ImageHostList, string Logo, OnboardingPresentation? Onboarding = null,
		string Banner = "", string Tagline = "", string Description = "", string SideArt = "", bool Sharpie = true);

	// Not rate-limited: every portal page load asks for this before it renders, and it reads options values,
	// the installed-package registry and the (cached) guest roster. A limiter here only ever delayed the portal's boot.
	[HttpGet]
	public async Task<IActionResult> Get(CancellationToken ct = default)
	{
		var current = options.CurrentValue;
		var cosmetic = current.Cosmetic;
		return Ok(new ServerInfoResponse(await guests.CanLogInAsync(ct), current.Net.MudName,
			await features.EnabledAsync(), build.Id,
			cosmetic.ImageHosts ?? "any", cosmetic.ImageHostList ?? string.Empty,
			cosmetic.PortalLogo?.Trim() ?? string.Empty, await onboarding.PublicAsync(ct),
			PortalIdentity.Banner(cosmetic) ?? string.Empty, PortalIdentity.Tagline(cosmetic), PortalIdentity.Description(cosmetic),
			PortalIdentity.SideArt(cosmetic) ?? string.Empty, cosmetic.PortalSharpie));
	}

	/// <summary>
	/// The portal's tab icon (<c>portal_favicon</c>, else <c>portal_logo</c>, else the SharpMUSH logo), as a
	/// redirect. <c>index.html</c> and the startup page link here, so the icon is the game's before the portal boots.
	/// </summary>
	[HttpGet("favicon")]
	public IActionResult Favicon()
	{
		Response.Headers.CacheControl = "no-cache";
		return Redirect(PortalPicture.Favicon(options.CurrentValue.Cosmetic));
	}
}
