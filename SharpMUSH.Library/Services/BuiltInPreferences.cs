using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The preference groups the portal itself declares, beside the ones installed packages declare. They are read and
/// written like any other group (<c>api/preferences</c>, <c>@preference</c>, <c>pref()</c>), under the package id
/// <see cref="PackageId"/>, which no package may take.
/// </summary>
public static class BuiltInPreferences
{
	/// <summary>The package id the built-in groups are declared under: <c>portal</c>.</summary>
	public const string PackageId = "portal";

	/// <summary>Every built-in group, ordered by id.</summary>
	public static IReadOnlyList<PreferenceGroup> Groups { get; } =
	[
		new(PackageId, PlayPreferences.Group)
	];

	/// <summary>The built-in group <paramref name="reference"/> names, or null.</summary>
	public static PreferenceGroup? Find(PreferenceGroupRef reference)
		=> reference.Package == PackageId
			? Groups.FirstOrDefault(group => group.Spec.Name == reference.Group)
			: null;
}
