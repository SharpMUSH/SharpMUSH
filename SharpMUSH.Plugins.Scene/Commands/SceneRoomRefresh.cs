using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Fires <c>ROOM`CONTENTS</c> with the cause <c>scene</c> for a room whose scene just changed in a way its
/// <c>room.info</c> shows: the active scene started, paused, finished, moved, was retitled or made public or
/// private, or its cast or a viewer's focus on it changed. The bundled room-contents handler answers that
/// cause by sending every connected viewer in the room a fresh <c>room.info</c>, so the portal's Play page
/// follows the scene without waiting for someone to move.
/// </summary>
/// <remarks>
/// Called by the <c>@scene</c> switches and the side-effect functions after a successful write, with the
/// parser that ran it. Best-effort, as <see cref="SceneBroadcast"/> is: the write is already committed, so
/// a failure here is logged and never reaches the caller.
/// </remarks>
public static class SceneRoomRefresh
{
	/// <summary>The <c>ROOM`CONTENTS</c> cause a scene change fires with.</summary>
	public const string Cause = "scene";

	/// <summary>The scene keys <c>room.info</c> reads (a scene shows only while active).</summary>
	private static readonly HashSet<string> RoomInfoKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		"status", "title", "public", "room",
	};

	/// <summary>Whether setting <paramref name="key"/> changes what <c>room.info</c> says.</summary>
	public static bool ChangesRoomInfo(string key) => RoomInfoKeys.Contains(key.Trim());

	/// <summary>The room <paramref name="sceneId"/> is bound to, or null for a roomless or missing scene.</summary>
	public static async ValueTask<string?> RoomOfAsync(ISceneService sceneService, string sceneId)
		=> await sceneService.GetSceneAsync(sceneId) is Contracts.Scene scene ? scene.RoomDbref : null;

	/// <summary>The scene <paramref name="playerDbref"/> is focused on, or null.</summary>
	public static async ValueTask<string?> FocusOfAsync(ISceneService sceneService, string playerDbref)
		=> await sceneService.GetCurrentSceneAsync(playerDbref) is Contracts.Scene scene ? scene.Id : null;

	/// <summary>
	/// After <c>@scene/set</c> or <c>sceneset()</c>: refreshes the scene's room, and the room it left when
	/// <paramref name="key"/> moved it. Nothing for a key <c>room.info</c> does not show.
	/// </summary>
	public static ValueTask AfterSetAsync(IMUSHCodeParser parser, string key, string? roomBefore, Contracts.Scene scene)
		=> ChangesRoomInfo(key) ? RefreshAsync(parser, roomBefore, scene.RoomDbref) : ValueTask.CompletedTask;

	/// <summary>After a member was added or removed: the cast count changed for every viewer in the scene's room.</summary>
	public static async ValueTask AfterMembershipAsync(IMUSHCodeParser parser, ISceneService sceneService, string sceneId)
		=> await RefreshAsync(parser, await RoomOfAsync(sceneService, sceneId));

	/// <summary>
	/// After a player's focus moved from <paramref name="before"/> to <paramref name="after"/>: refreshes the
	/// player's own room when its scene is one of the two, the only place that focus shows. A focus that did
	/// not move refreshes nothing, so posing from the portal (which focuses on every pose) costs no refresh.
	/// </summary>
	public static async ValueTask AfterFocusAsync(IMUSHCodeParser parser, ISceneService sceneService,
		string playerDbref, string? before, string? after)
	{
		if (string.Equals(before, after, StringComparison.Ordinal)
			|| !DBRef.TryParse(playerDbref, out var parsed) || parsed is not { } player)
			return;

		var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
		if (await mediator.Send(new GetLocationQuery(player)) is not AnySharpContainer location) return;

		var room = location.Object().DBRef.ToString();
		if (await sceneService.GetActiveSceneInRoomAsync(room) is Contracts.Scene here
				&& (here.Id == before || here.Id == after))
			await RefreshAsync(parser, room);
	}

	/// <summary>Fires the event once for each distinct room named.</summary>
	public static async ValueTask RefreshAsync(IMUSHCodeParser parser, params string?[] rooms)
	{
		if (parser.ServiceProvider.GetService<IEventService>() is not { } events) return;

		foreach (var room in rooms.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal))
		{
			try
			{
				await events.TriggerEventAsync(parser, SharpEvents.RoomContents, parser.CurrentState.Enactor, room!, Cause);
			}
			catch (Exception ex)
			{
				parser.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SceneRoomRefresh))
					.LogWarning(ex, "Room {Room}: the scene refresh was not sent", room);
			}
		}
	}
}
