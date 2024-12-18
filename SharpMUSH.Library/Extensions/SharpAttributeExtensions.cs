using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

public static class SharpAttributeExtensions
{
	public static bool IsInternal(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "INTERNAL");

	public static bool IsWizard(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "WIZARD");

	public static bool IsLocked(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "LOCKED");

	public static bool IsNoprog(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "NOPROG");

	public static bool IsMortalDark(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "MORTALDARK");

	public static bool IsPrivate(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "PRIVATE");

	public static bool IsNoCopy(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "NOCOPY");

	public static bool IsVisual(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name is "VISUAL" or "PUBLIC");

	public static bool IsRegexp(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "REGEXP");

	public static bool IsCase(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "CASE");

	public static bool IsSafe(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "SAFE");

	public static bool IsCommand(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "COMMAND");

	public static bool IsListen(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "LISTEN");

	public static bool IsNoDump(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "NODUMP");

	public static bool IsPrefixMatch(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "PREFIXMATCH");

	public static bool IsVeiled(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "VEILED");

	public static bool IsDebug(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "DEBUG");

	public static bool IsNoDebug(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "NODEBUG");

	public static bool IsNearby(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "NEARBY");

	public static bool IsPublic(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "PUBLIC");

	public static bool IsMortalHear(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "MORTALHEAR");

	public static bool IsActionHear(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "ACTIONHEAR");

	public static bool IsQuiet(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "QUIET");

	public static bool IsRoot(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "ROOT");
}
