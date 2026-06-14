namespace SharpMUSH.Client.Models.Roles;

/// <summary>
/// Client view of a portal role, deserialized from the <c>/api/roles</c> DTO. Permissions map a
/// scope string to a tri-state decision (<c>"Allow"</c> / <c>"Deny"</c> / <c>"Inherit"</c>);
/// scopes absent from the dictionary are treated as Inherit by the editor.
/// </summary>
public sealed record PortalRoleModel(
	string Slug,
	string Name,
	string Color,
	int Priority,
	bool IsSystem,
	Dictionary<string, string> Permissions,
	long CreatedAt,
	long UpdatedAt);
