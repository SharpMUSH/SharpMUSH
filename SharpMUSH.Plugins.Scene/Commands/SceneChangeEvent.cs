using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Fires <c>SCENE`CHANGE</c> when <c>@scene/set</c> or <c>sceneset()</c> changes a scene's key other than its status,
/// which has <see cref="SceneStatusEvent"/>: a scene moved to another time, renamed or made private. The discord-scenes
/// package keeps a scene's Discord event in step this way.
/// </summary>
/// <remarks>Best-effort, as <see cref="SceneStatusEvent"/> is: the write is committed, so a failure is logged and never reaches the caller.</remarks>
public static class SceneChangeEvent
{
	/// <summary>
	/// What <paramref name="key"/> of <paramref name="sceneId"/> holds before a write, or null when the write is to the
	/// status, or the scene is missing: no event follows either.
	/// </summary>
	public static async ValueTask<string?> ValueBeforeAsync(ISceneService sceneService, string sceneId, string key) =>
		!SceneStatusEvent.IsStatus(key) && await sceneService.GetSceneAsync(sceneId) is Contracts.Scene scene
			? ValueOf(scene, key)
			: null;

	/// <summary>Fires the event when <paramref name="key"/> now holds something other than <paramref name="previous"/>.</summary>
	public static ValueTask AfterSetAsync(IMUSHCodeParser parser, string key, string? previous, Contracts.Scene scene) =>
		previous is not null && !string.Equals(previous, ValueOf(scene, key), StringComparison.Ordinal)
			? RaiseAsync(parser, scene, key.Trim().ToLowerInvariant(), previous)
			: ValueTask.CompletedTask;

	/// <summary>A key's stored value: a time in milliseconds, a flag as 1 or 0, an object as its dbref, a detail as text.</summary>
	private static string ValueOf(Contracts.Scene scene, string key) => key.Trim().ToLowerInvariant() switch
	{
		"public" => scene.IsPublic ? "1" : "0",
		"istemp" => scene.IsTempRoom ? "1" : "0",
		"scheduledfor" => scene.ScheduledFor?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
		"room" => scene.RoomDbref ?? string.Empty,
		"owner" => scene.OwnerDbref ?? string.Empty,
		var meta => scene.Meta.TryGetValue(meta, out var value) ? value : string.Empty,
	};

	private static async ValueTask RaiseAsync(IMUSHCodeParser parser, Contracts.Scene scene, string key, string previous)
	{
		if (parser.ServiceProvider.GetService<IEventService>() is not { } events) return;

		try
		{
			await events.TriggerEventAsync(SharpEvents.SceneChange, parser.CurrentState.Enactor, scene.Id, key, previous);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			parser.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SceneChangeEvent))
				.LogWarning(ex, "Scene {Scene}: SCENE`CHANGE was not raised for {Key}", scene.Id, key);
		}
	}
}
