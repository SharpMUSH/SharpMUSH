namespace SharpMUSH.Configuration.Options;

public record CompatibilityOptions(
	[property: SharpConfig(
		Name = "null_eq_zero",
		Category = "Compatibility",
		Description = "Treat null values as zero in numeric operations",
		Group = "Numeric Behavior",
		Order = 1)]
	bool NullEqualsZero,

	[property: SharpConfig(
		Name = "tiny_booleans",
		Category = "Compatibility",
		Description = "Only values with a nonzero leading signed integer are true (TinyMUSH booleans)",
		Group = "TinyMUSH Compatibility",
		Order = 1)]
	bool TinyBooleans,

	[property: SharpConfig(
		Name = "tiny_trim_fun",
		Category = "Compatibility",
		Description = "Use TinyMUSH-style string trimming in functions",
		Group = "TinyMUSH Compatibility",
		Order = 2)]
	bool TinyTrimFun,

	[property: SharpConfig(
		Name = "tiny_math",
		Category = "Compatibility",
		Description = "Treat nonnumeric strings as zero in numeric operations (TinyMUSH math)",
		Group = "TinyMUSH Compatibility",
		Order = 3)]
	bool TinyMath,

	[property: SharpConfig(
		Name = "silent_pemit",
		Category = "Compatibility",
		Description = "Suppress permission error messages for pemit command",
		Group = "Command Behavior",
		Order = 1)]
	bool SilentPEmit,

	[property: SharpConfig(
		Name = "paren_groups",
		Category = "Compatibility",
		Description = "Treat an unescaped ( that starts no function call as a literal group whose commas and ) are text, as PennMUSH does. A PennMUSH database import turns it on",
		Group = "PennMUSH Compatibility",
		Order = 1)]
	bool ParenGroups,

	[property: SharpConfig(
		Name = "http_qregisters",
		Category = "Compatibility",
		Description = "Also hand HTTP softcode q-registers, as PennMUSH does, besides the named arguments: STATUS, CONTENT-TYPE and HTTP-OVERFLOW to an @http callback, and HDR.<name> and HEADERS to the http_handler; and read @http's CONTENT-TYPE and USERPASS registers. A PennMUSH database import turns it on",
		Group = "PennMUSH Compatibility",
		Order = 2)]
	bool HttpQRegisters,

	[property: SharpConfig(
		Name = "mux_logic",
		Category = "Compatibility",
		Description = "and(), or(), xor(), cand() and cor() test each argument by its leading signed integer, as TinyMUX does, so text such as abc is false, and xor() is true for an odd number of true arguments; t(), if() and the rest keep the usual rule",
		Group = "TinyMUX Compatibility",
		Order = 1)]
	bool MuxLogic,

	[property: SharpConfig(
		Name = "mux_justify",
		Category = "Compatibility",
		Description = "ljust(), rjust() and center() cut text longer than the width, as TinyMUX does, unless a call says otherwise",
		Group = "TinyMUX Compatibility",
		Order = 2)]
	bool MuxJustify,

	[property: SharpConfig(
		Name = "mux_collation",
		Category = "Compatibility",
		Description = "comp() with no type and sort() with no sort type order text by collation, as TinyMUX does (a before A before b), rather than by character code",
		Group = "TinyMUX Compatibility",
		Order = 3)]
	bool MuxCollation,

	[property: SharpConfig(
		Name = "mux_list",
		Category = "Compatibility",
		Description = "list() with two or three arguments is TinyMUX's loop: it evaluates its second argument for each element and tells each result to the enactor. With one argument it stays PennMUSH's list()",
		Group = "TinyMUX Compatibility",
		Order = 4)]
	bool MuxList
,

	[property: SharpConfig(
		Name = "innermost_tokens",
		Category = "Compatibility",
		Description = "Read ## and #@ from the innermost iter() or @dolist that is running, and #$ from the innermost switch whose action is running, wherever the token is written, as TinyMUX does. Off, they read the outermost one whose text they are written in, as PennMUSH does",
		Group = "TinyMUX Compatibility",
		Order = 5)]
	bool InnermostTokens,

	[property: SharpConfig(
		Name = "rhost_edit",
		Category = "Compatibility",
		Description = "Read edit()'s fourth and fifth arguments as RhostMUSH's mode flags (a nonzero fourth replaces only the first match) instead of another from/to pair",
		Group = "RhostMUSH Compatibility",
		Order = 1)]
	bool RhostEdit,

	[property: SharpConfig(
		Name = "rhost_lattr",
		Category = "Compatibility",
		Description = "Read lattr()'s and lattrp()'s arguments as RhostMUSH lays them out: a target to list as, then a type, attribute flags, a regular expression switch and a tree switch, instead of an output delimiter",
		Group = "RhostMUSH Compatibility",
		Order = 2)]
	bool RhostLattr,

	[property: SharpConfig(
		Name = "rhost_printf",
		Category = "Compatibility",
		Description = "Read printf()'s format with RhostMUSH's $-code grammar ($-10s, $^20:-:s ...) instead of SharpMUSH's %-code grammar",
		Group = "RhostMUSH Compatibility",
		Order = 3)]
	bool RhostPrintf,

	[property: SharpConfig(
		Name = "rhost_map_args",
		Category = "Compatibility",
		Description = "In map(), leave %1 empty when no extra arguments are given, as RhostMUSH does, instead of passing the item's position",
		Group = "RhostMUSH Compatibility",
		Order = 4)]
	bool RhostMapArgs,

	[property: SharpConfig(
		Name = "sidefx_flag",
		Category = "Compatibility",
		Description = "Let side-effect functions (pemit(), set(), create() ...) work only for an object with the SIDEFX flag or the softcode.sidefx permission, as RhostMUSH does; others get the disabled-function error",
		Group = "RhostMUSH Compatibility",
		Order = 5)]
	bool SidefxFlag
);
