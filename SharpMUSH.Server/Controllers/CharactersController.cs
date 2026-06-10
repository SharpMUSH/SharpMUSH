using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Character directory API. Lists all player characters for the public directory at
/// <c>/characters</c>. Profile field data (and its visibility) is served separately by
/// <see cref="ProfileController"/> via the in-game http_handler.
///
/// Routes:
///   GET /api/characters — every character (name, dbref, creation time), name-sorted.
/// </summary>
[ApiController]
[Route("api/characters")]
public class CharactersController(IMediator mediator) : ControllerBase
{
	/// <summary>A directory row. <see cref="Slug"/> is the profile route segment (the character name).</summary>
	public record CharacterSummaryDto(int Dbref, string Name, string Slug, DateTimeOffset CreatedAt);

	[HttpGet]
	[AllowAnonymous]
	public async Task<IReadOnlyList<CharacterSummaryDto>> List(CancellationToken ct)
	{
		var rows = new List<CharacterSummaryDto>();

		await foreach (var player in mediator.CreateStream(new GetAllPlayersQuery()).WithCancellation(ct))
		{
			var obj = player.Object;
			rows.Add(new CharacterSummaryDto(
				Dbref: obj.Key,
				Name: obj.Name,
				Slug: obj.Name,
				CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime)));
		}

		return rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}
}
