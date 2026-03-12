using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	#region Migration

	public async ValueTask Migrate(CancellationToken cancellationToken = default)
	{
		if (_migrated) return;
		await MigrateLock.WaitAsync(cancellationToken);
		try
		{
			if (_migrated) return;
			logger.LogInformation("Migrating Memgraph Database");

			// Storage mode IN_MEMORY_ANALYTICAL is set via container startup flags
			// (--storage-mode=IN_MEMORY_ANALYTICAL) to avoid MVCC transaction conflicts.

			// Create indexes (Memgraph uses CREATE INDEX ON syntax)
			var indexQueries = new[]
			{
"CREATE INDEX ON :Object(key)",
"CREATE INDEX ON :Object(type)",
"CREATE INDEX ON :Object(name)",
"CREATE INDEX ON :Player(key)",
"CREATE INDEX ON :Room(key)",
"CREATE INDEX ON :Thing(key)",
"CREATE INDEX ON :Exit(key)",
"CREATE INDEX ON :Attribute(key)",
"CREATE INDEX ON :ObjectFlag(name)",
"CREATE INDEX ON :Power(name)",
"CREATE INDEX ON :AttributeFlag(name)",
"CREATE INDEX ON :AttributeEntry(name)",
"CREATE INDEX ON :Channel(name)",
"CREATE INDEX ON :Counter(name)"
};

			foreach (var q in indexQueries)
			{
				try { await ExecuteWithRetryAsync(q, ct: cancellationToken); }
				catch { /* Index may already exist */ }
			}

			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			// Create Counter for auto-increment object keys
			await ExecuteWithRetryAsync("MERGE (c:Counter {name: 'object_key'}) ON CREATE SET c.value = 2", ct: cancellationToken);

			// Create Room Zero (key=0)
			await ExecuteWithRetryAsync("""
MERGE (o:Object {key: 0})
ON CREATE SET o.name = 'Room Zero', o.type = 'ROOM', o.creationTime = $now, o.modifiedTime = $now, o.locks = '{}', o.warnings = 0
MERGE (r:Room {key: 0})
ON CREATE SET r.aliases = []
MERGE (r)-[:IS_OBJECT]->(o)
""", new { now }, cancellationToken);

			// Create Player One - God (key=1)
			await ExecuteWithRetryAsync("""
MERGE (o:Object {key: 1})
ON CREATE SET o.name = 'God', o.type = 'PLAYER', o.creationTime = $now, o.modifiedTime = $now, o.locks = '{}', o.warnings = 0
MERGE (p:Player {key: 1})
ON CREATE SET p.passwordHash = '', p.passwordSalt = '', p.aliases = [], p.quota = 999999
MERGE (p)-[:IS_OBJECT]->(o)
""", new { now }, cancellationToken);

			// Create Room Two - Master Room (key=2)
			await ExecuteWithRetryAsync("""
MERGE (o:Object {key: 2})
ON CREATE SET o.name = 'Master Room', o.type = 'ROOM', o.creationTime = $now, o.modifiedTime = $now, o.locks = '{}', o.warnings = 0
MERGE (r:Room {key: 2})
ON CREATE SET r.aliases = []
MERGE (r)-[:IS_OBJECT]->(o)
""", new { now }, cancellationToken);

			// Player One at Room Zero
			await ExecuteWithRetryAsync("""
MATCH (p:Player {key: 1}), (r:Room {key: 0})
MERGE (p)-[:AT_LOCATION]->(r)
""", ct: cancellationToken);

			// Player One home is Room Zero
			await ExecuteWithRetryAsync("""
MATCH (p:Player {key: 1}), (r:Room {key: 0})
MERGE (p)-[:HAS_HOME]->(r)
""", ct: cancellationToken);

			// Ownership: Object nodes -> Player typed node
			await ExecuteWithRetryAsync("""
MATCH (ownerPlayer:Player {key: 1})
MATCH (o:Object) WHERE o.key IN [0, 1, 2]
MERGE (o)-[:HAS_OWNER]->(ownerPlayer)
""", ct: cancellationToken);

			// Create initial flags
			await CreateInitialFlags(cancellationToken);

			// Create initial attribute flags
			await CreateInitialAttributeFlags(cancellationToken);

			// Create initial powers
			await CreateInitialPowers(cancellationToken);

			// Create initial attribute entries
			await CreateInitialAttributeEntries(cancellationToken);

			// Give Player One the WIZARD flag
			await ExecuteWithRetryAsync("""
MATCH (o:Object {key: 1}), (f:ObjectFlag {name: 'WIZARD'})
MERGE (o)-[:HAS_FLAG]->(f)
""", ct: cancellationToken);

			logger.LogInformation("Memgraph Migration Completed");
			_migrated = true;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Memgraph Migration Failed");
			throw;
		}
		finally
		{
			MigrateLock.Release();
		}
	}

	private async Task CreateInitialFlags(CancellationToken ct)
	{
		var flags = new (string Name, string Symbol, string[]? Aliases, string[] SetPerms, string[] UnsetPerms, string[] TypeRestrictions)[]
		{
("WIZARD", "W", null, ["trusted","wizard","log"], ["trusted","wizard"], ["ROOM","PLAYER","EXIT","THING"]),
("ABODE", "A", null, [], [], ["ROOM"]),
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
("MISTRUST", "m", ["MYOPIC"], ["trusted"], ["trusted"], ["PLAYER","EXIT","THING"]),
("NO_COMMAND", "n", ["NOCOMMAND"], [], [], ["ROOM","PLAYER","EXIT","THING"]),
("ON_VACATION", "o", ["ONVACATION","ON-VACATION"], [], [], ["PLAYER"]),
("PUPPET", "P", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
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
("MONIKER", "", null, ["royalty"], ["royalty"], ["ROOM","PLAYER","EXIT","THING"]),
("OPEN_OK", "", null, [], [], ["ROOM"]),
("GOING", "g", null, ["wizard"], ["wizard"], ["ROOM","PLAYER","EXIT","THING"]),
("GOING_TWICE", "", null, ["wizard"], ["wizard"], ["ROOM","PLAYER","EXIT","THING"]),
		};

		foreach (var f in flags)
		{
			await ExecuteWithRetryAsync("""
MERGE (f:ObjectFlag {name: $name})
ON CREATE SET f.symbol = $symbol, f.system = true, f.disabled = false,
f.aliases = $aliases, f.setPermissions = $setPerms, f.unsetPermissions = $unsetPerms,
f.typeRestrictions = $typeRestrictions
""", new
			{
				name = f.Name,
				symbol = f.Symbol,
				aliases = f.Aliases ?? Array.Empty<string>(),
				setPerms = f.SetPerms,
				unsetPerms = f.UnsetPerms,
				typeRestrictions = f.TypeRestrictions
			}, ct);
		}
	}

	private async Task CreateInitialAttributeFlags(CancellationToken ct)
	{
		var attrFlags = new (string Name, string Symbol, bool Inheritable)[]
		{
("no_command", "$", true),
("no_inherit", "i", true),
("no_clone", "c", true),
("mortal_dark", "m", true),
("wizard", "w", true),
("veiled", "V", true),
("nearby", "n", true),
("locked", "+", true),
("safe", "S", true),
("visual", "v", false),
("public", "p", false),
("debug", "b", true),
("no_debug", "B", true),
("regexp", "R", false),
("case", "C", false),
("nospace", "s", true),
("noname", "N", true),
("aahear", "A", false),
("amhear", "M", false),
("quiet", "Q", false),
("branch", "`", false),
("prefixmatch", "", false),
		};

		foreach (var af in attrFlags)
		{
			await ExecuteWithRetryAsync("""
MERGE (f:AttributeFlag {name: $name})
ON CREATE SET f.symbol = $symbol, f.system = true, f.inheritable = $inheritable
""", new { name = af.Name, symbol = af.Symbol, inheritable = af.Inheritable }, ct);
		}
	}

	private async Task CreateInitialPowers(CancellationToken ct)
	{
		var powers = new (string Name, string Alias, string[] SetPerms, string[] UnsetPerms)[]
		{
("Announce", "", ["wizard","log"], ["wizard"]),
("Boot", "", ["wizard","log"], ["wizard"]),
("Builder", "", ["wizard","log"], ["wizard"]),
("Can_Dark", "", ["wizard","log"], []),
("Can_HTTP", "", ["wizard","log"], []),
("Can_Spoof", "", ["wizard","log"], ["wizard"]),
("Chat_Privs", "", ["wizard","log"], ["wizard"]),
("Debit", "", ["wizard","log"], []),
("Functions", "", ["wizard","log"], ["wizard"]),
("Guest", "", ["wizard","log"], ["wizard"]),
("Halt", "", ["wizard","log"], ["wizard"]),
("Hide", "", ["wizard","log"], ["wizard"]),
("Hook", "", ["wizard","log"], []),
("Idle", "", ["wizard","log"], ["wizard"]),
("Immortal", "", ["wizard","log"], ["wizard"]),
("Link_Anywhere", "", ["wizard","log"], ["wizard"]),
("Login", "", ["wizard","log"], ["wizard"]),
("Long_Fingers", "", ["wizard","log"], ["wizard"]),
("Many_Attribs", "", ["wizard","log"], []),
("No_Pay", "", ["wizard","log"], ["wizard"]),
("No_Quota", "", ["wizard","log"], ["wizard"]),
("Open_Anywhere", "", ["wizard","log"], ["wizard"]),
("Pemit_All", "", ["wizard","log"], ["wizard"]),
("Pick_DBRefs", "", ["wizard","log"], ["wizard"]),
("Player_Create", "", ["wizard","log"], ["wizard"]),
("Poll", "", ["wizard","log"], ["wizard"]),
("Pueblo_Send", "", ["wizard","log"], ["wizard"]),
("Queue", "", ["wizard","log"], ["wizard"]),
("Search", "", ["wizard","log"], ["wizard"]),
("See_All", "", ["wizard","log"], ["wizard"]),
("See_Queue", "", ["wizard","log"], ["wizard"]),
("See_OOB", "", ["wizard","log"], ["wizard"]),
("SQL_OK", "", ["wizard","log"], ["wizard"]),
("Tport_Anything", "", ["wizard","log"], ["wizard"]),
("Tport_Anywhere", "", ["wizard","log"], ["wizard"]),
("Unkillable", "", ["wizard","log"], ["wizard"]),
		};

		foreach (var p in powers)
		{
			await ExecuteWithRetryAsync("""
MERGE (p:Power {name: $name})
ON CREATE SET p.alias = $alias, p.system = true, p.disabled = false,
p.setPermissions = $setPerms, p.unsetPermissions = $unsetPerms,
p.typeRestrictions = $typeRestrictions
""", new
			{
				name = p.Name,
				alias = p.Alias,
				setPerms = p.SetPerms,
				unsetPerms = p.UnsetPerms,
				typeRestrictions = new[] { "ROOM", "PLAYER", "EXIT", "THING" }
			}, ct);
		}
	}

	private async Task CreateInitialAttributeEntries(CancellationToken ct)
	{
		var entries = new (string Name, string[] DefaultFlags)[]
		{
("AAHEAR", ["no_command","prefixmatch"]),
("ABUY", ["no_command","prefixmatch"]),
("ACLONE", ["no_command","prefixmatch"]),
("ACONNECT", ["no_command","prefixmatch"]),
("ADEATH", ["no_command","prefixmatch"]),
("ADESCRIBE", ["no_command","prefixmatch"]),
("ADESTROY", ["no_inherit","no_clone","wizard","prefixmatch"]),
("ADISCONNECT", ["no_command","prefixmatch"]),
("ADROP", ["no_command","prefixmatch"]),
("AEFAIL", ["no_command","prefixmatch"]),
("AENTER", ["no_command","prefixmatch"]),
("AFAILURE", ["no_command","prefixmatch"]),
("AFOLLOW", ["no_command","prefixmatch"]),
("AGIVE", ["no_command","prefixmatch"]),
("AHEAR", ["no_command","prefixmatch"]),
("AIDESCRIBE", ["no_command","prefixmatch"]),
("ALEAVE", ["no_command","prefixmatch"]),
("ALFAIL", ["no_command","prefixmatch"]),
("ALIAS", ["no_command","visual","prefixmatch"]),
("AMAIL", ["wizard","prefixmatch"]),
("AMHEAR", ["no_command","prefixmatch"]),
("AMOVE", ["no_command","prefixmatch"]),
("ANAME", ["no_command","prefixmatch"]),
("APAYMENT", ["no_command","prefixmatch"]),
("ARECEIVE", ["no_command","prefixmatch"]),
("ASUCCESS", ["no_command","prefixmatch"]),
("ATPORT", ["no_command","prefixmatch"]),
("AUFAIL", ["no_command","prefixmatch"]),
("AUNFOLLOW", ["no_command","prefixmatch"]),
("AUSE", ["no_command","prefixmatch"]),
("AWAY", ["no_command","prefixmatch"]),
("AZENTER", ["no_command","prefixmatch"]),
("AZLEAVE", ["no_command","prefixmatch"]),
("BUY", ["no_command","prefixmatch"]),
("CHANALIAS", ["no_command"]),
("CHARGES", ["no_command","prefixmatch"]),
("CHATFORMAT", ["no_command","prefixmatch"]),
("COMMENT", ["no_command","no_clone","wizard","mortal_dark","prefixmatch"]),
("CONFORMAT", ["no_command","prefixmatch"]),
("COST", ["no_command","prefixmatch"]),
("DEATH", ["no_command","prefixmatch"]),
("DEBUGFORWARDLIST", ["no_command","no_inherit","prefixmatch"]),
("DESCFORMAT", ["no_command","prefixmatch"]),
("DESCRIBE", ["no_command","visual","prefixmatch","public","nearby"]),
("DESTINATION", ["no_command"]),
("DOING", ["no_command","no_inherit","visual","public"]),
("DROP", ["no_command","prefixmatch"]),
("EALIAS", ["no_command","prefixmatch"]),
("EFAIL", ["no_command","prefixmatch"]),
("ENTER", ["no_command","prefixmatch"]),
("EXITFORMAT", ["no_command","prefixmatch"]),
("EXITTO", ["no_command","prefixmatch"]),
("FAILURE", ["no_command","prefixmatch"]),
("FILTER", ["no_command","prefixmatch"]),
("FOLLOW", ["no_command","prefixmatch"]),
("FOLLOWERS", ["no_command","no_inherit","no_clone","wizard","prefixmatch"]),
("FOLLOWING", ["no_command","no_inherit","no_clone","wizard","prefixmatch"]),
("FORWARDLIST", ["no_command","no_inherit","prefixmatch"]),
("GIVE", ["no_command","prefixmatch"]),
("HAVEN", ["no_command","prefixmatch"]),
("IDESCFORMAT", ["no_command","prefixmatch"]),
("IDESCRIBE", ["no_command","prefixmatch"]),
("IDLE", ["no_command","prefixmatch"]),
("INFILTER", ["no_command","prefixmatch"]),
("INPREFIX", ["no_command","prefixmatch"]),
("INVFORMAT", ["no_command","prefixmatch"]),
("LALIAS", ["no_command","prefixmatch"]),
("LAST", ["no_clone","wizard","visual","locked","prefixmatch"]),
("LASTFAILED", ["no_clone","wizard","locked","prefixmatch"]),
("LASTIP", ["no_clone","wizard","locked","prefixmatch"]),
("LASTLOGOUT", ["no_clone","wizard","locked","prefixmatch"]),
("LASTPAGED", ["no_clone","wizard","locked","prefixmatch"]),
("LASTSITE", ["no_clone","wizard","locked","prefixmatch"]),
("LEAVE", ["no_command","prefixmatch"]),
("LFAIL", ["no_command","prefixmatch"]),
("LISTEN", ["no_command","prefixmatch"]),
("MAILCURF", ["no_command","no_clone","wizard","locked","prefixmatch"]),
("MAILFILTER", ["no_command","prefixmatch"]),
("MAILFILTERS", ["no_command","no_clone","wizard","locked","prefixmatch"]),
("MAILFOLDERS", ["no_command","no_clone","wizard","locked","prefixmatch"]),
("MAILFORWARDLIST", ["no_command","prefixmatch"]),
("MAILQUOTA", ["no_command","no_clone","wizard","locked"]),
("MAILSIGNATURE", ["no_command","prefixmatch"]),
("MONIKER", ["no_command","wizard","visual","locked"]),
("MOVE", ["no_command","prefixmatch"]),
("NAMEACCENT", ["no_command","visual","prefixmatch"]),
("NAMEFORMAT", ["no_command","prefixmatch"]),
("OBUY", ["no_command","prefixmatch"]),
("ODEATH", ["no_command","prefixmatch"]),
("ODESCRIBE", ["no_command","prefixmatch"]),
("ODROP", ["no_command","prefixmatch"]),
("OEFAIL", ["no_command","prefixmatch"]),
("OENTER", ["no_command","prefixmatch"]),
("OFAILURE", ["no_command","prefixmatch"]),
("OFOLLOW", ["no_command","prefixmatch"]),
("OGIVE", ["no_command","prefixmatch"]),
("OIDESCRIBE", ["no_command","prefixmatch"]),
("OLEAVE", ["no_command","prefixmatch"]),
("OLFAIL", ["no_command","prefixmatch"]),
("OMOVE", ["no_command","prefixmatch"]),
("ONAME", ["no_command","prefixmatch"]),
("OPAYMENT", ["no_command","prefixmatch"]),
("ORECEIVE", ["no_command","prefixmatch"]),
("OSUCCESS", ["no_command","prefixmatch"]),
("OTPORT", ["no_command","prefixmatch"]),
("OUFAIL", ["no_command","prefixmatch"]),
("OUNFOLLOW", ["no_command","prefixmatch"]),
("OUSE", ["no_command","prefixmatch"]),
("OUTPAGEFORMAT", ["no_command","prefixmatch"]),
("OXENTER", ["no_command","prefixmatch"]),
("OXLEAVE", ["no_command","prefixmatch"]),
("OXMOVE", ["no_command","prefixmatch"]),
("OXTPORT", ["no_command","prefixmatch"]),
("OZENTER", ["no_command","prefixmatch"]),
("OZLEAVE", ["no_command","prefixmatch"]),
("PAGEFORMAT", ["no_command","prefixmatch"]),
("PAYMENT", ["no_command","prefixmatch"]),
("PREFIX", ["no_command","prefixmatch"]),
("PRICELIST", ["no_command","prefixmatch"]),
("QUEUE", ["no_inherit","no_clone","wizard"]),
("RECEIVE", ["no_command","prefixmatch"]),
("REGISTERED_EMAIL", ["no_inherit","no_clone","wizard","locked"]),
("RQUOTA", ["mortal_dark","locked"]),
("RUNOUT", ["no_command","prefixmatch"]),
("SEMAPHORE", ["no_inherit","no_clone","locked"]),
("SEX", ["no_command","visual","prefixmatch"]),
("SPEECHMOD", ["no_command","prefixmatch"]),
("STARTUP", ["no_command","prefixmatch"]),
("SUCCESS", ["no_command","prefixmatch"]),
("TFPREFIX", ["no_command","no_inherit","no_clone","prefixmatch"]),
("TPORT", ["no_command","prefixmatch"]),
("TZ", ["no_command","visual"]),
("UFAIL", ["no_command","prefixmatch"]),
("UNFOLLOW", ["no_command","prefixmatch"]),
("USE", ["no_command","prefixmatch"]),
("VA", []), ("VB", []), ("VC", []), ("VD", []), ("VE", []), ("VF", []),
("VG", []), ("VH", []), ("VI", []), ("VJ", []), ("VK", []), ("VL", []),
("VM", []), ("VN", []), ("VO", []), ("VP", []), ("VQ", []), ("VR", []),
("VRML_URL", ["no_command","prefixmatch"]),
("VS", []), ("VT", []), ("VU", []), ("VV", []), ("VW", []), ("VX", []),
("VY", []), ("VZ", []),
("WA", []), ("WB", []), ("WC", []), ("WD", []), ("WE", []), ("WF", []),
("WG", []), ("WH", []), ("WI", []), ("WJ", []), ("WK", []), ("WL", []),
("WM", []), ("WN", []), ("WO", []), ("WP", []), ("WQ", []), ("WR", []),
("WS", []), ("WT", []), ("WU", []), ("WV", []), ("WW", []), ("WX", []),
("WY", []), ("WZ", []),
("XA", []), ("XB", []), ("XC", []), ("XD", []), ("XE", []), ("XF", []),
("XG", []), ("XH", []), ("XI", []), ("XJ", []), ("XK", []), ("XL", []),
("XM", []), ("XN", []), ("XO", []), ("XP", []), ("XQ", []), ("XR", []),
("XS", []), ("XT", []), ("XU", []), ("XV", []), ("XW", []), ("XX", []),
("XY", []), ("XZ", []),
("ZENTER", ["no_command","prefixmatch"]),
		};

		foreach (var e in entries)
		{
			await ExecuteWithRetryAsync("""
MERGE (e:AttributeEntry {name: $name})
ON CREATE SET e.defaultFlags = $defaultFlags, e.lim = '', e.enumValues = []
""", new { name = e.Name, defaultFlags = e.DefaultFlags }, ct);
		}
	}

	#endregion
}
