using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the <c>settings:</c> block (format 1.3).</summary>
public partial class PackageManifestService
{
	/// <summary>
	/// The configuration options a package sets: a mapping of option name, as <c>@config</c> lists it, to the
	/// value <c>@config/set</c> would take, or to one object ref for a dbref option. An option must exist and be
	/// one <c>@config/set</c> can set, and a literal value must be one the option takes; whether the value is in
	/// range and accepted by the game's validators is asked of the live configuration when the package is planned.
	/// </summary>
	private static IReadOnlyList<PackageSettingSpec> ReadSettings(
		Dictionary<string, object?> doc, PackageKind kind, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue("settings", out var node) || node is null)
		{
			return [];
		}

		if (kind == PackageKind.Managed)
		{
			issues.Add(PackageManifestIssue.Error("settings",
				"A managed package cannot declare 'settings'; declare them in a softcode package it depends on."));
			return [];
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("settings", "'settings' must be a mapping of option name to value."));
			return [];
		}

		var result = new List<PackageSettingSpec>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (rawKey, rawValue) in Normalize(map))
		{
			var option = rawKey.Trim().ToLowerInvariant();
			var path = $"settings.{option}";
			if (!seen.Add(option))
			{
				issues.Add(PackageManifestIssue.Error(path, $"'{option}' is set more than once."));
				continue;
			}

			if (ConfigOptionWriter.PropertyOf(option) is not { } property)
			{
				issues.Add(PackageManifestIssue.Error(path, $"'{option}' is not a configuration option."));
				continue;
			}

			if (!ConfigOptionWriter.Settable(property))
			{
				issues.Add(PackageManifestIssue.Error(path,
					$"'{option}' cannot be set by a package: it is a file path, one of God's options, or a list with a command of its own."));
				continue;
			}

			if (rawValue is null or Dictionary<object, object> or List<object>)
			{
				issues.Add(PackageManifestIssue.Error(path, "The value must be a single value, as @config/set takes it."));
				continue;
			}

			var value = rawValue.ToString()!.Trim();
			if (value.Contains("{{"))
			{
				if (PackageRefScanner.ParseSingle(value) is null)
				{
					issues.Add(PackageManifestIssue.Error(path,
						$"'{value}' must be exactly one ref ({{{{name}}}}, {{{{$well_known}}}}, {{{{?configure}}}}, or {{{{pkg/ref}}}})."));
					continue;
				}

				if (!ConfigOptionWriter.IsDbref(property))
				{
					issues.Add(PackageManifestIssue.Error(path, $"'{option}' is not a dbref option, so it cannot be set to an object ref."));
					continue;
				}
			}
			else if (!ConfigOptionWriter.TryParseValue(property, value, out _))
			{
				issues.Add(PackageManifestIssue.Error(path, $"'{value}' is not a value '{option}' takes."));
				continue;
			}

			result.Add(new PackageSettingSpec(option, value));
		}

		return result;
	}
}
