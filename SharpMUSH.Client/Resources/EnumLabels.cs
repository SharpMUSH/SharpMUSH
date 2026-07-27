using SharpMUSH.Client.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Client.Resources;

/// <summary>
/// <see cref="SharedResource"/> keys for the enums the portal shows to a human. The member name is a
/// machine identifier and must never reach the UI, so every display site looks its label up through
/// <c>Loc[value.ResourceKey()]</c> rather than rendering the value directly — the same contract
/// <see cref="WidgetZoneLabels"/> establishes for widget zones.
///
/// Deliberately absent: <see cref="SharpMUSH.Library.Authorization.PortalRole"/>. Its member names are
/// the values admins type into softcode and pass to the REST API (an application's
/// <c>MinimumRole</c>), and they are MUSH terms of art besides, so they stay untranslated everywhere.
/// </summary>
public static class EnumLabels
{
	/// <summary>Resource key for a game object's type, as shown in the object browser's group headings.</summary>
	public static string ResourceKey(this MushObjectType type) => $"EnumObjectType{type}";

	/// <summary>Resource key for whether a registered application is a page or a placeable widget.</summary>
	public static string ResourceKey(this ApplicationKind kind) => $"EnumApplicationKind{kind}";

	/// <summary>Resource key for the kind of apply that produced a package revision.</summary>
	public static string ResourceKey(this PackageRevisionKind kind) => $"EnumRevisionKind{kind}";
}
