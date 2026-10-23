using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the <c>wiki_properties:</c> block (format 1.17).</summary>
public partial class PackageManifestService
{
	/// <summary>The first format with the <c>wiki_properties:</c> block; below it the block is ignored.</summary>
	public static PackageFormatVersion WikiPropertiesFormat { get; } = new(1, 17);

	private static readonly IReadOnlySet<string> KnownWikiPropertyKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"type", "multiple", "values", "label", "description"
	};

	/// <summary>
	/// The properties a package's wiki facts state: a mapping of property key to its <c>type</c> (number, boolean, page
	/// or keyword), whether a page states <c>multiple</c> values, a keyword property's <c>values</c>, and its
	/// <c>label</c> and <c>description</c>, as <c>@wiki/property</c> takes them. Each is checked as the store checks it.
	/// </summary>
	private static IReadOnlyList<PackageWikiPropertySpec> ReadWikiProperties(Dictionary<string, object?> doc, PackageKind kind,
		PackageFormatVersion format, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue("wiki_properties", out var node) || node is null)
		{
			return [];
		}

		if (kind is PackageKind.Plugin or PackageKind.Ui)
		{
			issues.Add(PackageManifestIssue.Error("wiki_properties",
				"A plugin package cannot declare 'wiki_properties'; declare them in a softcode package it depends on."));
			return [];
		}

		if (format.Major == WikiPropertiesFormat.Major && format.Minor < WikiPropertiesFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Warning("wiki_properties",
				$"'wiki_properties' needs format {WikiPropertiesFormat} or later and is ignored; set format: \"{WikiPropertiesFormat}\"."));
			return [];
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("wiki_properties", "'wiki_properties' must be a mapping of property key to its definition."));
			return [];
		}

		var result = new List<PackageWikiPropertySpec>();
		foreach (var (rawId, rawEntry) in Normalize(map))
		{
			var path = $"wiki_properties.{rawId}";
			if (rawEntry is not Dictionary<object, object> entryMap)
			{
				issues.Add(PackageManifestIssue.Error(path, "A wiki property must be a mapping with at least a type."));
				continue;
			}

			var entry = Normalize(entryMap);
			foreach (var key in entry.Keys.Where(k => !KnownWikiPropertyKeys.Contains(k)))
			{
				issues.Add(PackageManifestIssue.Warning($"{path}.{key}", $"Unknown key '{key}' is ignored."));
			}

			if (ReadWikiProperty(rawId.Trim(), entry, path, issues) is not { } spec)
			{
				continue;
			}

			if (result.Any(p => p.Id == spec.Id))
			{
				issues.Add(PackageManifestIssue.Error(path, $"'{spec.Id}' is declared more than once."));
				continue;
			}

			result.Add(spec);
		}

		return result;
	}

	private static PackageWikiPropertySpec? ReadWikiProperty(
		string id, Dictionary<string, object?> entry, string path, List<PackageManifestIssue> issues)
	{
		if (WikiFactProperties.ParseType(entry.GetValueOrDefault("type") as string) is not { } type)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.type", "A wiki property's type is number, boolean, page or keyword."));
			return null;
		}

		var multiple = false;
		if (entry.GetValueOrDefault("multiple") is { } multipleNode && !bool.TryParse(multipleNode.ToString(), out multiple))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.multiple", "multiple is true or false."));
			return null;
		}

		var ok = true;
		var values = ScalarList(entry, "values", path, issues, ref ok);
		if (!ok)
		{
			return null;
		}

		var property = new PropertyDefinition(id, WikiFactProperties.SchemaVersion, type,
			multiple ? Cardinality.Multiple : Cardinality.Single, [.. values.Select(v => new Keyword(v.Value))],
			(entry.GetValueOrDefault("label") as string) ?? string.Empty,
			(entry.GetValueOrDefault("description") as string) ?? string.Empty);
		switch (WikiFactProperties.Checked(property))
		{
			case PropertyDefinition checkedProperty:
				return PackageDeclarationPlanner.WikiPropertySpec(checkedProperty);
			case Error<string> error:
				issues.Add(PackageManifestIssue.Error(path, error.Value));
				return null;
			default:
				return null;
		}
	}
}
