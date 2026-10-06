using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH <c>count_players()</c> (<c>src/bsd.c</c>): connected descriptors that have a player behind
/// them, skipping hidden (DARK) ones unless <c>count_all</c> is set. <c>INFO</c> and MSSP's
/// <c>PLAYERS</c> both report it.
/// </summary>
public static class ConnectedPlayers
{
	public static async ValueTask<int> CountAsync(IConnectionService connections, IMediator mediator, bool countAll)
	{
		var count = 0;

		await foreach (var connection in connections.GetAll())
		{
			if (connection.Ref is not { } reference) continue;

			// GoodObject first, and unconditionally: a handle can outlive the object it is bound to, and
			// such a descriptor is not a connected player under any counting rule.
			if (await mediator.Send(new GetObjectNodeQuery(reference)) is not AnySharpObject found) continue;

			if (!countAll && await found.IsDark()) continue;

			count++;
		}

		return count;
	}
}
