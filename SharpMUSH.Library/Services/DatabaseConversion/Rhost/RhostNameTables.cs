using System.Collections.Frozen;
using SharpMUSH.Configuration;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.Services.DatabaseConversion.Rhost;

/// <summary>
/// What one RhostMUSH flag or toggle bit becomes here. Any of the parts may be empty; all of them empty
/// with a <see cref="Skip"/> reason means the bit is not imported, and says why.
/// </summary>
/// <param name="Word">Which word holds it: 0-3 the flag words, 4-5 the first two toggle words.</param>
/// <param name="Bit">The bit.</param>
/// <param name="Name">RhostMUSH's name for it.</param>
/// <param name="Flags">The SharpMUSH flags it sets, which for WIZARD and ROYALTY is a role.</param>
/// <param name="Powers">The SharpMUSH powers it grants, which for Builder and Guest is a role.</param>
/// <param name="Custom">The flag of its own it becomes, created by the import; none when null.</param>
/// <param name="StaffOnly">Whether that custom flag is wizard-set, rather than anyone's.</param>
/// <param name="Skip">Why it is not imported, when nothing else is given.</param>
/// <param name="Role">The staff-level role it gives, one of <see cref="BuiltInRoles.RhostLevels"/>; none when null.</param>
public sealed record RhostBitMapping(
	int Word,
	uint Bit,
	string Name,
	string[] Flags,
	string[] Powers,
	string? Custom = null,
	bool StaffOnly = false,
	string? Skip = null,
	string? Role = null);

/// <summary>A RhostMUSH power or depower: its 2-bit level field in one of the toggle words.</summary>
/// <param name="Word">The toggle word, 0-based (2-4 powers, 5-7 depowers).</param>
/// <param name="Shift">Where its two bits start.</param>
/// <param name="Name">RhostMUSH's name for it.</param>
/// <param name="Scopes">
/// The permissions it lands on: an allow for a power, a deny for a depower. None means it is not imported, for
/// <paramref name="Skip"/>.
/// </param>
/// <param name="Ranked">
/// Whether its level bounds whose things it acts on, which the import carries as a reach (<c>help @permission reach</c>).
/// </param>
/// <param name="Skip">Why it is not imported, when it lands on nothing.</param>
public sealed record RhostPowerMapping(int Word, int Shift, string Name, string[] Scopes, bool Ranked, string? Skip = null);

/// <summary>A RhostMUSH attribute flag (<c>AF_*</c>) and the SharpMUSH attribute flag it becomes, if any.</summary>
public sealed record RhostAttributeFlagMapping(uint Bit, string Name, string? Flag, string? Note = null);

/// <summary>
/// RhostMUSH's built-in names, by number or bit, and what each becomes in SharpMUSH. The names are
/// RhostMUSH's own, as its <c>@list</c> output and flatfiles show them; the decisions are this importer's.
/// </summary>
public static class RhostNameTables
{
	private static RhostBitMapping Same(int word, uint bit, string name) => new(word, bit, name, [name], []);

	private static RhostBitMapping Flag(int word, uint bit, string name, string flag) => new(word, bit, name, [flag], []);

	private static RhostBitMapping Power(int word, uint bit, string name, string power) => new(word, bit, name, [], [power]);

	private static RhostBitMapping Custom(int word, uint bit, string name, bool staffOnly = false)
		=> new(word, bit, name, [], [], name, staffOnly);

	private static RhostBitMapping Skip(int word, uint bit, string name, string reason)
		=> new(word, bit, name, [], [], Skip: reason);

	private const string ServerState = "server state RhostMUSH keeps for itself";

	/// <summary>Every flag and toggle bit RhostMUSH defines, and its fate.</summary>
	public static readonly IReadOnlyList<RhostBitMapping> Bits =
	[
		// Flag word 1. The low three bits are the type.
		Flag(0, 0x8, "SEETHRU", "TRANSPARENT"),
		// The bit is WIZARD in the source and displays as ROYALTY: RhostMUSH's wizard level.
		Flag(0, 0x10, "ROYALTY", "WIZARD"),
		Same(0, 0x20, "LINK_OK"),
		Same(0, 0x40, "DARK"),
		Same(0, 0x80, "JUMP_OK"),
		Same(0, 0x100, "STICKY"),
		Same(0, 0x200, "DESTROY_OK"),
		Same(0, 0x400, "HAVEN"),
		Same(0, 0x800, "QUIET"),
		Same(0, 0x1000, "HALT"),
		Flag(0, 0x2000, "TRACE", "DEBUG"),
		Skip(0, 0x4000, "GOING", "an object being destroyed; RhostMUSH finishes it on the next purge"),
		Same(0, 0x8000, "MONITOR"),
		Same(0, 0x10000, "MYOPIC"),
		Same(0, 0x20000, "PUPPET"),
		Same(0, 0x40000, "CHOWN_OK"),
		Same(0, 0x80000, "ENTER_OK"),
		Same(0, 0x100000, "VISUAL"),
		// RhostMUSH's level above its wizards: a role of its own, and a flag of its own so softcode that asks still finds it.
		new(0, 0x200000, "IMMORTAL", [], [], "IMMORTAL", true, Role: "immortal"),
		Skip(0, 0x400000, "HAS_STARTUP", ServerState + " (it has a STARTUP attribute)"),
		Same(0, 0x800000, "OPAQUE"),
		Same(0, 0x1000000, "VERBOSE"),
		Flag(0, 0x2000000, "INHERIT", "TRUST"),
		Same(0, 0x4000000, "NOSPOOF"),
		Custom(0, 0x8000000, "ROBOT"),
		Same(0, 0x10000000, "SAFE"),
		Custom(0, 0x20000000, "CONTROL_OK"),
		Flag(0, 0x40000000, "HEARTHRU", "AUDIBLE"),
		Same(0, 0x80000000, "TERSE"),

		// Flag word 2.
		Custom(1, 0x1, "KEY"),
		Same(1, 0x2, "ABODE"),
		Same(1, 0x4, "FLOATING"),
		Same(1, 0x8, "UNFINDABLE"),
		Custom(1, 0x10, "PARENT_OK"),
		Same(1, 0x20, "LIGHT"),
		Skip(1, 0x40, "HAS_LISTEN", ServerState + " (it has a LISTEN attribute)"),
		Skip(1, 0x80, "HAS_FWDLIST", ServerState + " (it has a FORWARDLIST attribute)"),
		// RhostMUSH's councilor level, between its wizards and architects: a role of its own, and a flag of its own.
		new(1, 0x100, "COUNCILOR", [], [], "COUNCILOR", true, Role: "councilor"),
		Custom(1, 0x200, "GUILDOBJ", true),
		new(1, 0x400, "GUILDMASTER", [], [], "GUILDMASTER", true, Role: "guildmaster"),
		Custom(1, 0x800, "NO_WALLS"),
		Custom(1, 0x1000, "REQUIRE_TREES"),
		Custom(1, 0x4000, "SCLOAK", true),
		Custom(1, 0x8000, "CLOAK", true),
		Custom(1, 0x10000, "FUBAR", true),
		Flag(1, 0x20000, "INDESTRUCTABLE", "SAFE"),
		Custom(1, 0x40000, "NO_YELL", true),
		Same(1, 0x80000, "NO_TEL"),
		Power(1, 0x100000, "FREE", "No_Pay"),
		Power(1, 0x200000, "GUEST", "Guest"),
		Custom(1, 0x400000, "RECOVER", true),
		Custom(1, 0x800000, "BYEROOM"),
		Custom(1, 0x1000000, "WANDERER"),
		Same(1, 0x2000000, "ANSI"),
		Flag(1, 0x4000000, "ANSICOLOR", "COLOR"),
		Custom(1, 0x8000000, "NOFLASH"),
		Same(1, 0x10000000, "SUSPECT"),
		// RhostMUSH's architect level: a role of its own, with the builder's permissions.
		new(1, 0x20000000, "ARCHITECT", [], [], Role: "architect"),
		Skip(1, 0x40000000, "CONNECTED", "session state, cleared on every load"),
		Custom(1, 0x80000000, "SLAVE", true),

		// Flag word 3.
		Custom(2, 0x1, "NOCONNECT", true),
		Skip(2, 0x2, "DPSHIFT", "read to decide the levels of its depowers"),
		Custom(2, 0x4, "NOPOSSESS"),
		Custom(2, 0x8, "COMBAT"),
		Custom(2, 0x10, "IC"),
		// Any object may be a zone here; the zone lists say which ones are.
		Skip(2, 0x20, "ZONEMASTER", "SharpMUSH needs no flag to make an object a zone; its members keep it as their zone"),
		Custom(2, 0x40, "ALTQUOTA", true),
		Custom(2, 0x80, "NOEXAMINE", true),
		Custom(2, 0x100, "NOMODIFY", true),
		Skip(2, 0x200, "CMDCHECK", ServerState + " (it has a CMDCHECK attribute)"),
		Custom(2, 0x400, "DOORED"),
		Custom(2, 0x800, "PRIVATE"),
		Custom(2, 0x1000, "NOMOVE", true),
		Custom(2, 0x2000, "STOP"),
		Custom(2, 0x4000, "NOSTOP", true),
		Flag(2, 0x8000, "NOCOMMAND", "NO_COMMAND"),
		Custom(2, 0x10000, "AUDITORIUM"),
		Custom(2, 0x20000, "SEE_OEMIT"),
		Custom(2, 0x40000, "NO_GOBJ", true),
		Custom(2, 0x80000, "NO_PESTER"),
		Custom(2, 0x100000, "LRFLAG"),
		Flag(2, 0x200000, "TELOK", "JUMP_OK"),
		Custom(2, 0x400000, "NO_OVERRIDE", true),
		Custom(2, 0x800000, "NO_USELOCK"),
		Skip(2, 0x1000000, "DR_PURGE", ServerState + " (queued for the purge)"),
		Custom(2, 0x2000000, "NO_ANSINAME", true),
		Custom(2, 0x4000000, "SPOOF", true),
		Same(2, 0x8000000, "SIDEFX"),
		Custom(2, 0x10000000, "ZONECONTENTS"),
		Custom(2, 0x20000000, "NOWHO", true),
		Custom(2, 0x40000000, "ANONYMOUS"),
		Custom(2, 0x80000000, "BACKSTAGE", true),

		// Flag word 4.
		Custom(3, 0x1, "NOBACKSTAGE", true),
		Power(3, 0x2, "LOGIN", "Login"),
		Skip(3, 0x4, "INPROGRAM", "session state (inside @program)"),
		Custom(3, 0x8, "COMMANDS"),
		Custom(3, 0x10, "MARKER0", true),
		Custom(3, 0x20, "MARKER1", true),
		Custom(3, 0x40, "MARKER2", true),
		Custom(3, 0x80, "MARKER3", true),
		Custom(3, 0x100, "MARKER4", true),
		Custom(3, 0x200, "MARKER5", true),
		Custom(3, 0x400, "MARKER6", true),
		Custom(3, 0x800, "MARKER7", true),
		Custom(3, 0x1000, "MARKER8", true),
		Custom(3, 0x2000, "MARKER9", true),
		Custom(3, 0x4000, "BOUNCE"),
		Custom(3, 0x8000, "SHOWFAILCMD"),
		Custom(3, 0x10000, "NOUNDERLINE"),
		Custom(3, 0x20000, "NONAME"),
		Custom(3, 0x40000, "ZONEPARENT"),
		Custom(3, 0x80000, "SPAMMONITOR", true),
		Custom(3, 0x100000, "BLIND"),
		Custom(3, 0x200000, "NOCODE", true),
		Skip(3, 0x400000, "HAS_PROTECT", ServerState + " (it has protected names)"),
		Flag(3, 0x800000, "XTERMCOLOR", "XTERM256"),
		Skip(3, 0x1000000, "HAS_ATTRPIPE", ServerState + " (an attribute pipe is open)"),
		Skip(3, 0x2000000, "HAS_OBJECTTAG", ServerState + " (it has object tags)"),
		Custom(3, 0x4000000, "COLORMAIL"),
		Same(3, 0x8000000, "TRUECOLOR"),
	];

	/// <summary>The toggle names, word 1 and 2, of every toggle bit; those not in <see cref="Bits"/> are dropped.</summary>
	public static readonly IReadOnlyList<(int Word, uint Bit, string Name)> ToggleNames =
	[
		.. Named(4, ["MONITOR", "MONITOR_USERID", "MONITOR_SITE", "MONITOR_STATS", "MONITOR_FAIL", "MONITOR_CONN",
			"VANILLA_ERRORS", "NO_ANSI_EX", "CPUTIME", "MONITOR_DISREASON", "MONITOR_VLIMIT", "NOTIFY_LINK", "MONITOR_AREG",
			"MONITOR_TIME", "CLUSTER", "SNUFFDARK", "NO_ANSI_PLAYER", "NO_ANSI_THING", "NO_ANSI_ROOM", "NO_ANSI_EXIT",
			"NO_TIMESTAMP", "NO_FORMAT", "ZONE_AUTOADD", "ZONE_AUTOADDALL", "WIELDED", "WORN", "SEE_SUSPECT", "MONITOR_CPU",
			"BRANDY_MAIL", "FORCEHALTED", "PROG", "NOSHPROG"]),
		.. Named(5, ["EXTANSI", "IMMPROG", "MONITOR_BAD", "PROG_ON_CONNECT", "MAIL_STRIPRETURN", "PENN_MAIL", "SILENTEFFECT",
			"IGNOREZONE", "VPAGE", "PAGELOCK", "MAIL_NOPARSE", "MAIL_LOCKDOWN", "MUXPAGE", "NOZONEPARENT", "ATRUSE", "VARIABLE",
			"KEEPALIVE", "CHKREALITY", "NOISY", "ZONECMDCHK", "HIDEIDLE", "MORTALREALITY", "ACCENTS", "MAILVALIDATE", "SAFELOG",
			"UTF8", null, "NODEFAULT", "EXFULLWIZATTR", "LOGROOMENH", "LOGROOM", "NOGLOBPARENT"]),
	];

	/// <summary>
	/// The toggle bits that carry over. Most toggles are a player's own settings for RhostMUSH features
	/// SharpMUSH does not have; those are reported, not turned into flags no code would read.
	/// </summary>
	public static readonly IReadOnlyList<RhostBitMapping> ToggleBits =
	[
		Flag(4, 0x1, "MONITOR", "HEAR_CONNECT"),
		Same(5, 0x10000, "KEEPALIVE"),
	];

	private static IEnumerable<(int Word, uint Bit, string Name)> Named(int word, string?[] names)
		=> names.Select((name, index) => (word, 1u << index, name)).Where(t => t.name is not null)
			.Select(t => (t.word, t.Item2, t.name!));

	private const string NeverChecked = "RhostMUSH never checks it";
	private const string NoApi = "SharpMUSH has no API listener for it to open";
	private const string NoMoney = "SharpMUSH does not track money";
	private const string AnsiNames = "anyone may colour a name here";
	private const string AllCommands = "SharpMUSH takes $-commands away only all together, with player.commands";

	private static string Power(string name) => PortalPermission.GamePower(name);

	/// <summary>A power or depower that acts the same whatever its level.</summary>
	private static (string, string[], bool, string?) Flat(string name, params string[] scopes) => (name, scopes, false, null);

	/// <summary>A power or depower whose level bounds whose things it acts on.</summary>
	private static (string, string[], bool, string?) Ranked(string name, params string[] scopes) => (name, scopes, true, null);

	private static (string, string[], bool, string?) Dropped(string name, string reason) => (name, [], false, reason);

	/// <summary>Every power (toggle words 3-5), at its level field, and the permissions it allows.</summary>
	public static readonly IReadOnlyList<RhostPowerMapping> Powers =
	[
		.. Fields(2, [
			Flat("CHANGE_QUOTAS", Power("Quotas")), Ranked("CHOWN_ME", PortalPermission.ChownSelf),
			Flat("CHOWN_ANYWHERE", PortalPermission.ChownSelf), Ranked("CHOWN_OTHER", PortalPermission.ChownAny),
			Ranked("WIZ_WHO", PortalPermission.PlayersView), Ranked("EXAMINE_ALL", Power("See_All")),
			Ranked("NOFORCE", PortalPermission.NoForce), Flat("SEE_QUEUE_ALL", Power("See_Queue")), Flat("FREE_QUOTA", Power("No_Quota")),
			Ranked("GRAB_PLAYER", Power("Tport_Anything")), Ranked("JOIN_PLAYER", Power("Tport_Anywhere")),
			Flat("LONG_FINGERS", Power("Long_Fingers")), Ranked("NO_BOOT", PortalPermission.NoBoot), Flat("BOOT", Power("Boot")),
			Ranked("STEAL", Power("DEBIT")), Ranked("SEE_QUEUE", Power("See_Queue"))]),
		.. Fields(3, [
			Flat("SHUTDOWN", PortalPermission.ServerOperate), Ranked("TEL_ANYWHERE", Power("Tport_Anywhere")),
			Ranked("TEL_ANYTHING", Power("Tport_Anything")), Flat("PCREATE", Power("Player_Create")), Ranked("STAT_ANY", Power("Search")),
			Flat("FREE_WALL", Power("No_Pay")), Dropped("EXECSCRIPT", NeverChecked), Flat("FREE_PAGE", Power("No_Pay")),
			Ranked("HALT_QUEUE", Power("Halt")), Flat("HALT_QUEUE_ALL", Power("Halt")), Dropped("FORMATTING", NeverChecked),
			Ranked("NOKILL", Power("Unkillable")), Ranked("SEARCH_ANY", Power("Search")), Ranked("SECURITY", PortalPermission.FlagsRestricted),
			Ranked("WHO_UNFIND", PortalPermission.PlayersView), Ranked("WRAITH", PortalPermission.OverrideMovement)]),
		.. Fields(4, [
			Flat("PURGE", PortalPermission.ServerOperate), Dropped("HIDEBIT", "SharpMUSH cannot hide the roles a player holds"),
			Flat("NOWHO", Power("Hide")), Flat("FULLTEL", Power("Tport_Anywhere")), Flat("EXAMINE_FULL", Power("See_All")),
			Dropped("API", NoApi), Dropped("MONITORAPI", NoApi), Flat("WIZ_IDLE", Power("Idle")), Ranked("WIZ_SPOOF", Power("Can_spoof")),
			Flat("USE_FREELIST", Power("PICK_DBREFS")), Dropped("ANSINAME", AnsiNames)]),
	];

	/// <summary>Every depower (toggle words 6-8), at its level field, and the permissions it denies.</summary>
	public static readonly IReadOnlyList<RhostPowerMapping> Depowers =
	[
		.. Fields(5, [
			Flat("WALL", Power("Announce")), Dropped("LONG_FINGERS", NeverChecked), Ranked("STEAL", Power("DEBIT")),
			Flat("CREATE", PortalPermission.PlayerCreate), Ranked("WIZ_WHO", PortalPermission.PlayersView),
			Flat("CLOAK", Power("Hide"), Power("CAN_DARK")), Ranked("BOOT", Power("Boot")), Ranked("PAGE", PortalPermission.PlayerPage),
			Ranked("FORCE", PortalPermission.PlayerForce), Ranked("LOCKS", PortalPermission.Override), Flat("COM", PortalPermission.PlayerChat),
			Flat("COMMAND", PortalPermission.PlayerCommands), Dropped("MASTER", AllCommands),
			Ranked("EXAMINE", PortalPermission.PlayerExamine, Power("See_All")), Ranked("NUKE", PortalPermission.Nuke),
			Flat("FREE", Power("No_Pay"))]),
		.. Fields(6, [
			Flat("OVERRIDE", PortalPermission.Override), Ranked("TEL_ANYWHERE", PortalPermission.PlayerTeleport, Power("Tport_Anywhere")),
			Ranked("TEL_ANYTHING", PortalPermission.PlayerTeleport, Power("Tport_Anything")), Flat("PCREATE", Power("Player_Create")),
			Flat("POWER", PortalPermission.RolesAdmin),
			Dropped("QUOTA", "@quota reads players.moderate here, which covers all moderation; deny it by hand to keep this"),
			Ranked("MODIFY", PortalPermission.PlayerModify), Ranked("CHOWN_ME", PortalPermission.PlayerChown, PortalPermission.ChownSelf),
			Ranked("CHOWN_OTHER", PortalPermission.ChownAny), Dropped("ABUSE", AllCommands), Flat("UNL_QUOTA", Power("No_Quota")),
			Ranked("SEARCH_ANY", Power("Search")), Flat("GIVE", PortalPermission.PlayerGive), Flat("RECEIVE", PortalPermission.PlayerReceive),
			Dropped("NOGOLD", NoMoney), Dropped("NOSTEAL", NoMoney)]),
		.. Fields(7, [
			Flat("PASSWORD", PortalPermission.PlayerPassword), Ranked("MORTAL_EXAMINE", PortalPermission.PlayerExamine),
			Dropped("PERSONAL_COMMAND", AllCommands), Dropped("ANSINAME", AnsiNames), Flat("DARK", Power("CAN_DARK"))]),
	];

	private static IEnumerable<RhostPowerMapping> Fields(int word, (string Name, string[] Scopes, bool Ranked, string? Skip)[] fields)
		=> fields.Select((field, index) => new RhostPowerMapping(word, index * 2, field.Name, field.Scopes, field.Ranked, field.Skip));

	/// <summary>The flag word 2 bits whose flags also take permissions away (<see cref="RestrictingFlags"/>).</summary>
	public static readonly IReadOnlyList<(uint Bit, string Name, IReadOnlyList<string> Denies)> RestrictingBits =
	[
		(0x10000, "FUBAR", RestrictingFlags.Fubar),
		(0x80000000, "SLAVE", RestrictingFlags.Slave),
	];

	/// <summary>The flag word 3 bit that changes how every depower's level is stored on its player.</summary>
	public const uint DepowerShift = 0x2;

	/// <summary>The staff levels a power or depower is set at, 1 to 3, as the roles that stand for them.</summary>
	public static readonly IReadOnlyList<string> LevelRoles = ["guildmaster", BuiltInRoles.ArchitectSlug, BuiltInRoles.CouncilorSlug];

	/// <summary>RhostMUSH's attribute flags.</summary>
	public static readonly IReadOnlyList<RhostAttributeFlagMapping> AttributeFlags =
	[
		// AF_ODARK is the default for nearly every attribute and means nothing on its own.
		new(0x1, "ODARK", null, ""),
		new(0x2, "DARK", "mortal_dark", "only God sees it in the source; wizards see it here"),
		new(0x4, "WIZARD", "wizard"),
		new(0x8, "MDARK", "mortal_dark"),
		new(0x10, "INTERNAL", null, ""),
		new(0x20, "NOCMD", "no_command"),
		new(0x40, "LOCK", "locked"),
		new(0x80, "DELETED", null, ""),
		new(0x100, "NOPROG", null, ""),
		new(0x200, "GOD", "wizard", "only God may set it in the source; wizards may here"),
		new(0x400, "ADMIN", "wizard", "councilors may set it in the source; only wizards here"),
		new(0x800, "BUILDER", "wizard", "architects may set it in the source; only wizards here"),
		new(0x1000, "IS_LOCK", null, ""),
		new(0x2000, "GUILDMASTER", "wizard", "guildmasters may set it in the source; only wizards here"),
		new(0x4000, "IMMORTAL", "wizard", "only immortals may set it in the source; wizards may here"),
		new(0x8000, "PRIVATE", "no_inherit"),
		new(0x10000, "NONBLOCKING", null),
		new(0x20000, "VISUAL", "visual"),
		new(0x40000, "NOANSI", null),
		new(0x80000, "PINVIS", "mortal_dark", "hidden from mortals in the source by the PINVIS rule; mortal_dark here"),
		new(0x100000, "NORETURN", null),
		new(0x200000, "NOCLONE", "no_clone"),
		new(0x400000, "NOPARSE", null),
		new(0x800000, "SAFE", "safe"),
		new(0x1000000, "USELOCK", null),
		new(0x2000000, "SINGLETHREAD", null),
		new(0x4000000, "DEFAULT", null),
		new(0x8000000, "ATRLOCK", null),
		new(0x10000000, "LOGGED", null),
		new(0x20000000, "REGEXP", "regexp"),
		new(0x40000000, "UNSAFE", null),
	];

	/// <summary>RhostMUSH's built-in attributes, by number, under RhostMUSH's names.</summary>
	public static readonly FrozenDictionary<int, string> BuiltInAttributes = BuildAttributes().ToFrozenDictionary();

	private static IEnumerable<KeyValuePair<int, string>> BuildAttributes()
	{
		string[] low =
		[
			"Osucc", "Ofail", "Fail", "Succ", "Password", "Desc", "Sex", "Odrop", "Drop", "Okill", "Kill", "Asucc", "Afail", "Adrop",
			"Akill", "Ause", "Charges", "Runout", "Startup", "Aclone", "Apay", "Opay", "Pay", "Cost", "Money", "Listen", "Aahear",
			"Amhear", "Ahear", "Last", "QueueMax", "Idesc", "Enter", "Oxenter", "Aenter", "Adesc", "Odesc", "Rquota", "Aconnect",
			"Adisconnect", "Allowance", "DefaultLock", "Name", "Comment", "Use", "Ouse", "Semaphore", "Timeout", "Quota", "Leave",
			"Oleave", "Aleave", "Oenter", "Oxleave", "Move", "Omove", "Amove", "Alias", "EnterLock", "LeaveLock", "PageLock",
			"UseLock", "GiveLock", "Ealias", "Lalias", "Efail", "Oefail", "Aefail", "Lfail", "Olfail", "Alfail", "Reject", "Away",
			"Idle", "Ufail", "Oufail", "Aufail", "Pfail", "Tport", "Otport", "Oxtport", "Atport", "Totems", "Logindata", "TportLock",
			"DropLock", "ReceiveLock", "Lastsite", "Inprefix", "Prefix", "Infilter", "Filter", "LinkLock", "TeloutLock",
			"Forwardlist", "ControlLock", "UserLock", "ParentLock", "lambda"
		];
		for (var i = 0; i < low.Length; i++)
		{
			yield return new(i + 1, low[i]);
		}

		for (var letter = 0; letter < 26; letter++)
		{
			yield return new(100 + letter, $"V{(char)('A' + letter)}");
			yield return new(129 + letter, $"Z{(char)('A' + letter)}");
		}

		(int, string)[] high =
		[
			(126, "Channels"), (127, "Guild"), (128, "FlagLevel"), (155, "BCCMail"), (156, "Email"), (157, "MailLock"),
			(158, "ShareLock"), (159, "Gfail"), (160, "Ogfail"), (161, "Agfail"), (162, "Rfail"), (163, "Orfail"), (164, "Arfail"),
			(165, "Dfail"), (166, "Odfail"), (167, "Adfail"), (168, "Tfail"), (169, "Otfail"), (170, "Atfail"), (171, "Tofail"),
			(172, "Otofail"), (174, "Atofail"), (175, "MailPass"), (176, "MailSet"), (177, "LastPage"), (178, "ReturnPage"),
			(179, "Rectime"), (180, "MailCurrent"), (181, "Mquota"), (182, "Lquota"), (183, "Tquota"), (184, "MailTime"),
			(185, "MailSMax"), (186, "MailSCur"), (187, "Ident"), (188, "ZoneWizLock"), (189, "ZoneToLock"), (190, "TwinkLock"),
			(191, "GoodSite"), (192, "BadSite"), (193, "MailSig"), (194, "Adesc2"), (195, "PayLim"), (196, "RsrvDesc2"),
			(197, "Race"), (198, "Cmdcheck"), (199, "SpeechLock"), (200, "Sfail"), (201, "Asfail"), (202, "Autoreg"),
			(203, "DarkLock"), (204, "STouch"), (205, "SATouch"), (206, "SOTouch"), (207, "SListen"), (208, "SAListen"),
			(209, "SOListen"), (210, "STaste"), (211, "SATaste"), (212, "SOTaste"), (213, "SSmell"), (214, "SASmell"),
			(215, "SOSmell"), (216, "DropToLock"), (217, "OpenLock"), (218, "ChownLock"), (219, "Caption"), (220, "AnsiName"),
			(221, "TotCmds"), (222, "LstCmds"), (223, "ReceiveLim"), (224, "ConFormat"), (225, "ExitFormat"),
			(226, "DarkExitFormat"), (227, "ModifiedStamp"), (228, "CreatedStamp"), (229, "AltName"), (230, "AltNameLock"),
			(231, "InvType"), (232, "TotCharIn"), (233, "TotCharOut"), (234, "GiveToLock"), (235, "GetFromLock"),
			(236, "SayString"), (237, "LastCreate"), (238, "SaveSendMail"), (239, "ProgBuffer"), (240, "ProgPrompt"),
			(241, "ProgPromptBuffer"), (242, "TempBuffer"), (243, "DestVattrMax"), (244, "RLevel"), (245, "NameFormat"),
			(246, "LastIP"), (247, "SpamProtect"), (248, "ExitTo"), (249, "ProtectName"), (250, "TitleCaption"), (251, "ObjectTag")
		];
		foreach (var (number, name) in high)
		{
			yield return new(number, name);
		}

		(int, string)[] inline =
		[
			(InlineStart, "ConnInfo"), (InlineStart + 1, "ConnRecord"), (InlineStart + 2, "MFail"), (InlineStart + 3, "WireFuncEval"),
			(InlineStart + 4, "WireQueueMax"), (InlineStart + 5, "AMail"), (InlineStart + 6, "LastWhisper")
		];
		foreach (var (number, name) in inline)
		{
			yield return new(number, name);
		}
	}

	/// <summary>
	/// <c>A_INLINE_START</c>/<c>A_INLINE_END</c>: RhostMUSH numbers its newer built-in attributes from here, far
	/// above the user attributes, and writes no <c>+A</c> header for them.
	/// </summary>
	public const int InlineStart = 2100000000;

	public const int InlineEnd = 2140000000;

	/// <summary>Attribute numbers with a field of their own in the import, not an attribute.</summary>
	public const int PasswordAttribute = 5;

	public const int AliasAttribute = 58;
	public const int CreatedAttribute = 228;
	public const int RealityAttribute = 244;
	public const int TotemsAttribute = 83;
	public const int TagsAttribute = 251;

	/// <summary>
	/// The totems RhostMUSH defines itself, all in the tenth totem word, and whether only wizards set each. API_LUA,
	/// which lets an object reach RhostMUSH's Lua interface, is not here: SharpMUSH has none.
	/// </summary>
	public static readonly IReadOnlyList<(TotemDirective Totem, bool StaffOnly)> BuiltInTotems =
	[
		.. Enumerable.Range(0, 10).Select(i => (new TotemDirective($"MARKER{i}", 9, 1u << i), true)),
		(new TotemDirective("PROMPT", 9, 0x800), true),
		(new TotemDirective("SETQLABEL", 9, 0x80000000), false)
	];

	/// <summary>The user attribute that lists a cluster's objects, the first of them the one it was made on.</summary>
	public const string ClusterAttribute = "_CLUSTER";

	private const string ClusterState = "the cluster's bookkeeping; the import merges a cluster into its first object";
	public const int ModifiedAttribute = 227;
	public const int NameAttribute = 43;

	/// <summary>
	/// The built-in attributes that are not imported, and why: RhostMUSH's own bookkeeping, or a value a
	/// field of the import already carries.
	/// </summary>
	public static readonly FrozenDictionary<int, string> SkippedAttributes = new Dictionary<int, string>
	{
		[5] = "the password, imported as the player's password",
		[25] = "the money, which SharpMUSH does not track",
		[43] = "the name, imported as the name",
		[49] = "the quota in use, which SharpMUSH counts itself; RQUOTA sets the limit",
		[78] = ServerState, [TotemsAttribute] = "the totems, imported as flags", [84] = ServerState + " (login history)", [99] = ServerState,
		[128] = ServerState, [155] = ServerState, [175] = ServerState + " (mail password)", [176] = ServerState,
		[179] = ServerState, [180] = ServerState, [181] = ServerState + " (mail quota)", [182] = ServerState + " (mail quota)",
		[183] = ServerState + " (mail quota)", [184] = ServerState, [185] = ServerState, [186] = ServerState,
		[221] = ServerState + " (command counts)", [222] = ServerState + " (command counts)",
		[227] = "the modification time, imported as the object's", [228] = "the creation time, imported as the object's",
		[232] = ServerState + " (traffic counts)", [233] = ServerState + " (traffic counts)", [237] = ServerState,
		[238] = ServerState, [239] = ServerState, [241] = ServerState, [242] = ServerState, [243] = ServerState,
		[RealityAttribute] = "the reality levels, imported as the object's reality layers", [247] = ServerState, [249] = ServerState,
		[TagsAttribute] = "the tags that name the object, imported as tags",
		[InlineStart] = ServerState + " (connection counts)", [InlineStart + 1] = ServerState + " (connection record)",
		[InlineStart + 4] = ServerState, [InlineStart + 6] = ServerState,
	}.ToFrozenDictionary();

	/// <summary>User attributes RhostMUSH writes for itself, by name.</summary>
	public static readonly FrozenDictionary<string, string> SkippedUserAttributes = new Dictionary<string, string>
	{
		["__OBJID_INTERNAL"] = ServerState + " (the object's creation stamp, which SharpMUSH's objid carries)",
		[ClusterAttribute] = ClusterState, ["_CLUSTER_THRESH"] = ClusterState, ["_CLUSTER_ACTION"] = ClusterState,
		["_CLUSTER_ACTION_FUNC"] = ClusterState,
	}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Built-in attributes imported under another name: PennMUSH's for the same thing, which SharpMUSH reads.
	/// Any other built-in keeps its RhostMUSH name, upper-cased.
	/// </summary>
	public static readonly FrozenDictionary<int, string> RenamedAttributes = new Dictionary<int, string>
	{
		[1] = "OSUCCESS", [2] = "OFAILURE", [3] = "FAILURE", [4] = "SUCCESS", [6] = "DESCRIBE", [12] = "ASUCCESS",
		[13] = "AFAILURE", [21] = "APAYMENT", [22] = "OPAYMENT", [23] = "PAYMENT", [32] = "IDESCRIBE", [36] = "ADESCRIBE",
		[37] = "ODESCRIBE", [156] = "REGISTERED_EMAIL", [177] = "LASTPAGED", [193] = "MAILSIGNATURE",
		[220] = "MONIKER", [InlineStart + 3] = LiveWire.AttributeName,
		// The lock-failure messages, under the names SharpMUSH's failed locks read (LockMessages). Tofail and
		// its siblings keep their names: SharpMUSH has no teleport-out lock for them to hang on.
		[InlineStart + 2] = "MAIL_LOCK`FAILURE", [159] = "GIVE_LOCK`FAILURE",
		[160] = "GIVE_LOCK`OFAILURE", [161] = "GIVE_LOCK`AFAILURE", [162] = "RECEIVE_LOCK`FAILURE",
		[163] = "RECEIVE_LOCK`OFAILURE", [164] = "RECEIVE_LOCK`AFAILURE", [165] = "DROP_LOCK`FAILURE",
		[166] = "DROP_LOCK`OFAILURE", [167] = "DROP_LOCK`AFAILURE", [168] = "TELEPORT_LOCK`FAILURE",
		[169] = "TELEPORT_LOCK`OFAILURE", [170] = "TELEPORT_LOCK`AFAILURE",
	}.ToFrozenDictionary();

	/// <summary>The built-in lock attributes that become SharpMUSH locks, under SharpMUSH's lock names.</summary>
	public static readonly FrozenDictionary<int, string> LockAttributes = new Dictionary<int, string>
	{
		[42] = "Basic", [59] = "Enter", [60] = "Leave", [61] = "Page", [62] = "Use", [63] = "Give", [85] = "Teleport",
		[86] = "Drop", [87] = "Receive", [93] = "Link", [96] = "Control", [98] = "Parent", [157] = "Mail", [188] = "Zone",
		[189] = "ChZone", [199] = "Speech", [216] = "DropTo", [217] = "Open", [218] = "ChOwn",
	}.ToFrozenDictionary();

	/// <summary>
	/// The lock attributes SharpMUSH has no lock for. They are kept as attributes under their own names, so
	/// softcode reading them still finds them, and reported.
	/// </summary>
	public static readonly FrozenDictionary<int, string> UnmatchedLockAttributes = new Dictionary<int, string>
	{
		[94] = "who may teleport out", [97] = "who may use its $-commands as a user", [158] = "who shares it",
		[190] = "twink lock", [203] = "who sees it while it is DARK", [230] = "who sees its ALTNAME",
		[234] = "what it may be given to", [235] = "who may take from it",
	}.ToFrozenDictionary();
}
