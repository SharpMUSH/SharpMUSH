namespace SharpMUSH.Database.Seed;

/// <summary>
/// The built-in object flags, shared verbatim across all database providers. Copied from
/// <c>SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs</c> (<c>CreateInitialFlags</c>) so a new
/// provider's seed migration reads the same array instead of re-typing it.
/// </summary>
public static class FlagSeed
{
	public static readonly (string Name, string Symbol, string[]? Aliases, string[] SetPerms, string[] UnsetPerms, string[] TypeRestrictions)[] Flags =
	[
		("WIZARD", "W", null, ["trusted","wizard","log"], ["trusted","wizard"], ["ROOM","PLAYER","EXIT","THING"]),
		("ABODE", "A", null, [], [], ["ROOM"]),
		("APPROVED", "+", null, ["royalty"], ["royalty"], ["PLAYER"]),
		("ANSI", "A", null, [], [], ["PLAYER"]),
		("CHOWN_OK", "C", null, [], [], ["ROOM","PLAYER","THING"]),
		("COLOR", "C", ["COLOUR"], [], [], ["PLAYER"]),
		("DARK", "D", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("FIXED", "F", null, ["wizard"], ["wizard"], ["PLAYER"]),
		("FLOATING", "F", null, [], [], ["ROOM"]),
		("HAVEN", "H", null, [], [], ["PLAYER"]),
		("TRUST", "I", ["INHERIT"], ["trusted"], ["trusted"], ["ROOM","PLAYER","EXIT","THING"]),
		("JUDGE", "J", null, ["royalty"], ["royalty"], ["PLAYER"]),
		("JUMP_OK", "J", ["TEL-OK","TEL_OK","TELOK"], [], [], ["ROOM"]),
		("LINK_OK", "L", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("MONITOR", "M", ["LISTENER","WATCHER"], [], [], ["ROOM","PLAYER","THING"]),
		("NO_LEAVE", "N", ["NOLEAVE"], [], [], ["THING"]),
		("NO_TEL", "N", null, [], [], ["ROOM"]),
		("OPAQUE", "O", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("QUIET", "Q", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("UNFINDABLE", "U", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("VISUAL", "V", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("SAFE", "X", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("SHARED", "Z", ["ZONE"], [], [], ["PLAYER"]),
		("Z_TEL", "Z", null, [], [], ["ROOM"]),
		("LISTEN_PARENT", "^", ["^"], [], [], ["PLAYER"]),
		("NOACCENTS", "~", null, [], [], ["PLAYER"]),
		("UNREGISTERED", "?", null, ["royalty"], ["royalty"], ["PLAYER"]),
		("NOSPOOF", "\"", null, ["odark"], ["odark"], ["ROOM","PLAYER","EXIT","THING"]),
		("AUDIBLE", "a", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("DEBUG", "b", ["TRACE"], [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("DESTROY_OK", "d", ["DEST_OK"], [], [], ["THING"]),
		("ENTER_OK", "e", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("GAGGED", "g", null, ["wizard"], ["wizard"], ["PLAYER"]),
		("HALT", "h", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("ORPHAN", "i", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("JURY_OK", "j", ["JURYOK"], ["royalty"], ["royalty"], ["PLAYER"]),
		("KEEPALIVE", "k", null, [], [], ["PLAYER"]),
		("LIGHT", "l", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("MISTRUST", "m", null, ["trusted"], ["trusted"], ["THING","EXIT","ROOM"]),
		// MYOPIC shares MISTRUST's letter and is told apart from it by object type, as in PennMUSH
		// (hdrs/flag_tab.h:51, src/flags.c:778). Symbols are not unique here — see ABODE/ANSI on 'A'.
		("MYOPIC", "m", null, [], [], ["PLAYER"]),
		("NO_COMMAND", "n", ["NOCOMMAND"], [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("ON_VACATION", "o", ["ONVACATION","ON-VACATION"], [], [], ["PLAYER"]),
		("PUPPET", "P", null, [], [], ["THING"]),
		("ROYALTY", "r", null, ["trusted","royalty","log"], ["trusted","royalty"], ["ROOM","PLAYER","EXIT","THING"]),
		("SUSPECT", "s", null, ["wizard","mdark","log"], ["wizard","mdark"], ["ROOM","PLAYER","EXIT","THING"]),
		("TRANSPARENT", "t", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("VERBOSE", "v", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("NO_WARN", "w", ["NOWARN"], [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("CLOUDY", "x", ["TERSE"], [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("CHAN_USEFIRSTMATCH", "", ["CHAN_FIRSTMATCH","CHAN_MATCHFIRST"], ["trusted"], ["trusted"], ["ROOM","PLAYER","EXIT","THING"]),
		("HEAR_CONNECT", "", null, ["royalty"], [], ["ROOM","PLAYER","EXIT","THING"]),
		("HEAVY", "", null, ["royalty"], [], ["ROOM","PLAYER","EXIT","THING"]),
		("LOUD", "", null, ["royalty"], [], ["ROOM","PLAYER","EXIT","THING"]),
		("NO_LOG", "", null, ["wizard","mdark","log"], ["wizard","mdark"], ["ROOM","PLAYER","EXIT","THING"]),
		("PARANOID", "", null, ["odark"], ["odark"], ["ROOM","PLAYER","EXIT","THING"]),
		("TRACK_MONEY", "", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
		("XTERM256", "", ["XTERM","COLOR256"], [], [], ["PLAYER"]),
		("TRUECOLOR", "", ["TRUECOLOUR","RGB","24BIT"], [], [], ["PLAYER"]),
		("MONIKER", "", null, ["royalty"], ["royalty"], ["ROOM","PLAYER","EXIT","THING"]),
		("OPEN_OK", "", null, [], [], ["ROOM"]),
		("GOING", "g", null, ["wizard"], ["wizard"], ["ROOM","PLAYER","EXIT","THING"]),
		("GOING_TWICE", "", null, ["wizard"], ["wizard"], ["ROOM","PLAYER","EXIT","THING"]),
	];
}
