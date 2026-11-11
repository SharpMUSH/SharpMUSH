using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>
/// The functions and commands of RhostMUSH and TinyMUX that SharpMUSH leaves out on purpose, by dialect. The help
/// topic <c>compatibility left out</c> lists each one with the reason; the post-import report points there when
/// imported code uses one.
/// </summary>
public static class ImportLeftOut
{
	/// <summary>RhostMUSH functions SharpMUSH leaves out.</summary>
	public static readonly IReadOnlySet<string> RhostFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		// Account layer: SharpMUSH has accounts of its own.
		"account_boot", "account_login", "account_owner", "account_su", "account_who",
		// Clusters: one object holds enough attributes here.
		"iscluster", "cluster_add", "cluster_attrcnt", "cluster_default", "cluster_edefault", "cluster_flags",
		"cluster_get", "cluster_get_eval", "cluster_grep", "cluster_hasattr", "cluster_hasflag", "cluster_lattr",
		"cluster_regrep", "cluster_regrepi", "cluster_set", "cluster_stats", "cluster_u", "cluster_u2",
		"cluster_u2default", "cluster_u2ldefault", "cluster_u2local", "cluster_udefault", "cluster_ueval",
		"cluster_uldefault", "cluster_ulocal", "cluster_vattrcnt", "cluster_wipe", "cluster_xget",
		// Passwords are never softcode values.
		"attrpass", "genpassword", "passwd",
		// Text is always Unicode.
		"isunicode", "isutf8",
		// Host scripts, host files and host networking.
		"execscript", "execscriptnr", "logtofile", "stderr", "nslookup", "ipv4math",
		// SQL connection management.
		"sqlite_query", "sqlon", "sqloff", "sqlping",
		// Engine internals.
		"crc32obj", "chkgarbage", "safebuff", "limits", "logstatus", "countspecial",
		// Time zones.
		"ruler", "runintz", "listtzones", "secstz", "msecstz",
		// Attribute shorthands and argument-stack tricks.
		"guild", "race", "shift", "args", "passthrough",
		// Little used; added when a game needs one.
		"array", "template", "strmath", "packmath", "sortlist", "garble", "rotl", "rotr", "trreverse", "ee", "bucket",
		"lloc", "sees", "writable", "sweep", "quietnomatch", "regnummatch", "regnummatchi", "regeditlit", "regeditilit",
		"regeditalllit", "regeditallilit", "esclist", "unesclist", "editansi", "ansipos", "xorflag", "listnewsgroups",
		"listprotection", "inprogram", "programmer", "rset"
	};

	/// <summary>RhostMUSH commands SharpMUSH leaves out.</summary>
	public static readonly IReadOnlySet<string> RhostCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"@cluster", "@mark", "@mark_all", "@apply_marked", "@cut", "@fixdb", "@dbclean", "@bfree", "@fnd", "@destroyall",
		"@convert", "@timewarp", "@crc32obj", "@atrcache", "@limit", "@flaglevel", "@protect", "@money", "@robot",
		"@snoop", "@turtle", "slay", "@rwho", "@mudwho", "rwho", "head", "post", "put", "@door", "@sqlconnect",
		"@sqldisconnect", "@tor", "@remote", "@list_file", "newsdb", "+players", "wielded", "worn", "mail"
	};

	/// <summary>
	/// RhostMUSH switches SharpMUSH leaves out, as <c>&lt;command&gt;/&lt;switch&gt;</c> under SharpMUSH's name for the
	/// command.
	/// </summary>
	public static readonly IReadOnlySet<string> RhostSwitches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		// Colour is markup, never text, so there is no raw form to show or edit.
		"@pemit/noansi", "@emit/noansi", "say/noansi", "pose/noansi", "think/noansi", "page/noansi", "@edit/raw",
		// Reality levels.
		"@pemit/reality", "@pemit/toreality",
		// Running code as another object, or changing who owns what runs.
		"@include/target", "@include/sudo", "@include/become", "@include/possess", "@hook/include",
		// Power levels: a power here is a role or a permission.
		"@power/guildmaster", "@power/architect", "@power/councilor", "@power/check", "@power/purge", "@power/off",
		// @queue/pause and /resume pause an entry, and @wait hands back no pid.
		"@halt/stop", "@halt/cont", "@wait/recpid", "@notify/pid",
		// @tag.
		"@decompile/tags",
		// Little used; added when a game needs one.
		"@emit/sub", "@wipe/owner", "@wipe/preserve", "@decompile/noextra", "@include/override", "@include/ignore",
		"@function/protected", "@edit/braces", "@search/zone", "examine/quick", "examine/regexp"
	};

	/// <summary>TinyMUX functions SharpMUSH leaves out.</summary>
	public static readonly IReadOnlySet<string> MuxFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"rserror", "rsnext", "rsprev", "rsrec", "rsrecnext", "rsrecprev", "rsrelease", "rsrows",
		"setname", "setparent", "text", "format", "mailj"
	};

	/// <summary>TinyMUX commands SharpMUSH leaves out.</summary>
	public static readonly IReadOnlySet<string> MuxCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"@cron", "@crondel", "@crontab", "@lua"
	};

	/// <summary>
	/// TinyMUX switches SharpMUSH leaves out, as <c>&lt;command&gt;/&lt;switch&gt;</c> under SharpMUSH's name for the
	/// command.
	/// </summary>
	public static readonly IReadOnlySet<string> MuxSwitches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		// Money.
		"@clone/cost",
		// Raw HTML.
		"@emit/html", "@pemit/html",
		// Exits are opened only from rooms.
		"@open/inventory",
		// Flags are renamed in the configuration, not for the session.
		"@flag/remove",
		// Everyone sees titles, and @channel/mute hides connection notices.
		"comtitle/on", "comtitle/off", "comtitle/gag", "comtitle/ungag"
	};

	/// <summary>Whether SharpMUSH leaves out the function <paramref name="name"/> of the dialect <paramref name="dialect"/>.</summary>
	public static bool LeavesOutFunction(string? dialect, string name) => dialect switch
	{
		_ when dialect == SoftcodeDialects.Rhost.Key => RhostFunctions.Contains(name),
		_ when dialect == SoftcodeDialects.Mux.Key => MuxFunctions.Contains(name),
		_ => false
	};

	/// <summary>
	/// Whether SharpMUSH leaves out the switch <paramref name="typed"/> of the command SharpMUSH calls
	/// <paramref name="command"/>, in the dialect <paramref name="dialect"/>; a shortened switch counts.
	/// </summary>
	public static bool LeavesOutSwitch(string? dialect, string command, string typed)
	{
		var switches = dialect == SoftcodeDialects.Rhost.Key ? RhostSwitches
			: dialect == SoftcodeDialects.Mux.Key ? MuxSwitches
			: null;
		var prefix = $"{command}/{typed}";
		return typed.Length > 0 && switches?.Any(entry => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is true;
	}

	/// <summary>Whether SharpMUSH leaves out the command <paramref name="name"/> of the dialect <paramref name="dialect"/>.</summary>
	public static bool LeavesOutCommand(string? dialect, string name) => dialect switch
	{
		_ when dialect == SoftcodeDialects.Rhost.Key => RhostCommands.Contains(name),
		_ when dialect == SoftcodeDialects.Mux.Key => MuxCommands.Contains(name),
		_ => false
	};
}
