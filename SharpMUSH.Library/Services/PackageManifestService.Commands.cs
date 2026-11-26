using System.Text.RegularExpressions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the <c>commands:</c> block (format 1.24).</summary>
public partial class PackageManifestService
{
	/// <summary>The first format with the <c>commands:</c> block; below it the block is ignored.</summary>
	public static PackageFormatVersion CommandsFormat { get; } = new(1, 24);

	private static readonly IReadOnlySet<string> KnownCommandKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"name", "object", "attribute", "nobreak"
	};

	/// <summary>What a command name may hold: what <c>@command/add</c> accepts, without spaces.</summary>
	[GeneratedRegex("^[^\\s:;\"#\\\\&\\]\\[\\p{C}=/]+$")]
	private static partial Regex CommandNameRegex();

	/// <summary>
	/// The commands a softcode package adds: a list of entries, each with the command's <c>name</c>, the <c>object</c>
	/// (a ref in <c>objects:</c>) holding the attribute its hook runs, that <c>attribute</c>, and <c>nobreak</c>.
	/// </summary>
	private static IReadOnlyList<PackageCommandSpec> ReadCommands(Dictionary<string, object?> doc, PackageKind kind,
		PackageFormatVersion format, IReadOnlyList<PackageObjectSpec> objects, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue("commands", out var node) || node is null)
		{
			return [];
		}

		if (format.Major == CommandsFormat.Major && format.Minor < CommandsFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Warning("commands",
				$"'commands' needs format {CommandsFormat} or later and is ignored; set format: \"{CommandsFormat}\"."));
			return [];
		}

		if (kind != PackageKind.Softcode)
		{
			issues.Add(PackageManifestIssue.Error("commands", "Only a softcode package adds commands."));
			return [];
		}

		if (node is not List<object> entries)
		{
			issues.Add(PackageManifestIssue.Error("commands", "'commands' must be a list of commands, each with a name, object and attribute."));
			return [];
		}

		var objectsByRef = objects.ToDictionary(o => o.Ref, StringComparer.Ordinal);
		var commands = new List<PackageCommandSpec>();
		var names = new HashSet<string>(StringComparer.Ordinal);
		for (var index = 0; index < entries.Count; index++)
		{
			var path = $"commands[{index}]";
			if (entries[index] is not Dictionary<object, object> map)
			{
				issues.Add(PackageManifestIssue.Error(path, "A command must be a mapping with a name, object and attribute."));
				continue;
			}

			if (ReadCommand(Normalize(map), path, objectsByRef, issues) is not PackageCommandSpec command)
			{
				continue;
			}

			if (!names.Add(command.Name))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.name", $"The command '{command.Name}' is listed twice."));
				continue;
			}

			commands.Add(command);
		}

		return commands;
	}

	private static Found<PackageCommandSpec> ReadCommand(Dictionary<string, object?> entry, string path,
		Dictionary<string, PackageObjectSpec> objectsByRef, List<PackageManifestIssue> issues)
	{
		foreach (var key in entry.Keys.Where(k => !KnownCommandKeys.Contains(k)))
		{
			issues.Add(PackageManifestIssue.Warning($"{path}.{key}", $"Unknown key '{key}' is ignored."));
		}

		var ok = true;
		var name = (entry.GetValueOrDefault("name") as string)?.Trim().ToUpperInvariant();
		if (string.IsNullOrEmpty(name) || !CommandNameRegex().IsMatch(name))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.name",
				"A command needs a name with no spaces and none of : ; \" # \\ & [ ] = /, such as +time."));
			ok = false;
		}

		var reference = (entry.GetValueOrDefault("object") as string)?.Trim();
		if (string.IsNullOrEmpty(reference) || !objectsByRef.TryGetValue(reference, out var holder))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.object",
				string.IsNullOrEmpty(reference)
					? "A command needs the object its hook runs on: a ref under 'objects'."
					: $"'{reference}' does not match any object ref in this package."));
			ok = false;
			holder = null;
		}

		var attribute = (entry.GetValueOrDefault("attribute") as string)?.Trim().ToUpperInvariant();
		if (string.IsNullOrEmpty(attribute) || !AttributeNameRegex().IsMatch(attribute)
			|| attribute.StartsWith('`') || attribute.EndsWith('`'))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.attribute", "A command needs the attribute its hook runs, such as CMD`TIME."));
			ok = false;
		}
		else if (holder is { IsAttach: false }
			&& !holder.Attributes.Keys.Any(key => key.Equals(attribute, StringComparison.OrdinalIgnoreCase)))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.attribute", $"The object '{reference}' sets no attribute {attribute}."));
			ok = false;
		}

		var noBreak = false;
		switch (entry.GetValueOrDefault("nobreak"))
		{
			case null:
				break;
			case string text when bool.TryParse(text, out var parsed):
				noBreak = parsed;
				break;
			case bool value:
				noBreak = value;
				break;
			default:
				issues.Add(PackageManifestIssue.Error($"{path}.nobreak", "'nobreak' is true or false."));
				ok = false;
				break;
		}

		if (!ok)
		{
			return new NotFound();
		}

		return new PackageCommandSpec(name!, reference!, attribute!, noBreak);
	}
}
