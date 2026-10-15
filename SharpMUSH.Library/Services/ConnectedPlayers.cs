using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
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

		await foreach (var _ in CountedAsync(connections, mediator, countAll))
		{
			count++;
		}

		return count;
	}

	/// <summary>
	/// The players <see cref="CountAsync"/> counts, each once however many connections it has: who the heartbeat's
	/// day and week counts are taken from.
	/// </summary>
	public static async ValueTask<IReadOnlySet<int>> DistinctAsync(IConnectionService connections, IMediator mediator, bool countAll)
	{
		var players = new HashSet<int>();

		await foreach (var reference in CountedAsync(connections, mediator, countAll))
		{
			players.Add(reference.Number);
		}

		return players;
	}

	/// <summary>One entry per connection that counts.</summary>
	private static async IAsyncEnumerable<DBRef> CountedAsync(IConnectionService connections, IMediator mediator, bool countAll)
	{
		await foreach (var connection in connections.GetAll())
		{
			if (connection.Ref is not { } reference) continue;

			// GoodObject first, and unconditionally: a handle can outlive the object it is bound to, and
			// such a descriptor is not a connected player under any counting rule.
			if (await mediator.Send(new GetObjectNodeQuery(reference)) is not AnySharpObject found) continue;

			if (!countAll && await found.IsDark()) continue;

			yield return reference;
		}
	}
}
