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
		"@sqldisconnect", "@tor", "@remote", "@list_file", "newsdb", "+players", "wielded", "worn"
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

	/// <summary>Whether SharpMUSH leaves out the function <paramref name="name"/> of the dialect <paramref name="dialect"/>.</summary>
	public static bool LeavesOutFunction(string? dialect, string name) => dialect switch
	{
		_ when dialect == SoftcodeDialects.Rhost.Key => RhostFunctions.Contains(name),
		_ when dialect == SoftcodeDialects.Mux.Key => MuxFunctions.Contains(name),
		_ => false
	};

	/// <summary>Whether SharpMUSH leaves out the command <paramref name="name"/> of the dialect <paramref name="dialect"/>.</summary>
	public static bool LeavesOutCommand(string? dialect, string name) => dialect switch
	{
		_ when dialect == SoftcodeDialects.Rhost.Key => RhostCommands.Contains(name),
		_ when dialect == SoftcodeDialects.Mux.Key => MuxCommands.Contains(name),
		_ => false
	};
}
