using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
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
///   GET api/maps/{dbref}/cells?level=&amp;left=&amp;bottom=&amp;right=&amp;top= — a window of a stored map's places (<see cref="IWorldMapSource"/>)
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
	IOptionsWrapper<SharpMUSHOptions> configuration,
	ICommandPermission commands,
	IEnumerable<IWorldMapSource> sources) : ControllerBase
{
	private WorldMapReader Reader => new(mediator, permissions, reality, attributes, connections, sources);

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

	[HttpGet("{dbref:int}/cells")]
	public async Task<IActionResult> GetCells(int dbref, [FromQuery] int level, [FromQuery] int left, [FromQuery] int bottom,
		[FromQuery] int right, [FromQuery] int top, CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } viewer) return Unauthorized();
		var window = new MapWindow(level, left, bottom, right, top);
		if (right < left || top < bottom || window.Cells > MapWindow.MaxCells)
			return BadRequest(new ApiErrorDto($"A window is at most {MapWindow.MaxCells} cells, with right at least left and top at least bottom."));
		Response.Headers.CacheControl = "no-store";
		return await Reader.CellsAsync(viewer, dbref, window, ct) switch
		{
			MapCellsDto cells => Ok(cells),
			SharpMUSH.Library.DiscriminatedUnions.NotFound => NotFound(new ApiErrorDto($"#{dbref} is not a map of places you may see."))
		};
	}

	[HttpGet("here")]
	public async Task<IActionResult> Here(CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } viewer) return Unauthorized();
		Response.Headers.CacheControl = "no-store";
		return Ok(await Reader.HereAsync(viewer, ct) with
		{
			CanBuild = !await viewer.IsGuest() && await commands.CanRunAsync(viewer, "@CREATE"),
		});
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
