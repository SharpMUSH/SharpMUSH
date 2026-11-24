namespace SharpMUSH.Client.Components.Scenes;

/// <summary>
/// The players a scene form invites, as typed into it: names separated by commas, sent to the game one
/// player at a time, so each refusal is that player's own line and one bad name stops no other.
/// </summary>
public static class SceneInvites
{
	/// <summary>The characters a player name never has that the game would read as code or as the end of a field.</summary>
	private static readonly char[] Unsafe = ['[', ']', '%', ';', '=', '{', '}', '\\', '\r', '\n'];

	/// <summary>Each name in <paramref name="typed"/> once, trimmed, in the order typed, ignoring case.</summary>
	public static IReadOnlyList<string> Names(string? typed) =>
		[.. (typed ?? string.Empty)
			.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.Select(name => string.Concat(name.Where(c => !Unsafe.Contains(c))).Trim())
			.Where(name => name.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)];

	/// <summary>Invites the player <paramref name="name"/> names, read back as whether they are on the list.</summary>
	public static (string Command, string Result, string Expected) Invite(string sceneId, string name) =>
		($"+scene/invite {sceneId}={name}", $"t(member(scene({sceneId},invited),num(*{name})))", "1");

	/// <summary>Withdraws the invitation of the player <paramref name="dbref"/>, read back as whether it is gone.</summary>
	public static (string Command, string Result, string Expected) Uninvite(string sceneId, string dbref) =>
		($"+scene/uninvite {sceneId}={dbref}", $"member(scene({sceneId},invited),{dbref})", "0");
}
