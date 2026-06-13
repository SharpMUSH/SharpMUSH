using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Models.Applications;

/// <summary>
/// Client view of a registered Dynamic Application, deserialized from the <c>/api/applications</c>
/// DTO (enums travel as strings, zones as a string array). Parsed-enum helpers are provided for
/// nav filtering and renderer selection.
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
	int Order)
{
	/// <summary>Parsed kind; defaults to <see cref="ApplicationKind.Page"/> on an unknown value.</summary>
	public ApplicationKind KindEnum =>
		Enum.TryParse<ApplicationKind>(Kind, ignoreCase: true, out var k) ? k : ApplicationKind.Page;

	/// <summary>Parsed minimum role; defaults to <see cref="PortalRole.Wizard"/> (fail closed) on an unknown value.</summary>
	public PortalRole MinimumRoleEnum =>
		Enum.TryParse<PortalRole>(MinimumRole, ignoreCase: true, out var r) ? r : PortalRole.Wizard;

	/// <summary>Parsed allowed zones for Widget apps.</summary>
	public IReadOnlyList<WidgetZone> ZoneEnums =>
		ApplicationRegistryMapping.ZonesFromString(string.Join(",", Zones)) ?? [];
}
