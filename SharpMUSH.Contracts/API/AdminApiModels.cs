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

/// <summary>A banned account (<c>GET api/admin/bans</c>).</summary>
/// <param name="AccountKey">The account's key (the id without <c>node_accounts/</c>).</param>
/// <param name="AccountName">Its username.</param>
/// <param name="Characters">The characters linked to it, by name.</param>
/// <param name="Reason">The reason staff gave.</param>
/// <param name="BannedBy">The username of the staff account that banned it, when known.</param>
/// <param name="At">When it was banned.</param>
/// <param name="ExpiresAt">When the ban lifts by itself, or null for a ban that stays until lifted.</param>
public sealed record AdminBanRow(
	string AccountKey,
	string AccountName,
	IReadOnlyList<string> Characters,
	string Reason,
	string? BannedBy,
	DateTimeOffset At,
	DateTimeOffset? ExpiresAt);

/// <summary>A sitelock rule, as the moderation page lists it: a host pattern and its access words.</summary>
public sealed record AdminHostRuleRow(string Pattern, IReadOnlyList<string> Rules);

/// <summary>Every account ban, and the sitelock's host rules for reference (<c>GET api/admin/bans</c>).</summary>
public sealed record AdminBansResponse(IReadOnlyList<AdminBanRow> Bans, IReadOnlyList<AdminHostRuleRow> Hosts);

/// <summary>Bans an account (<c>POST api/admin/bans</c>).</summary>
/// <param name="AccountKey">The account's key.</param>
/// <param name="Reason">Why; required, kept with the ban and in the audit log.</param>
/// <param name="ExpiresAt">When the ban lifts by itself, or null to keep it until it is lifted.</param>
public sealed record AdminBanRequest(string AccountKey, string Reason, DateTimeOffset? ExpiresAt);

/// <summary>Warns a character (<c>POST api/admin/characters/{dbref}/warn</c>).</summary>
/// <param name="Reason">Why; required, passed to the game's <c>PLAYER`WARN</c> handler and kept in the audit log.</param>
public sealed record AdminWarnRequest(string Reason);
