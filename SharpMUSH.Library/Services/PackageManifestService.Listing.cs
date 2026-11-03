using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

public partial class PackageManifestService
{
	/// <summary>The first format with <c>bridge:</c> and <c>subcategory:</c>.</summary>
	public static PackageFormatVersion ListingFormat { get; } = new(1, 20);

	/// <summary>
	/// Reads <c>subcategory:</c> (format 1.20): the group the package is listed under within its category, such as
	/// <c>Fate</c>, in the words the package lists show. Spaces are collapsed; an empty value is the same as none.
	/// </summary>
	private static string? ReadSubcategory(
		Dictionary<string, object?> doc, PackageFormatVersion format, List<PackageManifestIssue> issues)
	{
		if (OptionalString(doc, "subcategory", issues) is not { } text)
		{
			return null;
		}

		if (format.Major == ListingFormat.Major && format.Minor < ListingFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Warning("subcategory",
				$"'subcategory' needs format {ListingFormat} or later and is ignored; set format: \"{ListingFormat}\"."));
			return null;
		}

		var subcategory = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		if (subcategory.Length > MaxCategoryLength)
		{
			issues.Add(PackageManifestIssue.Error("subcategory",
				$"'subcategory' is {subcategory.Length} characters long; at most {MaxCategoryLength} are allowed."));
			return null;
		}

		return subcategory.Length == 0 ? null : subcategory;
	}

	/// <summary>
	/// Reads <c>bridge:</c> (format 1.20): whether the package joins two others, such as <c>sheet-fate-help</c>
	/// joining <c>sheet-fate</c> to <c>plus-help</c>. A bridge names both halves in <c>depends:</c>.
	/// </summary>
	private static bool ReadBridge(Dictionary<string, object?> doc, PackageFormatVersion format,
		IReadOnlyList<PackageDependencySpec> dependencies, List<PackageManifestIssue> issues)
	{
		if (doc.GetValueOrDefault("bridge") is not { } node)
		{
			return false;
		}

		if (format.Major == ListingFormat.Major && format.Minor < ListingFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Warning("bridge",
				$"'bridge' needs format {ListingFormat} or later and is ignored; set format: \"{ListingFormat}\"."));
			return false;
		}

		if (!bool.TryParse(node.ToString(), out var bridge))
		{
			issues.Add(PackageManifestIssue.Error("bridge", "'bridge' must be true or false."));
			return false;
		}

		if (bridge && dependencies.Count < 2)
		{
			issues.Add(PackageManifestIssue.Error("bridge",
				"A bridge joins two packages; list both in 'depends'."));
			return false;
		}

		return bridge;
	}
}
