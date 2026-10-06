using System.Security.Claims;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Models.Applications;

/// <summary>
/// Client view of a registered Dynamic Application, deserialized from the <c>/api/applications</c>
/// DTO (enums travel as strings, zones as a string array). Parsed-enum helpers are provided for
/// nav filtering and renderer selection. <see cref="Scope"/> names the layout scope a game panel belongs to
/// (e.g. <c>"play"</c>); <see cref="OobPackage"/> names the OOB package whose latest payload is its data;
/// <see cref="Permission"/> names a permission scope the viewer must hold besides the minimum role.
/// </summary>
public sealed record PortalApplication(
	string Slug,
	string DisplayName,
	string? Icon,
	string Kind,
	string SchemaUrl,
	string? DataUrl,
	string? SubmitRoute,
	string MinimumRole,
	string? NavPlacement,
	string[] Zones,
	int Order,
	string? OwningPackage = null,
	string RenderKind = ApplicationRenderKind.Schema,
	string? ComponentAssemblyUrl = null,
	string? ComponentTypeName = null,
	string? Scope = null,
	string? OobPackage = null,
	string? Permission = null)
{
	/// <summary>True when this app is rendered by a plugin-shipped compiled component (not the schema renderer).</summary>
	public bool IsComponent =>
		string.Equals(RenderKind, ApplicationRenderKind.Component, StringComparison.OrdinalIgnoreCase);

	/// <summary>Parsed kind; defaults to <see cref="ApplicationKind.Page"/> on an unknown value.</summary>
	public ApplicationKind KindEnum =>
		Enum.TryParse<ApplicationKind>(Kind, ignoreCase: true, out var k) ? k : ApplicationKind.Page;

	/// <summary>Parsed minimum role; defaults to <see cref="PortalRole.Wizard"/> (fail closed) on an unknown value.</summary>
	public PortalRole MinimumRoleEnum =>
		Enum.TryParse<PortalRole>(MinimumRole, ignoreCase: true, out var r) ? r : PortalRole.Wizard;

	/// <summary>
	/// Whether <paramref name="user"/> may see this application: their role meets <see cref="MinimumRoleEnum"/>
	/// and, when <see cref="Permission"/> is set, they hold that scope (a permission claim, compared
	/// case-insensitively as scopes are). Cosmetic, like every client gate: softcode still gates the data.
	/// </summary>
	public bool Admits(ClaimsPrincipal? user)
		=> Admits(PortalRoleHelper.CurrentRole(user), scope => HoldsPermission(user, scope));

	/// <summary>Whether a viewer of <paramref name="role"/> who holds the scopes <paramref name="holds"/> admits may see this application.</summary>
	public bool Admits(PortalRole role, Func<string, bool> holds)
		=> role >= MinimumRoleEnum && (string.IsNullOrWhiteSpace(Permission) || holds(Permission.Trim()));

	/// <summary>Whether <paramref name="user"/> carries the permission claim for <paramref name="scope"/>.</summary>
	public static bool HoldsPermission(ClaimsPrincipal? user, string scope)
		=> user?.FindAll(PortalPermission.ClaimType).Any(c => string.Equals(c.Value, scope, StringComparison.OrdinalIgnoreCase)) == true;

	/// <summary>Parsed allowed zones for Widget apps.</summary>
	public IReadOnlyList<WidgetZone> ZoneEnums =>
		ApplicationRegistryMapping.ZonesFromString(string.Join(",", Zones)) ?? [];
}
