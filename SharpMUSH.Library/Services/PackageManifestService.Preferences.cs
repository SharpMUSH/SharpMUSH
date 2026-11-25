using System.Globalization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the <c>preferences:</c> block (format 1.21).</summary>
public partial class PackageManifestService
{
	/// <summary>The first format with the <c>preferences:</c> block; below it the block is ignored.</summary>
	public static PackageFormatVersion PreferencesFormat { get; } = new(1, 21);

	private static readonly IReadOnlySet<string> KnownPreferenceGroupKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"scope", "label", "description", "keys"
	};

	private static readonly IReadOnlySet<string> KnownPreferenceKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"type", "default", "values", "min", "max", "max_length", "label", "description"
	};

	/// <summary>
	/// The preference groups a package declares: a mapping of group name to its <c>scope</c> (account or character),
	/// <c>label</c>, <c>description</c> and <c>keys</c>, each key a mapping of its <c>type</c>, <c>default</c>, a choice's
	/// <c>values</c>, a number's <c>min</c> and <c>max</c>, text's or JSON's <c>max_length</c>, and its <c>label</c> and
	/// <c>description</c>. Every default is checked as a value written to the preference is.
	/// </summary>
	private static IReadOnlyList<PackagePreferenceGroupSpec> ReadPreferences(Dictionary<string, object?> doc,
		PackageFormatVersion format, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue("preferences", out var node) || node is null)
		{
			return [];
		}

		if (format.Major == PreferencesFormat.Major && format.Minor < PreferencesFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Warning("preferences",
				$"'preferences' needs format {PreferencesFormat} or later and is ignored; set format: \"{PreferencesFormat}\"."));
			return [];
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("preferences", "'preferences' must be a mapping of group name to its definition."));
			return [];
		}

		var groups = new List<PackagePreferenceGroupSpec>();
		var groupNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (rawName, rawGroup) in Normalize(map))
		{
			var path = $"preferences.{rawName}";
			var name = rawName.Trim();
			if (!Preferences.IsValidGroupName(name))
			{
				issues.Add(PackageManifestIssue.Error(path,
					$"'{name}' is not a valid group name (lowercase letters, digits, hyphens; a letter first; at most {Preferences.MaxNameLength})."));
				continue;
			}

			if (!groupNames.Add(name))
			{
				issues.Add(PackageManifestIssue.Error(path, $"The group '{name}' is declared twice."));
				continue;
			}

			if (rawGroup is not Dictionary<object, object> groupMap)
			{
				issues.Add(PackageManifestIssue.Error(path, "A preference group must be a mapping with a scope and keys."));
				continue;
			}

			if (ReadPreferenceGroup(name, Normalize(groupMap), path, issues) is PackagePreferenceGroupSpec group)
			{
				groups.Add(group);
			}
		}

		return groups;
	}

	private static Found<PackagePreferenceGroupSpec> ReadPreferenceGroup(string name, Dictionary<string, object?> group,
		string path, List<PackageManifestIssue> issues)
	{
		foreach (var key in group.Keys.Where(k => !KnownPreferenceGroupKeys.Contains(k)))
		{
			issues.Add(PackageManifestIssue.Warning($"{path}.{key}", $"Unknown key '{key}' is ignored."));
		}

		if (Preferences.ParseScope(group.GetValueOrDefault("scope") as string) is not { } scope)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.scope", "A preference group's scope is account or character."));
			return new NotFound();
		}

		if (group.GetValueOrDefault("keys") is not Dictionary<object, object> keyMap || keyMap.Count == 0)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.keys", "'keys' must be a mapping of at least one preference name to its definition."));
			return new NotFound();
		}

		if (keyMap.Count > Preferences.MaxKeys)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.keys", $"A group holds at most {Preferences.MaxKeys} preferences."));
			return new NotFound();
		}

		var ok = true;
		var keys = new List<PackagePreferenceSpec>();
		var keyNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (rawKey, rawEntry) in Normalize(keyMap))
		{
			var keyPath = $"{path}.keys.{rawKey}";
			var key = rawKey.Trim();
			if (!Preferences.IsValidKeyName(key))
			{
				issues.Add(PackageManifestIssue.Error(keyPath,
					$"'{key}' is not a valid preference name (lowercase letters, digits, underscores; a letter first; at most {Preferences.MaxNameLength})."));
				ok = false;
				continue;
			}

			if (!keyNames.Add(key))
			{
				issues.Add(PackageManifestIssue.Error(keyPath, $"The preference '{key}' is declared twice."));
				ok = false;
				continue;
			}

			if (rawEntry is not Dictionary<object, object> entryMap)
			{
				issues.Add(PackageManifestIssue.Error(keyPath, "A preference must be a mapping with at least a type."));
				ok = false;
				continue;
			}

			if (ReadPreference(key, Normalize(entryMap), keyPath, issues) is PackagePreferenceSpec spec)
			{
				keys.Add(spec);
			}
			else
			{
				ok = false;
			}
		}

		return ok
			? new PackagePreferenceGroupSpec(name, scope, Text(group, "label"), Text(group, "description"), keys)
			: new NotFound();
	}

	private static Found<PackagePreferenceSpec> ReadPreference(string key, Dictionary<string, object?> entry, string path,
		List<PackageManifestIssue> issues)
	{
		foreach (var unknown in entry.Keys.Where(k => !KnownPreferenceKeys.Contains(k)))
		{
			issues.Add(PackageManifestIssue.Warning($"{path}.{unknown}", $"Unknown key '{unknown}' is ignored."));
		}

		if (Preferences.ParseType(entry.GetValueOrDefault("type") as string) is not { } type)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.type",
				$"A preference's type is {string.Join(", ", Preferences.TypeNames)}."));
			return new NotFound();
		}

		var ok = true;
		var values = ScalarList(entry, "values", path, issues, ref ok).Select(v => v.Value).ToList();
		if (!ok)
		{
			return new NotFound();
		}

		if (type == PreferenceType.Choice
			&& (values.Count == 0 || values.Count > Preferences.MaxChoices || values.Any(v => v.Length == 0)))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.values",
				$"A choice lists from 1 to {Preferences.MaxChoices} values, none of them empty."));
			return new NotFound();
		}

		if (type != PreferenceType.Choice && values.Count > 0)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.values", "Only a choice lists values."));
			return new NotFound();
		}

		if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.values", "A choice lists each value once."));
			return new NotFound();
		}

		var numeric = type is PreferenceType.Number or PreferenceType.Integer;
		if (!ReadBound(entry, "min", path, numeric, issues, out var min)
			|| !ReadBound(entry, "max", path, numeric, issues, out var max))
		{
			return new NotFound();
		}

		if (min > max)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.max", "max is below min."));
			return new NotFound();
		}

		var maxLength = Preferences.DefaultMaxLength;
		if (entry.GetValueOrDefault("max_length") is { } lengthNode)
		{
			if (type is not (PreferenceType.Text or PreferenceType.Json))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.max_length", "Only text and json preferences take max_length."));
				return new NotFound();
			}

			if (!int.TryParse(lengthNode.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out maxLength)
				|| maxLength is < 1 or > Preferences.LargestMaxLength)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.max_length",
					$"max_length is a whole number from 1 to {Preferences.LargestMaxLength}."));
				return new NotFound();
			}
		}

		var spec = new PackagePreferenceSpec(key, type, null, values, min, max, maxLength,
			Text(entry, "label"), Text(entry, "description"));
		switch (entry.GetValueOrDefault("default"))
		{
			case null:
				return spec;
			case string text:
				return Preferences.Check(spec, text) switch
				{
					string stored => spec with { Default = stored },
					Error<string> refused => Refused($"{path}.default", $"The default is refused: {refused.Value}", issues)
				};
			default:
				// A JSON default is written as its text, quoted: '{"a": 1}'.
				return Refused($"{path}.default", "The default must be a single value.", issues);
		}
	}

	private static NotFound Refused(string path, string message, List<PackageManifestIssue> issues)
	{
		issues.Add(PackageManifestIssue.Error(path, message));
		return new NotFound();
	}

	private static bool ReadBound(Dictionary<string, object?> entry, string key, string path, bool numeric,
		List<PackageManifestIssue> issues, out decimal? bound)
	{
		bound = null;
		if (entry.GetValueOrDefault(key) is not { } node)
		{
			return true;
		}

		if (!numeric)
		{
			issues.Add(PackageManifestIssue.Error($"{path}.{key}", $"Only number and integer preferences take {key}."));
			return false;
		}

		if (!decimal.TryParse(node.ToString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
				CultureInfo.InvariantCulture, out var value))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.{key}", $"{key} is a number."));
			return false;
		}

		bound = value;
		return true;
	}

	private static string Text(Dictionary<string, object?> map, string key)
		=> (map.GetValueOrDefault(key) as string)?.Trim() ?? string.Empty;
}
