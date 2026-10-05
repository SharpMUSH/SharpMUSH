namespace SharpMUSH.Client.Models.Roles;

/// <summary>
/// Client view of an account's role assignments, deserialized from the
/// <c>/api/roles/account</c> DTO. <see cref="RoleSlugs"/> lists every role currently assigned;
/// <see cref="Overrides"/> maps each per-account override's scope to <c>Allow</c> or <c>Deny</c>.
/// </summary>
public sealed record AccountRolesModel(
	string AccountId,
	string Username,
	string Email,
	string Status,
	string[] RoleSlugs,
	Dictionary<string, string> Overrides);
