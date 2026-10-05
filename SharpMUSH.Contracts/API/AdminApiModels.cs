using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.API;

/// <summary>One character in the admin character list (<c>GET api/admin/characters</c>).</summary>
/// <param name="DbrefNumber">The character's dbref number.</param>
/// <param name="CreationTime">Its creation time, so a stale list cannot act on a recycled number.</param>
/// <param name="Name">Its name.</param>
/// <param name="AccountKey">The owning account's key (the id without <c>node_accounts/</c>), or null when unlinked.</param>
/// <param name="AccountName">The owning account's username, or null when unlinked.</param>
/// <param name="Online">Whether it has a logged-in connection now.</param>
/// <param name="Flags">Its flag names, space-separated.</param>
/// <param name="LastConnect">Its <c>LAST</c> attribute, as the game wrote it, or null before its first connect.</param>
public sealed record AdminCharacterRow(
	int DbrefNumber,
	long CreationTime,
	string Name,
	string? AccountKey,
	string? AccountName,
	bool Online,
	string Flags,
	string? LastConnect);

/// <summary>One page of the admin character list, after the filters.</summary>
/// <param name="Characters">The page's rows, by name.</param>
/// <param name="Total">How many characters matched the filters in all.</param>
public sealed record AdminCharacterPage(IReadOnlyList<AdminCharacterRow> Characters, int Total);

/// <summary>One character's detail (<c>GET api/admin/characters/{dbref}</c>).</summary>
/// <param name="Character">The list row.</param>
/// <param name="Created">When it was created.</param>
/// <param name="AttributeCount">How many attributes it holds itself, nested ones included.</param>
/// <param name="MailTotal">Messages in its inbox, every folder.</param>
/// <param name="MailUnread">Of those, unread.</param>
/// <param name="LastLogout">Its <c>LASTLOGOUT</c> attribute, or null.</param>
/// <param name="LastSite">Its <c>LASTSITE</c>; only for <c>server.admin</c>, null otherwise.</param>
/// <param name="LastIp">Its <c>LASTIP</c>; only for <c>server.admin</c>, null otherwise.</param>
/// <param name="Roles">The roles it holds itself, by slug.</param>
/// <param name="Connections">Its live connections' descriptors.</param>
public sealed record AdminCharacterDetail(
	AdminCharacterRow Character,
	DateTimeOffset Created,
	int AttributeCount,
	int MailTotal,
	int MailUnread,
	string? LastLogout,
	string? LastSite,
	string? LastIp,
	IReadOnlyList<string> Roles,
	IReadOnlyList<long> Connections);

/// <summary>The audit log's filter choices (<c>GET api/admin/audit/actions</c>).</summary>
public sealed record AuditActionsResponse(IReadOnlyList<string> Actions);
