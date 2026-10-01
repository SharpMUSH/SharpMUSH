using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Client.Components.Scenes;

/// <summary>What a scene is called, and its picture, wherever the Scenes section lists one.</summary>
public static class SceneText
{
	/// <summary>The scene's softcode title, or "Scene in {room}" when it has none.</summary>
	public static string Title(this IStringLocalizer<SharedResource> loc, SceneSummary scene) =>
		scene.Meta.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title)
			? title
			: loc["RolSceneIn", scene.RoomName];

	/// <summary>The scene's image from its softcode meta; scenes carry none yet, so usually null.</summary>
	public static string? Image(SceneSummary scene) =>
		scene.Meta.TryGetValue("image", out var image) && !string.IsNullOrWhiteSpace(image) ? image : null;
}
