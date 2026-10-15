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
		Description = "Also hand HTTP softcode q-registers, as PennMUSH does, besides the named arguments: STATUS and CONTENT-TYPE to an @http callback, and HDR.<name> and HEADERS to the http_handler. A PennMUSH database import turns it on",
		Group = "PennMUSH Compatibility",
		Order = 2)]
	bool HttpQRegisters,

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
