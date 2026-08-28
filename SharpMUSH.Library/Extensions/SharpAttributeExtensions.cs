using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
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

	/// <summary>
	/// PennMUSH's <c>AF_NOPROG</c>, which <c>attr_privs_set</c> spells <c>no_command</c>
	/// (<c>src/atr_tab.c:34</c>) - "noprog" is the C symbol, never a stored flag name, so this
	/// tested for a flag no provider seeds and could never be true.
	/// </summary>
	public static bool IsNoprog(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase));

	public static bool IsMortalDark(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("mortal_dark", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// PennMUSH's <c>AF_PRIVATE</c>. <c>attr_privs_set</c> gives that one bit two names -
	/// <c>no_inherit</c> and <c>private</c>, both on <c>'i'</c> (<c>src/atr_tab.c:35-36</c>) - and
	/// SharpMUSH seeds only the first, so testing for a flag literally named "private" could never
	/// be true. Delegates rather than duplicating the string: this IS
	/// <see cref="IsNoInherit(SharpAttribute)"/>, under Penn's other name for it.
	/// </summary>
	public static bool IsPrivate(this SharpAttribute attribute)
		=> attribute.IsNoInherit();

	/// <summary>
	/// PennMUSH's <c>AF_PRIVATE</c> (spelled <c>no_inherit</c> here): the attribute does not
	/// cross an inheritance boundary. Tested on EVERY level of a tree attribute's path, not just
	/// the leaf - a no_inherit branch blocks its whole subtree (<c>atr_get_with_parent</c>,
	/// <c>src/attrib.c:1232-1252</c>).
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
	/// Whether the attribute is a <c>$</c>-command. PennMUSH derives this
	/// (<c>set_cmd_flags</c>, <c>src/attrib.c:840-859</c>) rather than storing it: the value must
	/// start with <c>$</c> AND contain an unescaped <c>:</c>. The sigil alone is not enough -
	/// <c>&amp;FOO me=$hello</c> is not a command in Penn, and is not one here either.
	/// <para>
	/// Shares <see cref="CommandDiscoveryService.CommandPatternRegex"/> with the scanner that
	/// actually dispatches commands, so a coarse "does this object have commands" check cannot
	/// answer yes about an attribute the dispatcher will never match. Together with the
	/// <c>no_command</c> term this is exactly Penn's own has-active-commands test,
	/// <c>if (AF_Command(ptr) &amp;&amp; !AF_Noprog(ptr)) return 1;</c> (<c>src/game.c:1597</c>) -
	/// which is the single caller this predicate has.
	/// </para>
	/// </summary>
	public static bool IsCommand(this SharpAttribute attribute)
		=> attribute.Flags.All(x => !x.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase))
			&& CommandDiscoveryService.CommandPatternRegex().IsMatch(attribute.Value.ToPlainText());

	/// <summary>
	/// Whether the attribute is a <c>^</c>-listen pattern. Derived exactly like
	/// <see cref="IsCommand"/> - <c>set_cmd_flags</c> handles <c>^</c> and <c>$</c> in one switch,
	/// and PennMUSH's <c>AF_LISTEN</c> is INTERNAL (<c>hdrs/attrib.h:152</c>, "value starts with ^"),
	/// never a settable flag. This previously tested for a stored flag named "listen", which no
	/// provider seeds and Penn has no name for, so it could never be true.
	/// <para>
	/// The <c>no_command</c> term is not an analogy: <c>atr_comm_match</c>'s <c>AF_Noprog</c> skip
	/// and its <c>nocmd_roots</c> subtree propagation sit in the loop shared by both dialects
	/// (<c>src/attrib.c:1935, 1984</c>) - only <c>flag_mask</c> and the object-level
	/// <c>NoCommand(thing)</c> check differ between <c>$</c> and <c>^</c>.
	/// <para>
	/// Note <see cref="CommandDiscoveryService.ListenPatternRegex"/> is deliberately the naive
	/// <c>[^:]+</c> form rather than the command regex's escape-aware lookbehind; this follows the
	/// handler that dispatches listens rather than diverging from it.
	/// </para>
	/// </summary>
	public static bool IsListen(this SharpAttribute attribute)
		=> attribute.Flags.All(x => !x.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase))
			&& CommandDiscoveryService.ListenPatternRegex().IsMatch(attribute.Value.ToPlainText());

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
	/// PennMUSH's <c>AF_MHEAR</c>, stored as <c>amhear</c> (<c>src/atr_tab.c:56</c>): the attribute
	/// triggers on things the object itself hears. "mortalhear" is not a flag name anywhere.
	/// </summary>
	public static bool IsMortalHear(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("amhear", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// PennMUSH's <c>AF_AHEAR</c>, stored as <c>aahear</c> (<c>src/atr_tab.c:57</c>): the attribute
	/// triggers on anything anyone hears. "actionhear" is not a flag name anywhere.
	/// </summary>
	public static bool IsActionHear(this SharpAttribute attribute)
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
