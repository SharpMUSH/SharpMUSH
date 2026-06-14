namespace SharpMUSH.Client.Models.Roles;

/// <summary>
/// Client view of an account's role assignments, deserialized from the
/// <c>/api/roles/account</c> DTO. <see cref="RoleSlugs"/> lists every role currently assigned.
/// </summary>
public sealed record AccountRolesModel(
	string AccountId,
	string Username,
	string Email,
	bool IsDisabled,
	string[] RoleSlugs);
