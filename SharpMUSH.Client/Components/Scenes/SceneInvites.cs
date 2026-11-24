namespace SharpMUSH.Client.Components.Scenes;

/// <summary>
/// The players a scene form invites, as typed into it: names separated by commas, sent to the game one
/// player at a time, so each refusal is that player's own line and one bad name stops no other.
/// </summary>
public static class SceneInvites
{
	/// <summary>The characters the game would read as code, a group or the end of an argument rather than as part of a name.</summary>
	private const string Special = "\\[]{}()%,";

	/// <summary>Each name in <paramref name="typed"/> once, trimmed, in the order typed, ignoring case.</summary>
	public static IReadOnlyList<string> Names(string? typed) =>
		[.. (typed ?? string.Empty)
			.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.Select(name => string.Concat(name.Where(c => c is not ('\r' or '\n'))).Trim())
			.Where(name => name.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)];

	/// <summary>
	/// <paramref name="name"/> as the game reads it back unchanged: each character it would otherwise act on
	/// escaped, and a semicolon spelled <c>%;</c>, so a name is never run and never loses a character.
	/// </summary>
	public static string Literal(string name) =>
		string.Concat(name.Select(c => c == ';' ? "%;" : Special.Contains(c) ? $"\\{c}" : c.ToString()));

	/// <summary>Invites the player <paramref name="name"/> names, read back as whether they are on the list.</summary>
	public static (string Command, string Result, string Expected) Invite(string sceneId, string name) =>
		($"+scene/invite {sceneId}={Literal(name)}", $"t(member(scene({sceneId},invited),num(*{Literal(name)})))", "1");

	/// <summary>Withdraws the invitation of the player <paramref name="dbref"/>, read back as whether it is gone.</summary>
	public static (string Command, string Result, string Expected) Uninvite(string sceneId, string dbref) =>
		($"+scene/uninvite {sceneId}={dbref}", $"member(scene({sceneId},invited),{dbref})", "0");
}
