using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Counts a scene for the staff activity reports when it first goes active, in its room or in none. A scene
/// is created <c>new</c> or <c>scheduled</c> and started later, by <c>@scene/set</c> or <c>sceneset()</c>
/// setting its status; resuming a paused or finished one is the same scene and does not count again.
/// </summary>
public static class SceneActivity
{
	private const string Status = "status";

	/// <summary>The scene's status before a write of <paramref name="key"/>, read only when that write sets the status.</summary>
	public static async ValueTask<string?> StatusBeforeAsync(ISceneService sceneService, string sceneId, string key)
		=> IsStatus(key) && await sceneService.GetSceneAsync(sceneId) is Contracts.Scene scene ? scene.Status : null;

	/// <summary>After a successful write of <paramref name="key"/>: counts the scene when it has just started.</summary>
	public static void AfterSet(IMUSHCodeParser parser, string key, string? statusBefore, Contracts.Scene scene)
	{
		if (!IsStatus(key)
			|| !string.Equals(scene.Status, "active", StringComparison.Ordinal)
			|| statusBefore is "active" or "paused" or "finished")
		{
			return;
		}

		parser.ServiceProvider.GetService<IActivityLedger>()?.RecordScene(
			DBRef.TryParse(scene.RoomDbref, out var room) && room is { } where ? where.Number : null);
	}

	private static bool IsStatus(string key) => string.Equals(key.Trim(), Status, StringComparison.OrdinalIgnoreCase);
}
