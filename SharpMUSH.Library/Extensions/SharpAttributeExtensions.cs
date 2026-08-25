using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

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

	/// <summary>
	/// PennMUSH's <c>AF_VISUAL</c> alone (<c>attrib.c:306</c>). Do not fold in <c>public</c>:
	/// that flag overrides <c>SAFER_UFUN</c> for evaluation (see <see cref="IsPublic(SharpAttribute)"/>
	/// and <c>PermissionService.CanEvalAttr</c>) and has nothing to do with reading.
	/// </summary>
	public static bool IsVisual(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "visual");

	public static bool IsVisual(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "visual");

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
		=> attribute.Flags.Any(x => x.Name == "no_debug");

	public static bool IsNearby(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "nearby");

	public static bool IsNearby(this LazySharpAttribute attribute)
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

	/// <summary>
	/// The attribute holds a command list (<c>cmdsyntax</c>), for display formatting.
	/// Unrelated to <c>no_command</c>, which governs $-command matching.
	/// </summary>
	public static bool IsCmdSyntax(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "cmdsyntax");

	/// <summary>
	/// The attribute holds a function expression (<c>funsyntax</c>), for display formatting.
	/// </summary>
	public static bool IsFunSyntax(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "funsyntax");

	/// <summary>
	/// The parse dialect declared by the syntax flags, or <c>null</c> when neither is set.
	/// <c>cmdsyntax</c> wins when both are present: a command list may contain function
	/// calls, but not the reverse.
	/// </summary>
	public static ParseType? SyntaxParseType(this SharpAttribute attribute)
		=> attribute.IsCmdSyntax() ? ParseType.CommandList
			: attribute.IsFunSyntax() ? ParseType.Function
			: null;
}
