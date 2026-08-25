using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Extensions;

public static class SharpAttributeExtensions
{
	public static bool IsInternal(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("internal", StringComparison.OrdinalIgnoreCase));

	public static bool IsInternal(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("internal", StringComparison.OrdinalIgnoreCase));

	public static bool IsWizard(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("wizard", StringComparison.OrdinalIgnoreCase));

	public static bool IsLocked(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("locked", StringComparison.OrdinalIgnoreCase));

	public static bool IsMortalDark(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("mortal_dark", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// PennMUSH's <c>AF_NoInherit</c> (<c>attrib.c:1232-1252</c>), tested on EVERY level of a tree
	/// attribute's path, not just the leaf - a no_inherit branch blocks its whole subtree.
	/// <para>
	/// Compares case-insensitively: flag names are resolved from a canonical, case-normalised
	/// catalog under the ordinary <c>@set</c> path, but this is the one place every provider's
	/// inheritance gate (ArangoDB, Memgraph, SurrealDB, and the parent-boundary re-resolution in
	/// <c>GetAttributeQueryHandler</c>) tests for it - a hand-rolled ordinal comparison at any of
	/// those sites would silently diverge from the others the moment stored casing wasn't
	/// canonical (imported data, a hand-edited record). Route every gate through here instead.
	/// </para>
	/// </summary>
	public static bool IsNoInherit(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("no_inherit", StringComparison.OrdinalIgnoreCase));

	/// <inheritdoc cref="IsNoInherit(SharpAttribute)"/>
	public static bool IsNoInherit(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("no_inherit", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// PennMUSH's <c>AF_Nocopy</c> (<c>attrib.c:1703</c>), tested by <c>atr_cpy</c> before
	/// copying an attribute during <c>@CLONE</c>.
	/// <para>
	/// Compares case-insensitively, matching <see cref="IsNoInherit(SharpAttribute)"/> - this
	/// had zero production callers until @CLONE's attribute-tree fix made it load-bearing, and
	/// PR #808's case-insensitivity sweep predates that, so a hand-rolled ordinal comparison
	/// here would have been the exact bug class that sweep fixed at 14 other sites, just never
	/// exercised.
	/// </para>
	/// </summary>
	public static bool IsNoCopy(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("no_clone", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// PennMUSH's <c>AF_VISUAL</c> alone (<c>attrib.c:306</c>). Do not fold in <c>public</c>:
	/// that flag overrides <c>SAFER_UFUN</c> for evaluation (see <see cref="IsPublic(SharpAttribute)"/>
	/// and <c>PermissionService.CanEvalAttr</c>) and has nothing to do with reading.
	/// </summary>
	public static bool IsVisual(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("visual", StringComparison.OrdinalIgnoreCase));

	public static bool IsVisual(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("visual", StringComparison.OrdinalIgnoreCase));

	public static bool IsRegexp(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("regexp", StringComparison.OrdinalIgnoreCase));

	public static bool IsCase(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("case", StringComparison.OrdinalIgnoreCase));

	public static bool IsSafe(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("safe", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Checks if an attribute is a command pattern ($-command).
	/// Note: This pattern check is centralized here to avoid duplication.
	/// </summary>
	public static bool IsCommand(this SharpAttribute attribute)
		=> attribute.Flags.All(x => !x.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase))
			&& attribute.Value.ToString().StartsWith('$');

	public static bool IsNoDump(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("nodump", StringComparison.OrdinalIgnoreCase));

	public static bool IsPrefixMatch(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("prefixmatch", StringComparison.OrdinalIgnoreCase));

	public static bool IsVeiled(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("veiled", StringComparison.OrdinalIgnoreCase));

	public static bool IsDebug(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("debug", StringComparison.OrdinalIgnoreCase));

	public static bool IsNoDebug(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("no_debug", StringComparison.OrdinalIgnoreCase));

	public static bool IsNearby(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("nearby", StringComparison.OrdinalIgnoreCase));

	public static bool IsNearby(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("nearby", StringComparison.OrdinalIgnoreCase));

	public static bool IsPublic(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("public", StringComparison.OrdinalIgnoreCase));

	public static bool IsPublic(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("public", StringComparison.OrdinalIgnoreCase));

	public static bool IsMortalDark(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("mortal_dark", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// PennMUSH's <c>AF_MHEAR</c> (<c>hdrs/attrib.h:165</c>, catalog name <c>amhear</c>): a
	/// <c>^</c>-listen on this attribute is triggered only by sound the object itself makes,
	/// matching <c>@amhear</c> rather than the default <c>@ahear</c> behaviour.
	/// <para>
	/// Previously misnamed <c>IsMortalHear</c> checking a non-existent <c>"mortalhear"</c> flag -
	/// no provider seed defines that name, so the check was permanently <see langword="false"/>.
	/// The <c>M</c>/<c>A</c> symbols stand for "me" and "all" (see <c>game/txt/hlp/penncmd.hlp</c>
	/// under <c>@amhear</c>/<c>@aahear</c>), not "mortal"/"action" as the old name implied.
	/// </para>
	/// </summary>
	public static bool IsAmHear(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("amhear", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// PennMUSH's <c>AF_AHEAR</c> (<c>hdrs/attrib.h:166</c>, catalog name <c>aahear</c>): a
	/// <c>^</c>-listen on this attribute is triggered by all matching sound regardless of source,
	/// matching <c>@aahear</c> rather than the default <c>@ahear</c> behaviour.
	/// <para>
	/// Previously misnamed <c>IsActionHear</c> checking a non-existent <c>"actionhear"</c> flag -
	/// see <see cref="IsAmHear(SharpAttribute)"/> for why the old name was wrong.
	/// </para>
	/// </summary>
	public static bool IsAaHear(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("aahear", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// The attribute holds a command list (<c>cmdsyntax</c>), for display formatting.
	/// Unrelated to <c>no_command</c>, which governs $-command matching.
	/// </summary>
	public static bool IsCmdSyntax(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("cmdsyntax", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// The attribute holds a function expression (<c>funsyntax</c>), for display formatting.
	/// </summary>
	public static bool IsFunSyntax(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("funsyntax", StringComparison.OrdinalIgnoreCase));

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
