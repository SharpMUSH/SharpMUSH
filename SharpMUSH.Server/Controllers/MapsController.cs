using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The World map's reads, as the account session's character (<see cref="WorldMapReader"/>).
///
/// Routes:
///   GET api/maps          — every map the character may see
///   GET api/maps/{dbref}  — one map's rooms and exits
///   GET api/maps/route/{dbref} — the way from where the character stands to a room, across maps
///
/// An app the player allowed (<c>api/oauth</c>) may read them with its access token when it holds the <c>commands</c>
/// scope: a map shows only what the character could find by walking the rooms.
/// </summary>
[ApiController]
[Route("api/maps")]
[Authorize(Policy = DelegatedPolicies.Commands)]
public class MapsController(
	IMediator mediator,
	IPermissionService permissions,
	IRealityPolicy reality,
	IAttributeService attributes,
	IVisibleWorldProjection projection) : ControllerBase
{
	private WorldMapReader Reader => new(mediator, permissions, reality, attributes);

	[HttpGet]
	public async Task<IActionResult> List(CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } viewer) return Unauthorized();
		Response.Headers.CacheControl = "no-store";
		return Ok(await Reader.ListAsync(viewer, ct));
	}

	[HttpGet("{dbref:int}")]
	public async Task<IActionResult> Get(int dbref, CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } viewer) return Unauthorized();
		Response.Headers.CacheControl = "no-store";
		return await Reader.ReadAsync(viewer, dbref, ct) switch
		{
			MapDto map => Ok(map),
			SharpMUSH.Library.DiscriminatedUnions.NotFound => NotFound(new ApiErrorDto($"#{dbref} is not a map you may see."))
		};
	}

	[HttpGet("route/{dbref:int}")]
	public async Task<IActionResult> GetRoute(int dbref, CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } viewer) return Unauthorized();
		Response.Headers.CacheControl = "no-store";
		return await Reader.RouteAsync(viewer, dbref, ct) switch
		{
			MapRouteDto route => Ok(route),
			SharpMUSH.Library.DiscriminatedUnions.NotFound => NotFound(new ApiErrorDto($"No route you may see leads to #{dbref}."))
		};
	}
}
