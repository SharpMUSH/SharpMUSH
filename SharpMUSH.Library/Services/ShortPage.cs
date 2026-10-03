using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH's partial match of a connected player's name: <c>short_page</c> and
/// <c>visible_short_page</c> (<c>src/bsd.c:6376</c>, <c>:6416</c>). <c>page</c> resolves a recipient
/// with the first, and <c>match_player</c> (<c>src/match.c:276</c>) falls back on the second when
/// <c>*name</c> finds no exact name or alias.
/// </summary>
public static class ShortPage
{
	/// <summary>
	/// <c>short_page</c>: the connected players whose name starts with <paramref name="name"/>,
	/// case-insensitively. A whole-name match wins outright and ends the walk; two of them are
	/// <see cref="AmbiguousName"/>.
	/// </summary>
	public static async ValueTask<PageRecipient> MatchAsync(IMediator mediator, IConnectionService connections,
		string name)
	{
		if (name.Length == 0)
		{
			return new NotFound();
		}

		var count = 0;
		var match = new PageRecipient(new NotFound());
		DBRef? previous = null;

		await foreach (var connection in connections.GetAll())
		{
			// short_page walks DESC_ITER_CONN, so a socket still at the connect screen is nobody.
			if (connection.State is not IConnectionService.ConnectionState.LoggedIn || connection.Ref is null)
			{
				continue;
			}

			if (await mediator.Send(new GetObjectNodeQuery(connection.Ref.Value)) is not AnySharpObject player)
			{
				continue;
			}

			var playerName = player.Object().Name;
			if (!playerName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (playerName.Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				return player;
			}

			// short_page compares against the *previous* match rather than a set, so one player holding
			// two connections that are not adjacent in the list counts twice and reads as ambiguous.
			if (previous is null || !connection.Ref.Value.Equals(previous.Value))
			{
				previous = connection.Ref.Value;
				match = player;
				count++;
			}
		}

		return count switch
		{
			0 => new NotFound(),
			1 => match,
			_ => new AmbiguousName()
		};
	}

	/// <summary>
	/// <c>visible_short_page</c>: <see cref="MatchAsync"/>, except that a <c>DARK</c> player, or one whose
	/// every connection is hidden and who is not near <paramref name="looker"/>, is not found by someone
	/// without <c>Priv_Who</c> (wizard, royalty or <c>See_All</c>).
	/// </summary>
	public static async ValueTask<PageRecipient> VisibleMatchAsync(IMediator mediator, IConnectionService connections,
		AnySharpObject looker, string name)
	{
		var match = await MatchAsync(mediator, connections, name);
		if (match is not AnySharpObject target || await looker.IsSee_All())
		{
			return match;
		}

		if (await target.HasFlag("DARK")
				|| (await AllConnectionsHidden(connections, target.Object().DBRef)
						&& !await LocateService.Nearby(looker, target)))
		{
			return new NotFound();
		}

		return match;
	}

	/// <summary>bsd.c <c>hidden()</c>: connected, and hidden on every connection.</summary>
	private static async ValueTask<bool> AllConnectionsHidden(IConnectionService connections, DBRef player)
	{
		var any = false;
		await foreach (var connection in connections.Get(player))
		{
			if (connection.State is not IConnectionService.ConnectionState.LoggedIn) continue;
			if (!connection.IsHidden) return false;
			any = true;
		}

		return any;
	}
}
