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
		Name = "innermost_tokens",
		Category = "Compatibility",
		Description = "Read ## and #@ from the innermost iter() or @dolist that is running, and #$ from the innermost switch whose action is running, wherever the token is written, as TinyMUX does. Off, they read the outermost one whose text they are written in, as PennMUSH does",
		Group = "TinyMUX Compatibility",
		Order = 4)]
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
	bool SidefxFlag,

	[property: SharpConfig(
		Name = "rhost_list",
		Category = "Compatibility",
		Description = "list() with two to five arguments is RhostMUSH's loop, which takes TinyMUX's in its first three: it evaluates its second argument for each element and tells each result to the enactor, after an optional header, or to an optional target the enactor controls. list() then needs at least two arguments, so PennMUSH's list() is not reachable",
		Group = "RhostMUSH Compatibility",
		Order = 6)]
	bool RhostList,

	[property: SharpConfig(
		Name = "dialect",
		Category = "Compatibility",
		Description = "Another server's names for functions and commands that typed input and attributes without a dialect flag also read, after SharpMUSH's own: rhost reads RhostMUSH's (wildmatch(), @sudo ...), mux reads TinyMUX's (strtrunc(), @qmail ...). Empty reads none. A RhostMUSH or TinyMUX database import sets it",
		ValidationPattern = @"^(rhost|mux)?$",
		Group = "Dialects",
		Order = 1)]
	string? Dialect,

	[property: SharpConfig(
		Name = "mux_queue_functions",
		Category = "Compatibility",
		Description = "Turn on trigger() and verb(), which queue an attribute's actions from inside a function, as TinyMUX does. Off, they answer the disabled-function error. Importing a TinyMUX database turns it on",
		Group = "TinyMUX Compatibility",
		Order = 5)]
	bool MuxQueueFunctions,

	[property: SharpConfig(
		Name = "rhost_iter_separator",
		Category = "Compatibility",
		Description = "iter() and parse() put a space between results when no output separator is given, as RhostMUSH does, instead of the list's delimiter",
		Group = "RhostMUSH Compatibility",
		Order = 7)]
	bool RhostIterSeparator,

	[property: SharpConfig(
		Name = "rhost_lrooms",
		Category = "Compatibility",
		Description = "lrooms() takes any depth, and a depth of 0 follows exits as far as they lead, as RhostMUSH does; it lists at most 1,000 rooms. Off, a depth of 0 lists nothing and the deepest is 50, as on TinyMUX",
		Group = "RhostMUSH Compatibility",
		Order = 8)]
	bool RhostLrooms,

	[property: SharpConfig(
		Name = "mux_quiet",
		Category = "Compatibility",
		Description = "The /quiet switch of drop, get, give, goto, enter, leave and @teleport does what TinyMUX's does: it holds only on what you control and leaves out the o-message and action, give/quiet is a wizard's and leaves out the default notices, and @teleport/quiet hushes leaving and arriving but runs @move. Off, /quiet leaves out your own message and the o-message, and the action still runs. Importing a TinyMUX database turns it on",
		Group = "TinyMUX Compatibility",
		Order = 6)]
	bool MuxQuiet,

	[property: SharpConfig(
		Name = "mapsql_sync",
		Category = "Compatibility",
		Description = "@mapsql runs its query inside the command, as PennMUSH does, so every row is queued before anything queued after the command; the game waits while the query runs. Off, the query runs while the game goes on and each row is queued as it arrives. A PennMUSH database import turns it on",
		Group = "PennMUSH Compatibility",
		Order = 3)]
	bool MapsqlSync
);
