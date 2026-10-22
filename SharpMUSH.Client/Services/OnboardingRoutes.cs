namespace SharpMUSH.Client.Services;

/// <summary>Preserves a validated guide or character-entry destination after first sign-in.</summary>
public static class OnboardingRoutes
{
	public static string LocalReturn(string? path) => path is { Length: > 0 } && path.StartsWith('/')
		&& !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\') && !path.Any(char.IsControl)
		? path : "/play";

	public static string FirstCharacter(string? path)
	{
		if (path is null || LocalReturn(path) != path) return "/characters/new";
		if (path is "/characters/new" or "/characters/claim"
			|| path.StartsWith("/characters/new?", StringComparison.Ordinal)
			|| path.StartsWith("/characters/claim?", StringComparison.Ordinal)) return path;
		return path == "/welcome" || path.StartsWith("/welcome?", StringComparison.Ordinal)
			? "/characters/new?returnUrl=" + Uri.EscapeDataString(path) : "/characters/new";
	}
}
