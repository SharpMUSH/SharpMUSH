using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Services;

/// <summary>Where a <c>$</c>-command is looked for, in the order PennMUSH looks.</summary>
public enum CommandScope
{
	/// <summary>Step 9: the executor, its location's contents and its own contents.</summary>
	Nearby,

	/// <summary>Step 11: the contents of the zone master room of the executor's location.</summary>
	LocationZone,

	/// <summary>Step 12: the executor's location itself.</summary>
	Location,

	/// <summary>Step 13: the contents of the executor's personal zone master room.</summary>
	PersonalZone,

	/// <summary>Steps 14-15: the master room and its contents.</summary>
	MasterRoom
}

/// <summary>
/// The objects whose <c>$</c>-commands a line can reach, scope by scope. Command dispatch walks them to
/// run a line; the command palette walks them to offer what a line could run.
/// </summary>
public static class CommandScopes
{
	/// <summary>
	/// The objects <paramref name="scope"/> searches for <paramref name="executor"/>'s <c>$</c>-commands,
	/// or <see cref="NotFound"/> when the scope does not apply: no location for something that is not
	/// content, no zone master room, or no master room.
	/// </summary>
	public static async ValueTask<Found<IAsyncEnumerable<AnySharpObject>>> CandidatesIn(IMediator mediator,
		CommandScope scope, AnySharpObject executor, IOptionsWrapper<SharpMUSHOptions> configuration)
	{
		switch (scope)
		{
			case CommandScope.Nearby:
				return Found(NearbyObjects.ForAsync(mediator, executor));

			case CommandScope.LocationZone when executor.AsOptionalContent is AnySharpContent zoned:
				// Step 10: Zone Exit Name and Aliases - handled in LocateService
				return await ZoneRoomContentsAsync(mediator, (await zoned.Location()).WithExitOption());

			case CommandScope.Location when executor.AsOptionalContent is AnySharpContent located:
				{
					AnySharpObject[] item = [(await located.Location()).WithExitOption()];
					return Found(item.ToAsyncEnumerable());
				}

			case CommandScope.PersonalZone:
				return await ZoneRoomContentsAsync(mediator, executor);

			case CommandScope.MasterRoom:
				{
					var goConfig = configuration.CurrentValue.Database.MasterRoom;
					// A master room that does not exist has no global commands to offer.
					if (await mediator.Send(new GetObjectNodeQuery(new DBRef(Convert.ToInt32(goConfig)))) is AnySharpObject { AsOptionalContainer: AnySharpContainer masterRoom } globalObject)
					{
						AnySharpObject[] globalObjects = [globalObject];
						var globalObjectContent = masterRoom
							.Content(mediator)
							.Select(x => x.WithRoomOption());

						return Found(globalObjects.ToAsyncEnumerable().Union(globalObjectContent));
					}

					return new NotFound();
				}

			default:
				return new NotFound();
		}

		static Found<IAsyncEnumerable<AnySharpObject>> Found(IAsyncEnumerable<AnySharpObject> candidates) => candidates;
	}

	/// <summary>
	/// The contents of each zone master room <paramref name="zoned"/> is zoned to, its first zone's first, or
	/// <see cref="NotFound"/> when none of its zones is a room.
	/// </summary>
	private static async ValueTask<Found<IAsyncEnumerable<AnySharpObject>>> ZoneRoomContentsAsync(IMediator mediator,
		AnySharpObject zoned)
	{
		var rooms = new List<AnySharpContainer>();
		foreach (var zone in await mediator.ZonesOfAsync(zoned))
		{
			if (zone is SharpRoom room) rooms.Add(room);
		}

		if (rooms.Count == 0) return new NotFound();

		return Found(rooms.ToAsyncEnumerable()
			.SelectMany(room => room.Content(mediator).Select(x => x.WithRoomOption())));

		static Found<IAsyncEnumerable<AnySharpObject>> Found(IAsyncEnumerable<AnySharpObject> candidates) => candidates;
	}
}
