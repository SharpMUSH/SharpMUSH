using System.Collections.Frozen;

namespace SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

/// <summary>
/// What each number in a TinyMUX flatfile means here: object flags (three words), powers (two words),
/// attribute flags, built-in attribute numbers and lock attributes. The bit values and names are
/// TinyMUX's (<c>flags.h</c>, <c>powers.h</c>, <c>attrs.h</c>); the right-hand sides are SharpMUSH's.
/// </summary>
/// <remarks>
/// A TinyMUX name that SharpMUSH spells differently is renamed (SEETHRU is TRANSPARENT). A flag or power
/// SharpMUSH has no counterpart for is created as a custom definition (<see cref="CustomFlags"/>,
/// <see cref="CustomPowers"/>): objects keep it and softcode can test it, but nothing in the engine acts
/// on it. Server state that TinyMUX keeps in flag bits (CONNECTED, GOING, HAS_STARTUP ...) is dropped.
/// </remarks>
public static class TinyMuxNames
{
	/// <summary>Object type in the low three bits of the first flag word (<c>TYPE_MASK</c>).</summary>
	public const int TypeMask = 0x7;

	public const int TypeRoom = 0;
	public const int TypeThing = 1;
	public const int TypeExit = 2;
	public const int TypePlayer = 3;
	public const int TypeGarbage = 5;

	/// <summary>Where a flag bit goes: the SharpMUSH flags it becomes, or why it is not kept.</summary>
	public sealed record FlagMapping(string Source, string[] Flags, string? Power = null, string? Dropped = null);

	private static FlagMapping Same(string name) => new(name, [name]);
	private static FlagMapping As(string source, params string[] flags) => new(source, flags);
	private static FlagMapping Internal(string source) => new(source, [], Dropped: "server state, not kept");

	/// <summary>The first flag word, by bit.</summary>
	public static readonly FrozenDictionary<uint, FlagMapping> Flags1 = new Dictionary<uint, FlagMapping>
	{
		[0x8] = As("SEETHRU", "TRANSPARENT"),
		[0x10] = Same("WIZARD"),
		[0x20] = Same("LINK_OK"),
		[0x40] = Same("DARK"),
		[0x80] = Same("JUMP_OK"),
		[0x100] = Same("STICKY"),
		[0x200] = Same("DESTROY_OK"),
		[0x400] = Same("HAVEN"),
		[0x800] = Same("QUIET"),
		[0x1000] = As("HALTED", "HALT"),
		[0x2000] = As("TRACE", "DEBUG"),
		[0x4000] = Internal("GOING"),
		[0x8000] = Same("MONITOR"),
		[0x10000] = Same("MYOPIC"),
		[0x20000] = Same("PUPPET"),
		[0x40000] = Same("CHOWN_OK"),
		[0x80000] = Same("ENTER_OK"),
		[0x100000] = Same("VISUAL"),
		[0x200000] = new("IMMORTAL", [], Power: "Immortal"),
		[0x400000] = Internal("HAS_STARTUP"),
		[0x800000] = Same("OPAQUE"),
		[0x1000000] = Same("VERBOSE"),
		[0x2000000] = As("INHERIT", "TRUST"),
		[0x4000000] = Same("NOSPOOF"),
		[0x8000000] = Same("ROBOT"),
		[0x10000000] = Same("SAFE"),
		[0x20000000] = Same("ROYALTY"),
		[0x40000000] = As("HEARTHRU", "AUDIBLE"),
		[0x80000000] = Same("TERSE"),
	}.ToFrozenDictionary();

	/// <summary>The second flag word, by bit.</summary>
	public static readonly FrozenDictionary<uint, FlagMapping> Flags2 = new Dictionary<uint, FlagMapping>
	{
		[0x1] = Same("KEY"),
		[0x2] = Same("ABODE"),
		[0x4] = Same("FLOATING"),
		[0x8] = Same("UNFINDABLE"),
		[0x10] = Same("PARENT_OK"),
		[0x20] = Same("LIGHT"),
		[0x40] = Internal("HAS_LISTEN"),
		[0x80] = Internal("HAS_FWDLIST"),
		[0x100] = Same("AUDITORIUM"),
		[0x200] = As("ANSI", "ANSI", "COLOR"),
		[0x400] = Same("HEAD"),
		[0x800] = Same("FIXED"),
		[0x1000] = Same("UNINSPECTED"),
		[0x2000] = Same("NO_COMMAND"),
		[0x4000] = As("KEEPALIVE", "KEEPALIVE"),
		[0x8000] = Same("NOBLEED"),
		[0x10000] = Same("STAFF"),
		[0x20000] = Internal("HAS_DAILY"),
		[0x40000] = Same("GAGGED"),
		[0x80000] = Same("OPEN_OK"),
		[0x100000] = As("COLOR256", "XTERM256"),
		[0x200000] = Same("TALKMODE"),
		[0x400000] = Same("TRUECOLOR"),
		[0x1000000] = As("VACATION", "ON-VACATION"),
		[0x2000000] = Internal("PLAYER_MAILS"),
		[0x4000000] = Same("HTML"),
		[0x8000000] = Same("BLIND"),
		[0x10000000] = Same("SUSPECT"),
		[0x20000000] = As("ASCII", "NOACCENTS"),
		[0x40000000] = Internal("CONNECTED"),
		[0x80000000] = Same("SLAVE"),
	}.ToFrozenDictionary();

	/// <summary>The third flag word, by bit. The first ten are the World of Darkness realm flags.</summary>
	public static readonly FrozenDictionary<uint, FlagMapping> Flags3 = new Dictionary<uint, FlagMapping>
	{
		[0x1] = Same("OBF"),
		[0x2] = Same("HSS"),
		[0x4] = Same("UMBRA"),
		[0x8] = Same("SHROUD"),
		[0x10] = Same("MATRIX"),
		[0x20] = Same("MEDIUM"),
		[0x40] = Same("DEAD"),
		[0x80] = Same("FAE"),
		[0x100] = Same("CHIMERA"),
		[0x200] = Same("PEERING"),
		[0x400] = Same("SITEMON"),
		[0x800] = Internal("CMDCHECK"),
		[0x1000] = Same("UNICODE"),
		[0x2000] = Same("ALONE"),
		[0x4000] = As("NOEXAMINE", "NO_EXAMINE"),
		[0x8000] = Same("NOMODIFY"),
		[0x10000] = Same("INDESTRUCTIBLE"),
		[0x20000] = Same("NOEVAL"),
		[0x40000] = Same("NAVIGABLE"),
		[0x400000] = Same("MARKER0"),
		[0x800000] = Same("MARKER1"),
		[0x1000000] = Same("MARKER2"),
		[0x2000000] = Same("MARKER3"),
		[0x4000000] = Same("MARKER4"),
		[0x8000000] = Same("MARKER5"),
		[0x10000000] = Same("MARKER6"),
		[0x20000000] = Same("MARKER7"),
		[0x40000000] = Same("MARKER8"),
		[0x80000000] = Same("MARKER9"),
	}.ToFrozenDictionary();

	/// <summary>
	/// The TinyMUX flags SharpMUSH has no counterpart for, created as custom flags with TinyMUX's letter
	/// where it has one, so objects keep them and <c>hasflag()</c> still answers. None of them changes what
	/// the engine does.
	/// </summary>
	public static readonly PennMUSHFlagDefinition[] CustomFlags =
	[
		Custom("ROBOT", "r", ["PLAYER", "THING"]),
		Custom("KEY", "K"),
		Custom("PARENT_OK", "Y"),
		Custom("AUDITORIUM", "b"),
		Custom("HEAD", "?", perms: ["wizard"]),
		Custom("NOBLEED", "-"),
		Custom("STAFF", "w", perms: ["wizard"]),
		Custom("SLAVE", "x", ["PLAYER"], ["wizard"]),
		Custom("TALKMODE", ""),
		Custom("HTML", "("),
		Custom("BLIND", "B", perms: ["wizard"]),
		Custom("SITEMON", "$", perms: ["wizard"]),
		Custom("UNICODE", ""),
		Custom("ALONE", "", perms: ["wizard"]),
		Custom("NO_EXAMINE", "E", perms: ["royalty"], aliases: ["NOEXAMINE"]),
		Custom("NOMODIFY", "", perms: ["royalty"], aliases: ["NO_MODIFY"]),
		Custom("INDESTRUCTIBLE", "", perms: ["royalty"]),
		Custom("NOEVAL", ""),
		Custom("NAVIGABLE", "", ["ROOM"]),
		Custom("OBF", "", perms: ["royalty"]),
		Custom("HSS", "", perms: ["royalty"]),
		Custom("UMBRA", "", perms: ["royalty"]),
		Custom("SHROUD", "", perms: ["royalty"]),
		Custom("MATRIX", "", perms: ["royalty"]),
		Custom("MEDIUM", "", perms: ["royalty"]),
		Custom("DEAD", "", perms: ["royalty"]),
		Custom("FAE", "", perms: ["royalty"]),
		Custom("CHIMERA", "", perms: ["royalty"]),
		Custom("PEERING", "", perms: ["royalty"]),
		.. Enumerable.Range(0, 10).Select(i => Custom($"MARKER{i}", "", perms: ["wizard"]))
	];

	private static PennMUSHFlagDefinition Custom(string name, string letter, string[]? types = null,
		string[]? perms = null, string[]? aliases = null) => new()
		{
			Name = name,
			Letter = letter,
			Types = [.. types ?? []],
			SetPermissions = [.. perms ?? []],
			UnsetPermissions = [.. perms ?? []],
			Aliases = [.. aliases ?? []]
		};

	/// <summary>Where a power bit goes: a SharpMUSH power, a permission scope, or a custom power.</summary>
	public sealed record PowerMapping(string Source, string? Power = null, string? Permission = null);

	/// <summary>The first power word, by bit.</summary>
	public static readonly FrozenDictionary<uint, PowerMapping> Powers1 = new Dictionary<uint, PowerMapping>
	{
		[0x1] = new("quota", "Quotas"),
		[0x2] = new("chown_anything", "chown_anything"),
		[0x4] = new("announce", "Announce"),
		[0x8] = new("boot", "Boot"),
		[0x10] = new("halt", "Halt"),
		[0x20] = new("control_all", Permission: "control.all"),
		[0x40] = new("expanded_who", "expanded_who"),
		[0x80] = new("see_all", "See_All"),
		[0x100] = new("find_unfindable", "find_unfindable"),
		[0x200] = new("free_money", "No_Pay"),
		[0x400] = new("free_quota", "No_Quota"),
		[0x800] = new("hide", "Hide"),
		[0x1000] = new("idle", "Idle"),
		[0x2000] = new("search", "Search"),
		[0x4000] = new("long_fingers", "Long_Fingers"),
		[0x8000] = new("prog", "prog"),
		[0x10000] = new("siteadmin", Permission: "server.operate"),
		[0x20000] = new("link_anywhere", "Link_Anywhere"),
		[0x40000] = new("dark", "CAN_DARK"),
		[0x80000] = new("comm_all", "Chat_Privs"),
		[0x100000] = new("see_queue", "See_Queue"),
		[0x200000] = new("see_hidden", "see_hidden"),
		[0x400000] = new("monitor", "monitor"),
		[0x800000] = new("poll", "Poll"),
		[0x1000000] = new("no_destroy", "no_destroy"),
		[0x2000000] = new("guest", "Guest"),
		[0x4000000] = new("pass_locks", "pass_locks"),
		[0x8000000] = new("stat_any", "stat_any"),
		[0x10000000] = new("steal_money", "DEBIT"),
		[0x20000000] = new("tel_anywhere", "Tport_Anywhere"),
		[0x40000000] = new("tel_anything", "Tport_Anything"),
		[0x80000000] = new("unkillable", "Unkillable"),
	}.ToFrozenDictionary();

	/// <summary>The second power word, by bit.</summary>
	public static readonly FrozenDictionary<uint, PowerMapping> Powers2 = new Dictionary<uint, PowerMapping>
	{
		[0x1] = new("builder", "Builder"),
		[0x2] = new("no_mail_expire", "no_mail_expire"),
	}.ToFrozenDictionary();

	/// <summary>
	/// The TinyMUX powers SharpMUSH has no counterpart for, created as custom powers so objects keep them
	/// and <c>haspower()</c> still answers. None of them changes what the engine does.
	/// </summary>
	public static readonly PennMUSHFlagDefinition[] CustomPowers =
	[
		.. new[] { "chown_anything", "expanded_who", "find_unfindable", "prog", "see_hidden", "monitor", "no_destroy",
				"pass_locks", "stat_any", "no_mail_expire" }
			.Select(name => Custom(name, "", perms: ["wizard"]))
	];

	/// <summary>What an attribute flag bit becomes: SharpMUSH attribute flag names, or why it is dropped.</summary>
	public sealed record AttributeFlagMapping(string Source, string[] Flags, string? Note = null);

	/// <summary>The attribute flag bits (<c>AF_*</c>) that carry over, exactly or approximately.</summary>
	public static readonly FrozenDictionary<int, AttributeFlagMapping> AttributeFlags = new Dictionary<int, AttributeFlagMapping>
	{
		[0x1] = new("odark", [], "dropped: SharpMUSH has no flag that hides only the o-message"),
		[0x2] = new("dark", ["mortal_dark"], "imported as mortal_dark, which wizards can still see"),
		[0x4] = new("wizard", ["wizard"]),
		[0x8] = new("mdark", ["mortal_dark"]),
		[0x10] = new("internal", ["internal"]),
		[0x20] = new("nocmd", [], "dropped: SharpMUSH has no flag that keeps an attribute from being set by command"),
		[0x40] = new("lock", ["locked"]),
		[0x100] = new("noprog", ["no_command"]),
		[0x200] = new("god", ["wizard"], "imported as wizard: SharpMUSH has no God-only attribute flag"),
		[0x800] = new("visual", ["visual"]),
		[0x1000] = new("private", ["no_inherit"]),
		[0x2000] = new("html", [], "dropped: SharpMUSH has no HTML attribute flag"),
		[0x4000] = new("noparse", [], "dropped: SharpMUSH has no flag that stops evaluation of a $-command's text"),
		[0x8000] = new("regexp", ["regexp"]),
		[0x10000] = new("noclone", ["no_clone"]),
		[0x20000] = new("const", ["locked", "wizard"], "imported as locked and wizard: SharpMUSH has no unchangeable attribute"),
		[0x40000] = new("case", ["case"]),
		[0x80000] = new("trace", ["debug"]),
		[0x100000] = new("noeval", [], "dropped: SharpMUSH has no flag that stops an attribute from being evaluated"),
		[0x400000] = new("noname", ["noname"]),
		[0x800000] = new("nodecomp", [], "dropped: SharpMUSH has no flag that hides an attribute from @decompile"),
	}.ToFrozenDictionary();

	/// <summary><c>AF_DELETED</c>: the attribute is gone.</summary>
	public const int AttributeDeleted = 0x80;

	/// <summary><c>AF_IS_LOCK</c>: the value is a lock key.</summary>
	public const int AttributeIsLock = 0x400;

	/// <summary>Attribute numbers the reader lifts into something other than an attribute.</summary>
	public const int PasswordAttribute = 5;
	public const int MoneyAttribute = 25;
	public const int RemainingQuotaAttribute = 38;
	public const int DefaultLockAttribute = 42;
	public const int NameAttribute = 43;
	public const int QuotaAttribute = 49;
	public const int AliasAttribute = 58;
	public const int EnterLockAttribute = 59;
	public const int UseLockAttribute = 62;
	public const int PrivilegesAttribute = 83;
	public const int CreatedAttribute = 218;
	public const int ModifiedAttribute = 219;
	public const int ListAttribute = 253;

	/// <summary>The first user-named attribute number (<c>A_USER_START</c>).</summary>
	public const int FirstUserAttribute = 256;

	/// <summary>
	/// Built-in attributes that hold server state rather than anything a game wrote, by number, with
	/// why they are not imported.
	/// </summary>
	public static readonly FrozenDictionary<int, string> DroppedAttributes = new Dictionary<int, string>
	{
		[MoneyAttribute] = "money, kept by TinyMUX in an attribute",
		[NameAttribute] = "the object's name, read from the record instead",
		[QuotaAttribute] = "the quota TinyMUX tracks itself; RQUOTA carries the remainder",
		[PrivilegesAttribute] = "per-object privileges TinyMUX never used",
		[198] = "@icmd state, which SharpMUSH has no counterpart for",
		[205] = "a mail message being written (@mail/to)",
		[206] = "a mail message being written (@mail/to)",
		[207] = "a mail message being written (@mail/to)",
		[210] = "@prog state",
		[213] = "the creation count TinyMUX keeps for itself",
		[250] = "reality levels, which SharpMUSH does not have",
		[252] = "an internal attribute list",
		[ListAttribute] = "the legacy packed attribute list",
		[254] = "an internal structure definition",
		[255] = "temporary server data",
	}.ToFrozenDictionary();

	/// <summary>
	/// TinyMUX's built-in attributes by number, under the name SharpMUSH gives the same attribute. Most
	/// are TinyMUX's name in capitals; the rest are SharpMUSH's (PennMUSH's) spelling of the same thing.
	/// </summary>
	public static readonly FrozenDictionary<int, string> Attributes = BuildAttributes();

	private static FrozenDictionary<int, string> BuildAttributes()
	{
		var table = new Dictionary<int, string>
		{
			[1] = "OSUCCESS", [2] = "OFAILURE", [3] = "FAILURE", [4] = "SUCCESS", [6] = "DESCRIBE", [7] = "SEX",
			[8] = "ODROP", [9] = "DROP", [10] = "ODEATH", [11] = "DEATH", [12] = "ASUCCESS", [13] = "AFAILURE",
			[14] = "ADROP", [15] = "ADEATH", [16] = "AUSE", [17] = "CHARGES", [18] = "RUNOUT", [19] = "STARTUP",
			[20] = "ACLONE", [21] = "APAYMENT", [22] = "OPAYMENT", [23] = "PAYMENT", [24] = "COST", [26] = "LISTEN",
			[27] = "AAHEAR", [28] = "AMHEAR", [29] = "AHEAR", [30] = "LAST", [31] = "QUEUEMAX", [32] = "IDESCRIBE",
			[33] = "ENTER", [34] = "OXENTER", [35] = "AENTER", [36] = "ADESCRIBE", [37] = "ODESCRIBE",
			[RemainingQuotaAttribute] = "RQUOTA", [39] = "ACONNECT", [40] = "ADISCONNECT", [41] = "ALLOWANCE",
			[44] = "COMMENT", [45] = "USE", [46] = "OUSE", [47] = "SEMAPHORE", [48] = "TIMEOUT", [50] = "LEAVE",
			[51] = "OLEAVE", [52] = "ALEAVE", [53] = "OENTER", [54] = "OXLEAVE", [55] = "MOVE", [56] = "OMOVE",
			[57] = "AMOVE", [AliasAttribute] = "ALIAS", [64] = "EALIAS", [65] = "LALIAS", [66] = "EFAIL",
			[67] = "OEFAIL", [68] = "AEFAIL", [69] = "LFAIL", [70] = "OLFAIL", [71] = "ALFAIL", [72] = "HAVEN",
			[73] = "AWAY", [74] = "IDLE", [75] = "UFAIL", [76] = "OUFAIL", [77] = "AUFAIL", [78] = "PFAIL",
			[79] = "TPORT", [80] = "OTPORT", [81] = "OXTPORT", [82] = "ATPORT", [84] = "LOGINDATA", [88] = "LASTSITE",
			[89] = "INPREFIX", [90] = "PREFIX", [91] = "INFILTER", [92] = "FILTER", [95] = "FORWARDLIST",
			[96] = "MAILFOLDERS", [128] = "MFAIL", [129] = "GFAIL", [130] = "OGFAIL", [131] = "AGFAIL",
			[132] = "RFAIL", [133] = "ORFAIL", [134] = "ARFAIL", [135] = "DFAIL", [136] = "ODFAIL", [137] = "ADFAIL",
			[138] = "TFAIL", [139] = "OTFAIL", [140] = "ATFAIL", [141] = "TOFAIL", [142] = "OTOFAIL",
			[143] = "ATOFAIL", [144] = "LASTIP", [145] = "UMBRADESC", [146] = "WRAITHDESC", [147] = "FAEDESC",
			[148] = "MATRIXDESC", [149] = "COMJOIN", [150] = "COMLEAVE", [151] = "COMON", [152] = "COMOFF",
			[199] = "MONIKER", [200] = "LASTPAGED", [201] = "MAILSUCC", [202] = "AMAIL", [203] = "MAILSIGNATURE",
			[204] = "DAILY", [208] = "MAILCURF", [211] = "MAILFLAGS", [212] = "DESTROYER", [214] = "SAYSTRING",
			[215] = "SPEECHMOD", [216] = "EXITTO", [220] = "VRML_URL", [221] = "HTDESC", [222] = "REASON",
			[224] = "CONNINFO", [227] = "LASTWHISPER", [228] = "ADESTROY", [229] = "APARENT", [230] = "ACREATE",
			[232] = "PRONOUN", [233] = "GMCP", [234] = "PROTECTNAME", [240] = "IDLETIMEOUT", [241] = "EXITFORMAT",
			[242] = "CONFORMAT", [243] = "NAMEFORMAT", [244] = "DESCFORMAT",
		};

		for (var i = 0; i < 26; i++)
		{
			table[100 + i] = $"V{(char)('A' + i)}";
		}

		return table.ToFrozenDictionary();
	}

	/// <summary>
	/// TinyMUX's own names for the built-in attributes SharpMUSH spells differently, for a lock key or
	/// softcode that names one (<c>Desc:*foo*</c>).
	/// </summary>
	public static readonly FrozenDictionary<string, string> AttributeRenames = new Dictionary<string, string>
	{
		["DESC"] = "DESCRIBE", ["ADESC"] = "ADESCRIBE", ["ODESC"] = "ODESCRIBE", ["IDESC"] = "IDESCRIBE",
		["SUCC"] = "SUCCESS", ["OSUCC"] = "OSUCCESS", ["ASUCC"] = "ASUCCESS", ["FAIL"] = "FAILURE",
		["OFAIL"] = "OFAILURE", ["AFAIL"] = "AFAILURE", ["SIGNATURE"] = "MAILSIGNATURE", ["PAY"] = "PAYMENT",
		["OPAY"] = "OPAYMENT", ["APAY"] = "APAYMENT", ["LASTPAGE"] = "LASTPAGED", ["KILL"] = "DEATH",
		["OKILL"] = "ODEATH", ["AKILL"] = "ADEATH", ["REJECT"] = "HAVEN",
	}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>The SharpMUSH name of an attribute a TinyMUX lock key or definition names.</summary>
	public static string AttributeName(string muxName)
		=> AttributeRenames.GetValueOrDefault(muxName, muxName.ToUpperInvariant());

	/// <summary>
	/// A lock attribute, by number: the SharpMUSH lock it becomes, and whether that is a user-defined lock
	/// (one SharpMUSH stores and evaluates on request, but no built-in check reads).
	/// </summary>
	public sealed record LockMapping(string MuxName, string Lock, bool UserDefined = false);

	public static readonly FrozenDictionary<int, LockMapping> Locks = new Dictionary<int, LockMapping>
	{
		[DefaultLockAttribute] = new("DefaultLock", "Basic"),
		[EnterLockAttribute] = new("EnterLock", "Enter"),
		[60] = new("LeaveLock", "Leave"),
		[61] = new("PageLock", "Page"),
		[UseLockAttribute] = new("UseLock", "Use"),
		[63] = new("GiveLock", "Give"),
		[85] = new("TportLock", "Teleport"),
		[86] = new("DropLock", "Drop"),
		[87] = new("ReceiveLock", "Receive"),
		[93] = new("LinkLock", "Link"),
		[94] = new("TeloutLock", "Telout", UserDefined: true),
		[97] = new("UserLock", "User", UserDefined: true),
		[98] = new("ParentLock", "Parent"),
		[99] = new("ControlLock", "ControlLock", UserDefined: true),
		[127] = new("GetFromLock", "Take"),
		[209] = new("SpeechLock", "Speech"),
		[217] = new("ChownLock", "ChOwn"),
		[225] = new("MailLock", "Mail"),
		[226] = new("OpenLock", "Open"),
		[231] = new("VisibleLock", "Visible", UserDefined: true),
	}.ToFrozenDictionary();

	/// <summary>The SpeechLock attribute, which TinyMUX checks only in an AUDITORIUM.</summary>
	public const int SpeechLockAttribute = 209;

	/// <summary>
	/// TinyMUX's <c>@lock</c> switch names (<c>lock_sw</c>), which an indirect lock (<c>@#5/enterlock</c>)
	/// names its lock by, under the SharpMUSH lock name.
	/// </summary>
	public static readonly FrozenDictionary<string, string> LockSwitches = new Dictionary<string, string>
	{
		["defaultlock"] = "Basic", ["droplock"] = "Drop", ["enterlock"] = "Enter", ["getfromlock"] = "Take",
		["givelock"] = "Give", ["leavelock"] = "Leave", ["linklock"] = "Link", ["maillock"] = "Mail",
		["openlock"] = "Open", ["pagelock"] = "Page", ["parentlock"] = "Parent", ["receivelock"] = "Receive",
		["speechlock"] = "Speech", ["teloutlock"] = "Telout", ["tportlock"] = "Teleport", ["uselock"] = "Use",
		["userlock"] = "User", ["visiblelock"] = "Visible",
	}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>The SharpMUSH lock an indirect lock's switch names: a full name, or any unique prefix of one.</summary>
	public static string? LockSwitch(string name)
	{
		if (LockSwitches.TryGetValue(name, out var exact)) return exact;
		var matches = LockSwitches.Where(entry => entry.Key.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToArray();
		return matches.Length == 1 ? matches[0].Value : null;
	}
}
