using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Layout;

/// <summary>
/// The shell's routing decisions, separated from <see cref="MainLayout"/> so they can be checked
/// without rendering it: which route is active, where a visitor must be sent before anything else,
/// and how the "open in new tab" character hint is read.
/// </summary>
public static class ShellRouting
{
	public const string SetupPath = "/setup";
	public const string AccountPath = "/account";

	/// <summary>The path of <paramref name="uri"/> without a trailing slash; the site root is <c>/</c>.</summary>
	public static string Path(string uri)
	{
		var path = new Uri(uri).AbsolutePath.TrimEnd('/');
		return string.IsNullOrEmpty(path) ? "/" : path;
	}

	/// <summary>Whether a nav link to <paramref name="href"/> is the current route, or an ancestor of it.</summary>
	/// <param name="exact">The root link: active on the root alone.</param>
	public static bool IsNavActive(string uri, string href, bool exact)
	{
		var path = Path(uri);
		if (exact) return path == "/";
		return path.Equals(href, StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith(href + "/", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>The config editor, which takes the whole content area.</summary>
	public static bool IsConfigRoute(string uri) => uri.Contains("/admin/config");

	public static bool IsPlayRoute(string uri) =>
		new Uri(uri).AbsolutePath.TrimEnd('/').Equals("/play", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether to ask the server if the game still awaits its first-run setup.
	/// </summary>
	/// <param name="needsSetup">The last answer: <c>null</c> before one arrives (or after it failed).</param>
	/// <param name="isDebugAuth">A development session signed in as the debug admin, which setup does not apply to.</param>
	/// <remarks>
	/// Only a "no" is final: once the game is claimed it stays claimed, so later navigations stop
	/// asking, while an unanswered or failed check is asked again rather than latched.
	/// </remarks>
	public static bool ShouldCheckSetup(bool? needsSetup, bool isDebugAuth) => needsSetup != false && !isDebugAuth;

	/// <summary>Where a visitor at <paramref name="currentPath"/> has to go before anything else, if anywhere.</summary>
	/// <param name="setupPending">The game is unclaimed (and the visitor is not the debug admin): every route funnels to setup.</param>
	/// <param name="mustChangePassword">A signed-in account that has to change its password first.</param>
	/// <returns>The path to send them to, or <see cref="NotFound"/> to leave them where they are.</returns>
	public static Found<string> Redirect(string currentPath, bool setupPending, bool mustChangePassword)
	{
		if (setupPending)
		{
			return IsAt(currentPath, SetupPath) ? new NotFound() : SetupPath;
		}

		if (mustChangePassword && !IsAt(currentPath, AccountPath))
		{
			return AccountPath;
		}

		return new NotFound();
	}

	/// <summary>
	/// Reads a <c>?as=&lt;dbref&gt;-&lt;creationTime&gt;</c> hint, which names one character exactly
	/// (a dbref alone could be a recycled object).
	/// </summary>
	public static Found<CharacterHint> ParseCharacterHint(string? hint)
	{
		if (string.IsNullOrEmpty(hint)) return new NotFound();

		var parts = hint.Split('-');
		return parts.Length == 2
			&& int.TryParse(parts[0], out var dbref)
			&& long.TryParse(parts[1], out var creationTime)
				? new CharacterHint(dbref, creationTime)
				: new NotFound();
	}

	private static bool IsAt(string currentPath, string path) =>
		string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A character named by its dbref and creation time.</summary>
public readonly record struct CharacterHint(int Dbref, long CreationTime);
