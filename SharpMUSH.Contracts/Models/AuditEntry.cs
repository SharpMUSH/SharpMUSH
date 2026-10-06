using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

/// <summary>Where a staff action was taken. Written by name, so the API reads <c>"Portal"</c>, not <c>1</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuditSource>))]
public enum AuditSource
{
	/// <summary>In the game: a command run by a character or object.</summary>
	Game,

	/// <summary>In the web portal, by a signed-in account.</summary>
	Portal,

	/// <summary>By the server itself, on a schedule: a ban that ran out, for one.</summary>
	System
}

/// <summary>
/// Who took a staff action: the account (portal actions, and in-game actions by a character linked to
/// one) and the character or object that acted. Names are kept as they were at the time, so the entry
/// still reads after a rename or a destroy.
/// </summary>
/// <param name="AccountId">The acting account's id, when there is one.</param>
/// <param name="AccountName">That account's username.</param>
/// <param name="Objid">The acting character or object, by objid (<c>#12:1700000000000</c>).</param>
/// <param name="Name">The acting character's or object's name.</param>
public sealed record AuditActor(string? AccountId, string? AccountName, string? Objid, string? Name)
{
	/// <summary>The account name, else the character name, else the objid, for display and search.</summary>
	public string Display => AccountName ?? Name ?? Objid ?? "(unknown)";
}

/// <summary>What a staff action was taken on.</summary>
/// <param name="Kind">One of <see cref="AuditTargetKinds"/>.</param>
/// <param name="Id">Its stable identifier: an objid, an account id, a role slug, a host pattern, a setting name.</param>
/// <param name="Name">Its name when the action was taken.</param>
public sealed record AuditTarget(string Kind, string Id, string Name);

/// <summary>The kinds of thing a staff action can target.</summary>
public static class AuditTargetKinds
{
	public const string Account = "account";
	public const string Character = "character";
	public const string Object = "object";
	public const string Role = "role";
	public const string Permission = "permission";
	public const string Category = "category";
	public const string Host = "host";
	public const string Setting = "setting";
	public const string Name = "name";
	public const string Command = "command";
	public const string Function = "function";
}

/// <summary>
/// One staff action, as the audit log keeps it: who did what, to what, when, from where.
/// </summary>
/// <param name="Id">The entry's id. Entries sort by it newest last; it is opaque otherwise.</param>
/// <param name="At">When the action was taken.</param>
/// <param name="Action">One of <see cref="AuditActions"/>.</param>
/// <param name="Source">Where it was taken.</param>
/// <param name="Actor">Who took it.</param>
/// <param name="Target">What it was taken on, when it had a target.</param>
/// <param name="Details">Anything else that says what changed: a new status, a role slug, a setting's new value.</param>
public sealed record AuditEntry(
	string Id,
	DateTimeOffset At,
	string Action,
	AuditSource Source,
	AuditActor Actor,
	AuditTarget? Target,
	string? Details);

/// <summary>A staff action to record; the store gives it its id.</summary>
public sealed record AuditDraft(
	DateTimeOffset At,
	string Action,
	AuditSource Source,
	AuditActor Actor,
	AuditTarget? Target,
	string? Details);

/// <summary>
/// Which audit entries to read, newest first. Every filter is optional; a null one matches everything.
/// </summary>
/// <param name="Action">An exact action, or a prefix ending in <c>.</c> (<c>role.</c> matches every role action).</param>
/// <param name="Actor">Matches the actor's account name, character name or objid, ignoring case.</param>
/// <param name="From">The earliest time to include.</param>
/// <param name="To">The latest time to include.</param>
/// <param name="Text">Matches the target's name or id, or the details, ignoring case.</param>
/// <param name="Before">Continue a listing: only entries older than the entry with this id.</param>
/// <param name="Limit">At most this many entries.</param>
public sealed record AuditFilter(
	string? Action = null,
	string? Actor = null,
	DateTimeOffset? From = null,
	DateTimeOffset? To = null,
	string? Text = null,
	string? Before = null,
	int Limit = 50)
{
	/// <summary>The most entries one read returns, whatever <see cref="Limit"/> asks for.</summary>
	public const int MaxLimit = 500;
}

/// <summary>One page of a newest-first audit listing.</summary>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="Next">The <see cref="AuditFilter.Before"/> that reads the next page, or null at the end.</param>
public sealed record AuditPage(IReadOnlyList<AuditEntry> Entries, string? Next);

/// <summary>
/// The actions the audit log records. A name is <c>area.verb</c>, so a filter on <c>area.</c> finds an
/// area's actions. Names are stored: never rename one.
/// </summary>
public static class AuditActions
{
	public const string AccountStatus = "account.status";
	public const string AccountPassword = "account.password";
	public const string CharacterLink = "character.link";
	public const string CharacterUnlink = "character.unlink";
	public const string PlayerCreate = "player.create";
	public const string PlayerDestroy = "player.destroy";
	public const string PlayerPassword = "player.password";
	public const string PlayerBoot = "player.boot";
	public const string PlayerWarn = "player.warn";
	public const string BanAdd = "ban.add";
	public const string BanLift = "ban.lift";
	public const string BanExpired = "ban.expired";
	public const string RoleSave = "role.save";
	public const string RoleDelete = "role.delete";
	public const string RoleAssign = "role.assign";
	public const string RoleUnassign = "role.unassign";
	public const string RoleOverride = "role.override";
	public const string PermissionDefine = "permission.define";
	public const string PermissionRemove = "permission.remove";
	public const string CategorySave = "category.save";
	public const string CategoryRename = "category.rename";
	public const string CategoryDelete = "category.delete";
	public const string SitelockAdd = "sitelock.add";
	public const string SitelockRemove = "sitelock.remove";
	public const string BannedNameAdd = "bannedname.add";
	public const string BannedNameRemove = "bannedname.remove";
	public const string ConfigSet = "config.set";
	public const string ConfigImport = "config.import";
	public const string RestrictionSet = "restriction.set";
	public const string RestrictionClear = "restriction.clear";

	/// <summary>Every action, for the viewer's filter.</summary>
	public static readonly IReadOnlyList<string> All =
	[
		AccountStatus, AccountPassword, CharacterLink, CharacterUnlink, PlayerCreate, PlayerDestroy, PlayerPassword, PlayerBoot,
		PlayerWarn, BanAdd, BanLift, BanExpired,
		RoleSave, RoleDelete, RoleAssign, RoleUnassign, RoleOverride, PermissionDefine, PermissionRemove,
		CategorySave, CategoryRename, CategoryDelete, SitelockAdd, SitelockRemove,
		BannedNameAdd, BannedNameRemove, ConfigSet, ConfigImport, RestrictionSet, RestrictionClear
	];
}
