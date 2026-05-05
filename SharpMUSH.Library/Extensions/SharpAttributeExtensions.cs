using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

public static class SharpAttributeExtensions
{
	public static bool IsInternal(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "internal");

	public static bool IsWizard(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "wizard");

	public static bool IsLocked(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "locked");

	public static bool IsNoprog(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "noprog");

	public static bool IsMortalDark(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "mortal_dark");

	public static bool IsPrivate(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "private");

	public static bool IsNoCopy(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "no_clone");

	public static bool IsVisual(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name is "visual" or "public");

	public static bool IsVisual(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name is "visual" or "public");

	public static bool IsRegexp(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "regexp");

	public static bool IsCase(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "case");

	public static bool IsSafe(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "safe");

	/// <summary>
	/// Checks if an attribute is a command pattern ($-command).
	/// Note: This pattern check is centralized here to avoid duplication.
	/// </summary>
	public static bool IsCommand(this SharpAttribute attribute)
		=> attribute.Flags.All(x => x.Name != "no_command") && attribute.Value.ToString().StartsWith('$');

	public static bool IsListen(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "listen");

	public static bool IsNoDump(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "nodump");

	public static bool IsPrefixMatch(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "prefixmatch");

	public static bool IsVeiled(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "veiled");

	public static bool IsDebug(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "debug");

	public static bool IsNoDebug(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "nodebug");

	public static bool IsNearby(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "nearby");

	public static bool IsPublic(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "public");

	public static bool IsPublic(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "public");

	public static bool IsMortalDark(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "mortal_dark");

	public static bool IsMortalHear(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "mortalhear");

	public static bool IsActionHear(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "actionhear");
}
