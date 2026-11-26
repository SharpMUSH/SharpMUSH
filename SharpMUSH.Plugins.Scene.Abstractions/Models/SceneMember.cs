namespace SharpMUSH.Plugins.Scene.Models;

/// <summary>
/// A player's participation in a scene — a projection of the
/// <c>edge_sharp_sys_scene_member</c> edge (player → scene). Carries the live
/// participation state: the free-string <see cref="Role"/>, the per-scene
/// <see cref="ShowAs"/> persona, and <see cref="IsCurrent"/> (the player's focus —
/// at most one current scene per player). The member is a graph edge to the real
/// player plus a <see cref="MemberName"/> snapshot.
/// </summary>
/// <param name="SceneId">The scene this membership is on.</param>
/// <param name="MemberDbref">Live member dbref resolved from the edge, or null if the player is gone.</param>
/// <param name="MemberName">Snapshot of the member's name when membership was granted.</param>
/// <param name="Role">Free-string relationship (e.g. "participant", "watcher", "attending", "owner", custom).</param>
/// <param name="ShowAs">The player's current display persona for this scene; empty string = pose as self.</param>
/// <param name="IsCurrent">True when this is the player's focused scene (poses here are eligible for capture).</param>
/// <param name="GrantedAt">UTC Unix-millis the membership was created.</param>
public record SceneMember(
	string SceneId,
	string? MemberDbref,
	string MemberName,
	string Role,
	string ShowAs,
	bool IsCurrent,
	long GrantedAt);
