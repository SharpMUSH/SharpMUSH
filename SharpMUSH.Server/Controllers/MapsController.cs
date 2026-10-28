using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Common;
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
///   GET api/maps/here     — where the character stands, and the map that is on
///   GET api/maps/description/{dbref} — the attribute a look shows as a room's description to the character
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
	IVisibleWorldProjection projection,
	IConnectionService connections,
	ILookService look,
	IOptionsWrapper<SharpMUSHOptions> configuration) : ControllerBase
{
	private WorldMapReader Reader => new(mediator, permissions, reality, attributes, connections);

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
			MapDto { CanEdit: true } map => Ok(map with { BuildsLeft = await BuildingHelpers.BuildsLeftAsync(mediator, configuration, viewer) }),
			MapDto map => Ok(map),
			SharpMUSH.Library.DiscriminatedUnions.NotFound => NotFound(new ApiErrorDto($"#{dbref} is not a map you may see."))
		};
	}

	[HttpGet("here")]
	public async Task<IActionResult> Here(CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } viewer) return Unauthorized();
		Response.Headers.CacheControl = "no-store";
		return Ok(await Reader.HereAsync(viewer, ct));
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

	[HttpGet("description/{dbref:int}")]
	public async Task<IActionResult> GetDescription(int dbref, CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } viewer) return Unauthorized();
		Response.Headers.CacheControl = "no-store";
		return await Reader.RoomAsync(viewer, dbref, ct) switch
		{
			AnySharpObject room => Ok(new MapDescriptionDto(await look.RoomDescriptionAttributeAsync(viewer, room))),
			SharpMUSH.Library.DiscriminatedUnions.NotFound => NotFound(new ApiErrorDto($"#{dbref} is not a room you may see."))
		};
	}
}
