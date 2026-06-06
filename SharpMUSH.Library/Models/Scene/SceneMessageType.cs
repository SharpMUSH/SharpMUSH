namespace SharpMUSH.Library.Models.Scene;

/// <summary>
/// Classifies the type of a scene message line, mirroring the
/// MUSH output conventions.
/// </summary>
public enum SceneMessageType
{
	/// <summary>Descriptive pose or emote (e.g. <c>:waves</c> → "Harry waves").</summary>
	Pose,

	/// <summary>Spoken dialogue (e.g. <c>"Hello"</c> → Harry says "Hello").</summary>
	Say,

	/// <summary>Whisper visible only to sender and target(s).</summary>
	Whisper,

	/// <summary>Out-of-character remark.</summary>
	Ooc,

	/// <summary>System notification (e.g. character arrives or departs).</summary>
	System,

	/// <summary>Raw or unclassified output from the game engine.</summary>
	Normal,
}
