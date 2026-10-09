using SharpMUSH.Client.Models.Roles;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Pages.Admin.Roles;

/// <summary>The role editor's unsaved fields, refilled from the selected role on every reload.</summary>
public sealed class RoleDraft
{
	public string Name { get; set; } = "";
	public string Category { get; set; } = "";
	public string Color { get; set; } = "#6cde9a";
	public int Priority { get; set; }
	public Dictionary<string, string> Permissions { get; private set; } = new();

	public void Load(PortalRoleModel role)
	{
		Name = role.Name;
		Category = role.Category;
		Color = role.Color;
		Priority = role.Priority;
		Permissions = new Dictionary<string, string>(role.Permissions);
	}

	/// <summary>Allow, Deny, or Inherit when the role says nothing about the scope.</summary>
	public string StateFor(string scope) =>
		Permissions.TryGetValue(scope, out var s) && !string.IsNullOrEmpty(s) ? s : "Inherit";

	public void SetState(string scope, string state) => Permissions[scope] = state;
}

/// <summary>A category's name and description as typed: the create form's, or a category row's unsaved edit.</summary>
public sealed class RoleCategoryDraft
{
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";
}

/// <summary>The Permissions tab's form for defining a custom permission.</summary>
public sealed class CustomPermissionDraft
{
	public string Scope { get; set; } = "";
	public string Category { get; set; } = "";
	public string Description { get; set; } = "";
}

/// <summary>The Assignments tab's account lookup, kept by the page so it outlives a switch to another tab.</summary>
public sealed class RoleAccountLookup
{
	public string Name { get; set; } = "";
	public bool Looking { get; set; }
	public bool LookedUp { get; set; }
	public AccountRolesModel? Account { get; set; }
	public string OverrideScope { get; set; } = PortalPermission.AllScopes[0];
}

/// <summary>A role's colour as the page paints it: the faint text colour when it has none.</summary>
public static class RoleColor
{
	public static string Safe(string color) =>
		string.IsNullOrWhiteSpace(color) ? "var(--text-faint)" : color;
}
