using System.Text.RegularExpressions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the <c>attributes:</c> block (format 1.5).</summary>
public partial class PackageManifestService
{
	/// <summary>The first format with the <c>attributes:</c> block.</summary>
	public static PackageFormatVersion AttributeEntriesFormat { get; } = new(1, 5);

	/// <summary>The first format in which an entry's <c>index:</c> means something; below it the key is ignored.</summary>
	public static PackageFormatVersion AttributeIndexFormat { get; } = new(1, 8);

	private static readonly IReadOnlySet<string> KnownAttributeEntryKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"flags", "limit", "enum", "delimiter", "retroactive", "index"
	};

	/// <summary>
	/// The attribute flags a new world has (<c>AttributeFlagSeed</c> in SharpMUSH.Database, which a test keeps this
	/// in step with), less <c>branch</c>: that marks a copy with attributes below it, which is structure, not a default.
	/// The plan checks the game's own table as well.
	/// </summary>
	public static IReadOnlySet<string> AttributeEntryFlags { get; } = new HashSet<string>(StringComparer.Ordinal)
	{
		"no_command", "no_inherit", "no_clone", "mortal_dark", "wizard", "veiled", "nearby", "locked", "safe",
		"visual", "public", "debug", "no_debug", "regexp", "case", "nospace", "noname", "aahear", "amhear", "quiet",
		"prefixmatch", "cmdsyntax", "funsyntax", "internal", "nodump"
	};

	[GeneratedRegex("^" + HelperFunctions.AttributeNameCharacters + "+$")]
	private static partial Regex AttributeEntryNameRegex();

	/// <summary>Whether <paramref name="name"/> is one <c>@attribute</c> takes: its characters, and <c>`</c> only between branches.</summary>
	public static bool IsValidAttributeEntryName(string name)
		=> AttributeEntryNameRegex().IsMatch(name) && !name.StartsWith('`') && !name.EndsWith('`') && !name.Contains("``");

	/// <summary>
	/// The standard attribute entries a package declares: a mapping of attribute name to its default
	/// <c>flags</c>, and a <c>limit</c> or an <c>enum</c> (with its <c>delimiter</c>), as <c>@attribute</c> makes them,
	/// and whether the flags are <c>retroactive</c>. Whether the game's table already has an entry, and whether it
	/// agrees, is asked of the live table when the package is planned.
	/// </summary>
	private static IReadOnlyList<PackageAttributeEntrySpec> ReadAttributeEntries(
		Dictionary<string, object?> doc, PackageKind kind, PackageFormatVersion format, List<PackageManifestIssue> issues)
	{
		if (!doc.TryGetValue("attributes", out var node) || node is null)
		{
			return [];
		}

		if (kind is PackageKind.Plugin or PackageKind.Ui)
		{
			issues.Add(PackageManifestIssue.Error("attributes",
				"A plugin package cannot declare 'attributes'; declare them in a softcode package it depends on."));
			return [];
		}

		if (format.Major == AttributeEntriesFormat.Major && format.Minor < AttributeEntriesFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Error("attributes",
				$"'attributes' needs format {AttributeEntriesFormat} or later; set format: {AttributeEntriesFormat}."));
		}

		if (node is not Dictionary<object, object> map)
		{
			issues.Add(PackageManifestIssue.Error("attributes", "'attributes' must be a mapping of attribute name to its entry."));
			return [];
		}

		var result = new List<PackageAttributeEntrySpec>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (rawName, rawEntry) in Normalize(map))
		{
			var name = rawName.Trim().ToUpperInvariant();
			var path = $"attributes.{rawName}";
			if (!IsValidAttributeEntryName(name))
			{
				issues.Add(PackageManifestIssue.Error(path,
					$"'{rawName}' is not an attribute name @attribute takes: no spaces or brackets, and ` only between branches."));
				continue;
			}

			if (PackageRefIndirection.IsReservedAttribute(name))
			{
				issues.Add(PackageManifestIssue.Error(path, "The PM` attribute tree is reserved for the package engine."));
				continue;
			}

			if (!seen.Add(name))
			{
				issues.Add(PackageManifestIssue.Error(path, $"'{name}' is declared more than once."));
				continue;
			}

			// A bare name (BBPOST_COUNT:) is an entry with no default flags and no restriction.
			Dictionary<string, object?> entry;
			switch (rawEntry)
			{
				case null:
					entry = [];
					break;
				case Dictionary<object, object> entryMap:
					entry = Normalize(entryMap);
					break;
				default:
					issues.Add(PackageManifestIssue.Error(path, "An attribute entry must be a mapping with flags, limit or enum."));
					continue;
			}

			if (ReadAttributeEntry(name, entry, path, format, issues) is { } spec)
			{
				result.Add(spec);
			}
		}

		return result;
	}

	private static PackageAttributeEntrySpec? ReadAttributeEntry(
		string name, Dictionary<string, object?> entry, string path, PackageFormatVersion format,
		List<PackageManifestIssue> issues)
	{
		foreach (var key in entry.Keys.Where(k => !KnownAttributeEntryKeys.Contains(k)))
		{
			issues.Add(PackageManifestIssue.Warning($"{path}.{key}", $"Unknown key '{key}' is ignored."));
		}

		var ok = true;
		var flags = new List<string>();
		foreach (var (flag, flagPath) in ScalarList(entry, "flags", path, issues, ref ok))
		{
			var lower = flag.Trim().ToLowerInvariant();
			if (lower == "branch")
			{
				issues.Add(PackageManifestIssue.Error(flagPath, "branch marks a copy with attributes below it; it is not a default flag."));
				ok = false;
			}
			else if (!AttributeEntryFlags.Contains(lower))
			{
				issues.Add(PackageManifestIssue.Error(flagPath, $"'{flag}' is not an attribute flag."));
				ok = false;
			}
			else if (!flags.Contains(lower))
			{
				flags.Add(lower);
			}
		}

		string? limit = null;
		if (entry.GetValueOrDefault("limit") is { } limitNode)
		{
			limit = limitNode as string;
			if (string.IsNullOrEmpty(limit))
			{
				issues.Add(PackageManifestIssue.Error($"{path}.limit", "A limit is a regular expression, as @attribute/limit takes it."));
				ok = false;
			}
			else
			{
				try
				{
					SoftcodeRegex.Create(limit, RegexOptions.IgnoreCase);
				}
				catch (ArgumentException ex)
				{
					issues.Add(PackageManifestIssue.Error($"{path}.limit", $"'{limit}' is not a regular expression: {ex.Message}"));
					ok = false;
				}
			}
		}

		var delimiter = ' ';
		if (entry.GetValueOrDefault("delimiter") is { } delimiterNode)
		{
			if (delimiterNode is not string { Length: 1 } text)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.delimiter", "A delimiter is exactly one character; quote it, as \" \" or \"|\"."));
				ok = false;
			}
			else
			{
				delimiter = text[0];
			}

			if (!entry.ContainsKey("enum"))
			{
				issues.Add(PackageManifestIssue.Warning($"{path}.delimiter", "A delimiter only means something with an enum; it is ignored."));
			}
		}

		List<string>? choices = null;
		if (entry.ContainsKey("enum"))
		{
			choices = [];
			foreach (var (choice, choicePath) in ScalarList(entry, "enum", path, issues, ref ok))
			{
				if (choice.Length == 0 || choice.Contains(delimiter))
				{
					issues.Add(PackageManifestIssue.Error(choicePath,
						$"A choice is not empty and does not hold the delimiter ('{delimiter}'); choose another delimiter for choices with spaces."));
					ok = false;
					continue;
				}

				choices.Add(choice);
			}

			if (choices.Count == 0 && ok)
			{
				issues.Add(PackageManifestIssue.Error($"{path}.enum", "An enum lists at least one choice."));
				ok = false;
			}
		}

		if (limit is not null && choices is not null)
		{
			issues.Add(PackageManifestIssue.Error(path, "An entry takes a limit or an enum, not both: each replaces the other, as in @attribute."));
			ok = false;
		}

		var retroactive = false;
		if (entry.GetValueOrDefault("retroactive") is { } retroNode && !bool.TryParse(retroNode.ToString(), out retroactive))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.retroactive", "retroactive is true or false."));
			ok = false;
		}

		var index = ReadAttributeIndex(entry, path, format, issues, ref ok);
		if (index is not null && flags.Contains("internal"))
		{
			issues.Add(PackageManifestIssue.Error($"{path}.index", "An internal attribute cannot be indexed: no copy of it is ever listed."));
			ok = false;
		}

		return ok ? new PackageAttributeEntrySpec(name, flags, limit, choices, delimiter, retroactive, index) : null;
	}

	/// <summary>
	/// An entry's <c>index:</c>, as <c>@attribute/index</c> takes it: <c>true</c> for the default (case-insensitive
	/// text, whole values, every object), <c>false</c> for none, or the option words, as one string
	/// (<c>numeric type:thing</c>) or a list (<c>[numeric, type:thing]</c>).
	/// </summary>
	private static AttributeIndexOptions? ReadAttributeIndex(Dictionary<string, object?> entry, string path,
		PackageFormatVersion format, List<PackageManifestIssue> issues, ref bool ok)
	{
		if (entry.ContainsKey("index") && format.Major == AttributeIndexFormat.Major && format.Minor < AttributeIndexFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Warning($"{path}.index",
				$"'index' needs format {AttributeIndexFormat} or later and is ignored; set format: {AttributeIndexFormat}."));
			return null;
		}

		string words;
		switch (entry.GetValueOrDefault("index"))
		{
			case null:
				return null;
			case string text when bool.TryParse(text, out var on):
				if (!on) return null;
				words = string.Empty;
				break;
			case string text:
				words = text;
				break;
			case List<object> list when list.All(item => item is string):
				words = string.Join(' ', list.Cast<string>());
				break;
			default:
				issues.Add(PackageManifestIssue.Error($"{path}.index",
					"index is true, or the options @attribute/index takes: exact, numeric, split[:<delimiter>], type:<type>."));
				ok = false;
				return null;
		}

		switch (AttributeIndexOptions.Parse(words))
		{
			case AttributeIndexOptions options:
				return options;
			case Error<string> error:
				issues.Add(PackageManifestIssue.Error($"{path}.index", error.Value));
				ok = false;
				return null;
			default:
				return null;
		}
	}

	/// <summary>The scalars in the list <paramref name="key"/> of <paramref name="entry"/>, each with its path; anything else is reported.</summary>
	private static List<(string Value, string Path)> ScalarList(
		Dictionary<string, object?> entry, string key, string path, List<PackageManifestIssue> issues, ref bool ok)
	{
		var result = new List<(string, string)>();
		switch (entry.GetValueOrDefault(key))
		{
			case null:
				break;
			case List<object> list:
				for (var i = 0; i < list.Count; i++)
				{
					if (list[i] is string item)
					{
						result.Add((item.Trim(), $"{path}.{key}[{i}]"));
					}
					else
					{
						issues.Add(PackageManifestIssue.Error($"{path}.{key}[{i}]", "Must be a single value."));
						ok = false;
					}
				}

				break;
			default:
				issues.Add(PackageManifestIssue.Error($"{path}.{key}", $"'{key}' must be a list, such as [a, b]."));
				ok = false;
				break;
		}

		return result;
	}
}
