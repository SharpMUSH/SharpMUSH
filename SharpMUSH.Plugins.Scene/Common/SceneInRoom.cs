using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Plugins.Scene.Storage;

namespace SharpMUSH.Plugins.Scene.Common;

/// <summary>
/// Which of a room's active scenes a player in it is in. A room can run several scenes at once; a player's
/// poses there belong to the one they are focused on, and with no focus among them, to the room's most recent.
/// </summary>
public static class SceneInRoom
{
	/// <summary>
	/// The active scene in <paramref name="roomDbref"/> that <paramref name="playerDbref"/> is focused on, else
	/// the room's most recently active one, else <c>NotFound</c>.
	/// </summary>
	public static async Task<Found<Contracts.Scene>> ForPlayerAsync(ISceneService service, string roomDbref, string playerDbref)
	{
		var here = await service.ListActiveScenesInRoomAsync(roomDbref);
		if (here.Count == 0) return new NotFound();

		return await service.GetCurrentSceneAsync(playerDbref) is Contracts.Scene focused
			&& here.FirstOrDefault(s => s.Id == focused.Id) is { } mine
				? mine
				: here[0];
	}

}
