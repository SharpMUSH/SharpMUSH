using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

public static class SharpAttributeExtensions
{
	public static bool IsInternal(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Internal");

	public static bool IsWizard(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Wizard");

	public static bool IsLocked(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Locked");

	public static bool IsNoprog(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Noprog");

	public static bool IsMortalDark(this SharpAttribute attribute)
		=> attribute.Flags.Contains("MortalDark");

	public static bool IsPrivate(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Private");

	public static bool IsNoCopy(this SharpAttribute attribute)
		=> attribute.Flags.Contains("NoCopy");

	public static bool IsVisual(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Visual") || attribute.IsPublic();

	public static bool IsRegexp(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Regexp");

	public static bool IsCase(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Case");

	public static bool IsSafe(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Safe");

	public static bool IsCommand(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Command");

	public static bool IsListen(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Listen");

	public static bool IsNoDump(this SharpAttribute attribute)
		=> attribute.Flags.Contains("NoDump");

	public static bool IsPrefixMatch(this SharpAttribute attribute)
		=> attribute.Flags.Contains("PrefixMatch");

	public static bool IsVeiled(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Veiled");

	public static bool IsDebug(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Debug");

	public static bool IsNoDebug(this SharpAttribute attribute)
		=> attribute.Flags.Contains("NoDebug");

	public static bool IsNearby(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Nearby");

	public static bool IsPublic(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Public");

	public static bool IsMortalHear(this SharpAttribute attribute)
		=> attribute.Flags.Contains("MortalHear");

	public static bool IsActionHear(this SharpAttribute attribute)
		=> attribute.Flags.Contains("ActionHear");

	public static bool IsQuiet(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Quiet");

	public static bool IsRoot(this SharpAttribute attribute)
		=> attribute.Flags.Contains("Root");
}
