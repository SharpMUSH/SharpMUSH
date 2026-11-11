namespace SharpMUSH.Configuration.Options;

public record CompatibilityOptions(
	[property: SharpConfig(
		Name = "null_eq_zero",
		Category = "Compatibility",
		Description = "Treat null values as zero in numeric operations",
		Group = "TinyMUSH Compatibility",
		Order = 10,
		Emulates = CompatibilityServer.TinyMUSH | CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX,
		HelpTopic = "compatibility config numeric compatibility tiny math null eq zero")]
	bool NullEqualsZero,

	[property: SharpConfig(
		Name = "tiny_booleans",
		Category = "Compatibility",
		Description = "Only values with a nonzero leading signed integer are true (TinyMUSH booleans)",
		Group = "TinyMUSH Compatibility",
		Order = 11,
		Emulates = CompatibilityServer.TinyMUSH,
		SetBy = CompatibilityServer.None,
		HelpTopic = "compatibility config boolean compatibility tiny booleans")]
	bool TinyBooleans,

	[property: SharpConfig(
		Name = "tiny_trim_fun",
		Category = "Compatibility",
		Description = "Use TinyMUSH-style string trimming in functions",
		Group = "TinyMUSH Compatibility",
		Order = 12,
		Emulates = CompatibilityServer.TinyMUSH | CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config trim argument order tiny trim fun")]
	bool TinyTrimFun,

	[property: SharpConfig(
		Name = "tiny_math",
		Category = "Compatibility",
		Description = "Treat nonnumeric strings as zero in numeric operations (TinyMUSH math)",
		Group = "TinyMUSH Compatibility",
		Order = 13,
		Emulates = CompatibilityServer.TinyMUSH | CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX,
		HelpTopic = "compatibility config numeric compatibility tiny math null eq zero")]
	bool TinyMath,

	[property: SharpConfig(
		Name = "silent_pemit",
		Category = "Compatibility",
		Description = "@pemit leaves out its \"You pemit ...\" confirmation, as if given /silent, unless given /noisy, as TinyMUSH does",
		Group = "TinyMUSH Compatibility",
		Order = 14,
		Emulates = CompatibilityServer.TinyMUSH,
		SetBy = CompatibilityServer.None,
		HelpTopic = "compatibility config silent pemit silent pemit")]
	bool SilentPEmit,

	[property: SharpConfig(
		Name = "paren_groups",
		Category = "Compatibility",
		Description = "Treat an unescaped ( that starts no function call as a literal group whose commas and ) are text, as PennMUSH does",
		Group = "PennMUSH Compatibility",
		Order = 1,
		Emulates = CompatibilityServer.PennMUSH | CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.PennMUSH | CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config parenthesis groups paren groups")]
	bool ParenGroups,

	[property: SharpConfig(
		Name = "http_qregisters",
		Category = "Compatibility",
		Description = "Also hand HTTP softcode q-registers, as PennMUSH does, besides the named arguments: STATUS, CONTENT-TYPE and HTTP-OVERFLOW to an @http callback, and HDR.<name> and HEADERS to the http_handler; and read @http's CONTENT-TYPE and USERPASS registers",
		Group = "PennMUSH Compatibility",
		Order = 2,
		Emulates = CompatibilityServer.PennMUSH,
		SetBy = CompatibilityServer.PennMUSH,
		HelpTopic = "compatibility config pennmush http registers http qregisters")]
	bool HttpQRegisters,

	[property: SharpConfig(
		Name = "mux_logic",
		Category = "Compatibility",
		Description = "and(), or(), xor(), cand() and cor() test each argument by its leading signed integer, as TinyMUX does, so text such as abc is false, and xor() is true for an odd number of true arguments; t(), if() and the rest keep the usual rule",
		Group = "TinyMUX Compatibility",
		Order = 20,
		Emulates = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config logical functions mux logic")]
	bool MuxLogic,

	[property: SharpConfig(
		Name = "mux_justify",
		Category = "Compatibility",
		Description = "ljust(), rjust() and center() cut text longer than the width, as TinyMUX does, unless a call says otherwise",
		Group = "TinyMUX Compatibility",
		Order = 21,
		Emulates = CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX,
		HelpTopic = "compatibility config justification mux justify")]
	bool MuxJustify,

	[property: SharpConfig(
		Name = "mux_collation",
		Category = "Compatibility",
		Description = "comp() with no type and sort() with no sort type order text by collation, as TinyMUX does (a before A before b), rather than by character code",
		Group = "TinyMUX Compatibility",
		Order = 22,
		Emulates = CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX,
		HelpTopic = "compatibility config text order mux collation")]
	bool MuxCollation,

	[property: SharpConfig(
		Name = "innermost_tokens",
		Category = "Compatibility",
		Description = "Read ## and #@ from the innermost iter() or @dolist that is running, and #$ from the innermost switch whose action is running, wherever the token is written, as TinyMUX does. Off, they read the outermost one whose text they are written in, as PennMUSH does",
		Group = "TinyMUX Compatibility",
		Order = 23,
		Emulates = CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX,
		HelpTopic = "compatibility config innermost loop tokens")]
	bool InnermostTokens,

	[property: SharpConfig(
		Name = "rhost_edit",
		Category = "Compatibility",
		Description = "Read edit()'s fourth and fifth arguments as RhostMUSH's mode flags (a nonzero fourth replaces only the first match) instead of another from/to pair",
		Group = "RhostMUSH Compatibility",
		Order = 30,
		Emulates = CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config rhostmush edit modes rhost edit")]
	bool RhostEdit,

	[property: SharpConfig(
		Name = "rhost_lattr",
		Category = "Compatibility",
		Description = "Read lattr()'s and lattrp()'s arguments as RhostMUSH lays them out: a target to list as, then a type, attribute flags, a regular expression switch and a tree switch, instead of an output delimiter",
		Group = "RhostMUSH Compatibility",
		Order = 31,
		Emulates = CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config rhostmush lattr arguments rhost lattr")]
	bool RhostLattr,

	[property: SharpConfig(
		Name = "rhost_printf",
		Category = "Compatibility",
		Description = "Read printf()'s format with RhostMUSH's $-code grammar ($-10s, $^20:-:s ...) instead of SharpMUSH's %-code grammar",
		Group = "RhostMUSH Compatibility",
		Order = 32,
		Emulates = CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config rhostmush printf fields rhost printf")]
	bool RhostPrintf,

	[property: SharpConfig(
		Name = "rhost_map_args",
		Category = "Compatibility",
		Description = "In map(), leave %1 empty when no extra arguments are given, as RhostMUSH does, instead of passing the item's position",
		Group = "RhostMUSH Compatibility",
		Order = 33,
		Emulates = CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config rhostmush map arguments rhost map args")]
	bool RhostMapArgs,

	[property: SharpConfig(
		Name = "sidefx_flag",
		Category = "Compatibility",
		Description = "Let side-effect functions (pemit(), set(), create() ...) work only for an object with the SIDEFX flag or the softcode.sidefx permission, as RhostMUSH does; others get the disabled-function error",
		Group = "RhostMUSH Compatibility",
		Order = 34,
		Emulates = CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config rhostmush side effect flag sidefx flag")]
	bool SidefxFlag,

	[property: SharpConfig(
		Name = "rhost_list",
		Category = "Compatibility",
		Description = "list() with two to five arguments is RhostMUSH's loop, which takes TinyMUX's in its first three: it evaluates its second argument for each element and tells each result to the enactor, after an optional header, or to an optional target the enactor controls. list() then needs at least two arguments, so PennMUSH's list() is not reachable",
		Group = "RhostMUSH Compatibility",
		Order = 35,
		Emulates = CompatibilityServer.RhostMUSH | CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config rhostmush list loop rhost list")]
	bool RhostList,

	[property: SharpConfig(
		Name = "dialect",
		Category = "Compatibility",
		Description = "Another server's names for functions and commands that typed input and attributes without a dialect flag also read, after SharpMUSH's own: rhost reads RhostMUSH's (wildmatch(), @sudo ...), mux reads TinyMUX's (strtrunc(), @qmail ...). Empty reads none",
		ValidationPattern = @"^(rhost|mux)?$",
		Group = "Dialects",
		Order = 40,
		Emulates = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config other servers names dialect")]
	string? Dialect,

	[property: SharpConfig(
		Name = "mux_queue_functions",
		Category = "Compatibility",
		Description = "Turn on trigger() and verb(), which queue an attribute's actions from inside a function, as TinyMUX does. Off, they answer the disabled-function error",
		Group = "TinyMUX Compatibility",
		Order = 24,
		Emulates = CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX,
		HelpTopic = "compatibility config tinymux queue functions mux queue functions")]
	bool MuxQueueFunctions,

	[property: SharpConfig(
		Name = "rhost_lrooms",
		Category = "Compatibility",
		Description = "lrooms() takes any depth, and a depth of 0 follows exits as far as they lead, as RhostMUSH does; it lists at most 1,000 rooms. Off, a depth of 0 lists nothing and the deepest is 50, as on TinyMUX",
		Group = "RhostMUSH Compatibility",
		Order = 36,
		Emulates = CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config rhostmush lrooms depth rhost lrooms")]
	bool RhostLrooms,

	[property: SharpConfig(
		Name = "mux_quiet",
		Category = "Compatibility",
		Description = "The /quiet switch of drop, get, give, goto, enter, leave and @teleport does what TinyMUX's does: it holds only on what you control and leaves out the o-message and action, give/quiet is a wizard's and leaves out the default notices, and @teleport/quiet hushes leaving and arriving but runs @move. Off, /quiet leaves out your own message and the o-message, and the action still runs",
		Group = "TinyMUX Compatibility",
		Order = 25,
		Emulates = CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX,
		HelpTopic = "compatibility config tinymux quiet switches mux quiet")]
	bool MuxQuiet,

	[property: SharpConfig(
		Name = "mux_function_caller",
		Category = "Compatibility",
		Description = "A global @function defined without /privileged runs as whoever calls it, as on TinyMUX: %! and me are the caller, with the caller's permissions. One defined with /privileged runs as the object that holds it. Off, every global @function runs as the object that holds it, as on PennMUSH",
		Group = "TinyMUX Compatibility",
		Order = 26,
		Emulates = CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX,
		HelpTopic = "compatibility config tinymux function caller mux function caller")]
	bool MuxFunctionCaller,

	[property: SharpConfig(
		Name = "mapsql_sync",
		Category = "Compatibility",
		Description = "@mapsql runs its query inside the command, as PennMUSH does, so every row is queued before anything queued after the command; the game waits while the query runs. Off, the query runs while the game goes on and each row is queued as it arrives",
		Group = "PennMUSH Compatibility",
		Order = 3,
		Emulates = CompatibilityServer.PennMUSH,
		SetBy = CompatibilityServer.PennMUSH,
		HelpTopic = "compatibility config pennmush mapsql order mapsql sync")]
	bool MapsqlSync
)
{
	[SharpConfig(
		Name = "protection_flags",
		Category = "Compatibility",
		Description = "The NOEXAMINE and NOMODIFY flags protect what holds them, as in RhostMUSH and TinyMUX: below wizard and royalty, nobody examines or @decompiles a NOEXAMINE object, and nobody, its owner included, changes a NOMODIFY one",
		Group = "RhostMUSH Compatibility",
		Order = 37,
		Emulates = CompatibilityServer.RhostMUSH | CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config protection flags noexamine nomodify noconnect")]
	public bool ProtectionFlags { get; init; }

	[SharpConfig(
		Name = "attribute_eval_flags",
		Category = "Compatibility",
		Description = "The noeval and noparse attribute flags work as in TinyMUX and RhostMUSH. A noeval attribute is never evaluated: u(), get_eval() and a description or other message give its text as written, and a noeval action attribute does not run. A noparse $-command is matched against the command as typed, before it is evaluated. Off, both flags do nothing",
		Group = "TinyMUX Compatibility",
		Order = 27,
		Emulates = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		HelpTopic = "compatibility config attribute eval flags")]
	public bool AttributeEvalFlags { get; init; }

	[SharpConfig(
		Name = "mux_arguments",
		Category = "Compatibility",
		Description = "Read the arguments TinyMUX gives a different meaning: a fourth argument to setunion(), setdiff(), setinter() and setsymdiff() is the output separator; hmac() takes the text, the key and then the digest; lwho() with one argument lists dbref:port pairs when it is true; channels() lists the channels a player owns, with a delimiter, an offset and a limit; textfile() and dynhelp() whose first argument names no text file read an object's HELP_ attributes",
		Group = "TinyMUX Compatibility",
		Order = 28,
		Emulates = CompatibilityServer.TinyMUX,
		SetBy = CompatibilityServer.TinyMUX,
		HelpTopic = "compatibility config tinymux argument meanings mux arguments")]
	public bool MuxArguments { get; init; }

	[SharpConfig(
		Name = "space_compress",
		Category = "Compatibility",
		Description = "Text being evaluated has each run of spaces made one space, and the spaces before it dropped, as on PennMUSH. Off, spaces stay as written, as on a TinyMUX or RhostMUSH game whose configuration turns its space_compress off; importing that configuration turns this off too",
		Group = "TinyMUX Compatibility",
		Order = 29,
		Emulates = CompatibilityServer.TinyMUX | CompatibilityServer.RhostMUSH,
		SetBy = CompatibilityServer.None,
		HelpTopic = "compatibility config spaces space compress")]
	public bool SpaceCompress { get; init; } = true;
}
