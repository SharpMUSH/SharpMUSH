using Mediator;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH <c>check_connected_rooms</c> (<c>src/destroy.c:1219-1258</c>): every room reached through exits
/// from the base room, or from a FLOATING room, is connected. The rest are disconnected, and their owners are
/// told so, unless the room is GOING, FLOATING or NO_WARN (itself or its owner), or, with
/// <c>exits_connect_rooms</c> on, has an exit of its own.
/// </summary>
public static class ConnectedRoomCheck
{
	/// <summary>Tells each disconnected room's owner about it; answers how many rooms were reported.</summary>
	public static async ValueTask<int> WarnOwnersAsync(IMediator mediator, INotifyService notifyService,
		IPermissionService permissionService, IConnectionService connectionService, DBRef baseRoom,
		bool exitsConnectRooms)
	{
		var reported = 0;
		await foreach (var room in DisconnectedRoomsAsync(mediator, baseRoom, exitsConnectRooms))
		{
			var owner = new AnySharpObject(await room.Object.Owner.WithCancellation(CancellationToken.None));
			var shown = await MessageFormatting.UnparseObjectAsync(permissionService, owner, new AnySharpObject(room),
				connectionService);
			await notifyService.Notify(owner, string.Format(ErrorMessages.Notifications.DisconnectedRoomFormat, shown));
			reported++;
		}

		return reported;
	}

	public static async IAsyncEnumerable<SharpRoom> DisconnectedRoomsAsync(IMediator mediator, DBRef baseRoom,
		bool exitsConnectRooms)
	{
		var rooms = new Dictionary<int, SharpRoom>();
		var floating = new List<int>();
		await foreach (var obj in mediator.CreateStream(new GetAllTypedObjectsQuery()))
		{
			if (obj is not SharpRoom room) continue;
			rooms[room.Object.DBRef.Number] = room;
			if (await room.Object.HasFlag("FLOATING")) floating.Add(room.Object.DBRef.Number);
		}

		var connected = new HashSet<int>();
		var pending = new Stack<int>(floating.Prepend(baseRoom.Number));
		while (pending.TryPop(out var number))
		{
			if (!rooms.TryGetValue(number, out var room) || !connected.Add(number)) continue;
			await foreach (var exit in mediator.CreateStream(new GetExitsQuery(room.Object.DBRef)))
			{
				if (await exit.Home.WithCancellation(CancellationToken.None) is AnySharpContainer destination
						&& destination.IsRoom)
				{
					pending.Push(destination.Object().DBRef.Number);
				}
			}
		}

		foreach (var (number, room) in rooms)
		{
			if (connected.Contains(number)
					|| await room.Object.IsGoingAsync()
					|| await room.Object.HasNoWarnFlagAsync()
					|| await (await room.Object.Owner.WithCancellation(CancellationToken.None)).Object.HasNoWarnFlagAsync()
					|| exitsConnectRooms && await mediator.CreateStream(new GetExitsQuery(room.Object.DBRef)).AnyAsync())
			{
				continue;
			}

			yield return room;
		}
	}
}
