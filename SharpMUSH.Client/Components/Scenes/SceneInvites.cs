namespace SharpMUSH.Client.Components.Scenes;

/// <summary>
/// The players a scene form invites, as typed into it: names separated by commas, sent to the game as
/// <c>+scene/invite &lt;id&gt;=&lt;name&gt;,&lt;name&gt;</c>.
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

	/// <summary>The players' names as the right-hand side of <c>+scene/invite</c> and <c>+scene/uninvite</c>.</summary>
	public static string Command(IEnumerable<string> names) => string.Join(",", names);
}
