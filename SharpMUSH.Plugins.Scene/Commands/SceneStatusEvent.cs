using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Fires <c>SCENE`STATUS</c> when a scene is made or its status changes, so a package can act when a scene starts,
/// pauses or finishes without editing the scene package's commands: the timeline package dates a scene in the IC
/// calendar when it starts. Raised by the <c>@scene</c> switches and the side-effect functions after a successful
/// write, with the parser that ran it, so <c>%#</c> is whoever caused the change.
/// </summary>
/// <remarks>Best-effort, as <see cref="SceneRoomRefresh"/> is: the write is committed, so a failure is logged and never reaches the caller.</remarks>
public static class SceneStatusEvent
{
	/// <summary>Whether setting <paramref name="key"/> can change a scene's status.</summary>
	public static bool IsStatus(string key) => string.Equals(key.Trim(), "status", StringComparison.OrdinalIgnoreCase);

	/// <summary>The status of <paramref name="sceneId"/> before a write, empty for a missing scene.</summary>
	public static async ValueTask<string> StatusOfAsync(ISceneService sceneService, string sceneId) =>
		await sceneService.GetSceneAsync(sceneId) is Contracts.Scene scene ? scene.Status : string.Empty;

	/// <summary>A scene was made: its status is its first, and there was none before it.</summary>
	public static ValueTask CreatedAsync(IMUSHCodeParser parser, Contracts.Scene scene) =>
		RaiseAsync(parser, scene, string.Empty);

	/// <summary>Fires the event when <paramref name="key"/> was the status and it now differs from <paramref name="previous"/>.</summary>
	public static ValueTask AfterSetAsync(IMUSHCodeParser parser, string key, string previous, Contracts.Scene scene) =>
		IsStatus(key) && !string.Equals(previous, scene.Status, StringComparison.Ordinal)
			? RaiseAsync(parser, scene, previous)
			: ValueTask.CompletedTask;

	private static async ValueTask RaiseAsync(IMUSHCodeParser parser, Contracts.Scene scene, string previous)
	{
		if (parser.ServiceProvider.GetService<IEventService>() is not { } events) return;

		try
		{
			await events.TriggerEventAsync(SharpEvents.SceneStatus, parser.CurrentState.Enactor, scene.Id, scene.Status, previous);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			parser.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SceneStatusEvent))
				.LogWarning(ex, "Scene {Scene}: SCENE`STATUS was not raised", scene.Id);
		}
	}
}
