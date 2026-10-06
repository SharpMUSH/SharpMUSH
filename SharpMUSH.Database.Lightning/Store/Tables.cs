using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// The schema. Every table the provider touches is declared here so the store can open them all at
/// startup and the delete cascade can enumerate the edge pairs. Names are stable on disk; renaming one
/// is a migration. Plugin-owned tables are not declared here at all: they are opened on first use through
/// the accessor and tracked by the store itself, so <see cref="All"/> stays the core schema.
/// </summary>
public static class Tables
{
	public static readonly TableDef Meta = TableDef.Node("meta");
	public static readonly TableDef Obj = TableDef.Node("obj");
	public static readonly TableDef ObjName = TableDef.Index("obj.name", duplicates: true, fixedDuplicates: true);
	/// <summary>Object type + 0x00 → dbref, so enumerating one type (every player) reads only that type's rows.</summary>
	public static readonly TableDef ObjType = TableDef.Index("obj.type", duplicates: true, fixedDuplicates: true);
	public static readonly TableDef AttrMeta = TableDef.Node("attr.meta");
	public static readonly TableDef AttrVal = TableDef.Node("attr.val");
	public static readonly TableDef AttrFlag = TableDef.Node("attr.flag");
	public static readonly TableDef AttrEntry = TableDef.Node("attr.entry");
	public static readonly TableDef Flag = TableDef.Node("flag");
	public static readonly TableDef Power = TableDef.Node("power");

	public static readonly (TableDef Forward, TableDef Reverse) Location = TableDef.Edge("loc", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Home = TableDef.Edge("home", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Owner = TableDef.Edge("owner", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Parent = TableDef.Edge("parent", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Zone = TableDef.Edge("zone", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Exit = TableDef.Edge("exit", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) ObjFlag = TableDef.Edge("flag", fixedDuplicates: false);
	public static readonly (TableDef Forward, TableDef Reverse) ObjPower = TableDef.Edge("power", fixedDuplicates: false);
	public static readonly (TableDef Forward, TableDef Reverse) AccountChar = TableDef.Edge("acct.char", fixedDuplicates: false);
	/// <summary>Object → the role slugs assigned to it; the reverse finds a role's holders.</summary>
	public static readonly (TableDef Forward, TableDef Reverse) ObjRole = TableDef.Edge("obj.role", fixedDuplicates: false);

	public static readonly TableDef RevLocation = Location.Reverse;

	public static readonly TableDef Chan = TableDef.Node("chan");
	public static readonly TableDef ChanMember = TableDef.Node("chan.member");
	public static readonly TableDef RevChanMember = TableDef.Index("r.chan.member", duplicates: true);
	public static readonly TableDef Mail = TableDef.Node("mail");
	public static readonly TableDef MailBox = TableDef.Index("mail.box");
	public static readonly TableDef MailSent = TableDef.Index("mail.sent");
	public static readonly TableDef MailCount = TableDef.Node("mail.count");
	/// <summary>Recipient + folder + 0x00 + mail id → empty: one folder of one mailbox as a contiguous, id-ordered range.</summary>
	public static readonly TableDef MailFolder = TableDef.Index("mail.folder");
	/// <summary>Sender + recipient + mail id → empty: what one sender sent one recipient, id-ordered.</summary>
	public static readonly TableDef MailSentTo = TableDef.Index("mail.sent.to");
	public static readonly TableDef MailAlias = TableDef.Node("malias");
	public static readonly TableDef Account = TableDef.Node("account");
	public static readonly TableDef AccountEmail = TableDef.Index("account.email");
	public static readonly TableDef AccountUser = TableDef.Index("account.user");
	public static readonly TableDef AccountRole = TableDef.Index("e.acct.role", duplicates: true);
	/// <summary>Account key → its per-account permission overrides (scope → state), one record per account.</summary>
	public static readonly TableDef AccountPermission = TableDef.Node("acct.perm");
	/// <summary>Account key → its ban (reason, who, when, until when); absent when the account is not banned.</summary>
	public static readonly TableDef AccountBan = TableDef.Node("acct.ban");
	/// <summary>SHA-256 of a passkey's credential id → the passkey; see <c>LightningDatabase.Passkeys.cs</c>.</summary>
	public static readonly TableDef AccountPasskey = TableDef.Node("acct.passkey");
	/// <summary>Account key → the credential-id hashes of its passkeys.</summary>
	public static readonly TableDef AccountPasskeyByAccount = TableDef.Index("acct.passkey.acct", duplicates: true);
	/// <summary>Object dbref → the permission overrides set on it (scope → state), one record per object.</summary>
	public static readonly TableDef ObjPermission = TableDef.Node("obj.perm");
	public static readonly TableDef Session = TableDef.Node("session");
	public static readonly TableDef SessionAccount = TableDef.Index("session.acct", duplicates: true);
	public static readonly TableDef SessionIp = TableDef.Index("session.ip", duplicates: true);
	/// <summary>Expiry-ordered session index: big-endian expiry millis + token → empty. The expired-session sweep reads its head.</summary>
	public static readonly TableDef SessionExpiry = TableDef.Index("session.exp");
	public static readonly TableDef ReadMarker = TableDef.Node("read.marker");
	public static readonly TableDef PageLog = TableDef.Node("page.log");
	public static readonly TableDef PageConversation = TableDef.Node("page.conv");
	public static readonly TableDef PageConversationLatest = TableDef.Index("page.conv.latest");
	public static readonly TableDef PageLogTime = TableDef.Index("page.time");
	/// <summary>Staff actions: big-endian millis + big-endian sequence → the entry, so the log reads newest first and a date range is one key range.</summary>
	public static readonly TableDef Audit = TableDef.Node("audit");
	public static readonly TableDef State = TableDef.Node("state");
	public static readonly TableDef ExpandedObj = TableDef.Node("x.obj");
	public static readonly TableDef ExpandedSrv = TableDef.Node("x.srv");
	public static readonly TableDef WikiPage = TableDef.Node("wiki.page");
	public static readonly TableDef WikiSlug = TableDef.Index("wiki.slug");
	public static readonly TableDef WikiRev = TableDef.Node("wiki.rev");
	public static readonly TableDef WikiTr = TableDef.Node("wiki.tr");
	/// <summary>Wiki list indexes; each value is the page's visibility (published byte + author), so a listing
	/// filters and pages without decoding a page row. See <c>LightningDatabase.Wiki.cs</c> for the key shapes.</summary>
	public static readonly TableDef WikiRecent = TableDef.Index("wiki.recent");
	public static readonly TableDef WikiByNamespace = TableDef.Index("wiki.ns");
	public static readonly TableDef WikiByCategory = TableDef.Index("wiki.cat");
	/// <summary>What a namespace, category or page requires, keyed by (scope, key). See <c>LightningDatabase.WikiRequirements.cs</c>.</summary>
	public static readonly TableDef WikiRequirement = TableDef.Node("wiki.req");
	public static readonly TableDef Layout = TableDef.Node("layout");
	public static readonly TableDef App = TableDef.Node("app");
	public static readonly TableDef Role = TableDef.Node("role");
	/// <summary>Custom permissions a game defines, keyed by scope.</summary>
	public static readonly TableDef CustomPermission = TableDef.Node("perm.def");
	/// <summary>Categories of roles and custom permissions, keyed by lowercased name.</summary>
	public static readonly TableDef RoleCategory = TableDef.Node("role.cat");
	public static readonly TableDef PermissionCategory = TableDef.Node("perm.cat");
	public static readonly TableDef Pkg = TableDef.Node("pkg");
	public static readonly TableDef PkgObj = TableDef.Node("pkg.obj");
	public static readonly TableDef PkgAttr = TableDef.Node("pkg.attr");
	public static readonly TableDef PkgStruct = TableDef.Node("pkg.struct");
	public static readonly TableDef PkgRemote = TableDef.Node("pkg.remote");
	public static readonly TableDef PkgRev = TableDef.Node("pkg.rev");
	public static readonly TableDef PkgDep = TableDef.Index("pkg.dep", duplicates: true);

	public static readonly IReadOnlyList<TableDef> All = typeof(Tables)
		.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
		.SelectMany(f => f.GetValue(null) switch
		{
			TableDef t => [t],
			ValueTuple<TableDef, TableDef> pair => new[] { pair.Item1, pair.Item2 },
			_ => Array.Empty<TableDef>()
		})
		.Distinct()
		.ToArray();

	/// <summary>Every forward/reverse pair; the object-delete cascade walks all of them.</summary>
	public static IEnumerable<(TableDef Forward, TableDef Reverse)> EdgePairs =>
		All.Where(t => t.Kind == TableKind.ForwardEdge).Select(t => (t, t.Pair!));
}
