using System.Text.RegularExpressions;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Pages.Admin.Config;

/// <summary>
/// How <c>/admin/config/{category}</c> names things: the schema category a URL segment means, the
/// label a property shows, the anchors and element ids it links by, and the hints beside a control.
/// </summary>
public static partial class ConfigNaming
{
	/// <summary>The schema category a URL segment names; an unknown one is capitalised as given.</summary>
	public static string CategoryName(string urlCategory) => urlCategory.ToLower() switch
	{
		"net" or "network" => "Net",
		"database" => "Database",
		"limit" or "limits" => "Limit",
		"command" or "commands" => "Command",
		"chat" => "Chat",
		"log" or "logging" => "Log",
		"message" or "messages" => "Message",
		"cosmetic" => "Cosmetic",
		"cost" or "costs" => "Cost",
		"attribute" or "attributes" => "Attribute",
		"flag" or "flags" => "Flag",
		"compatibility" => "Compatibility",
		"alias" or "aliases" => "Alias",
		"debug" => "Debug",
		"function" or "functions" => "Function",
		"warning" or "warnings" => "Warning",
		"restriction" or "restrictions" => "Restriction",
		"bannednames" => "BannedNames",
		"sitelock" or "sitelockrules" => "SitelockRules",
		"mssp" => "Mssp",
		"dump" => "Dump",
		"file" or "files" => "File",
		"textfile" or "textfiles" => "TextFile",
		_ => char.ToUpper(urlCategory[0]) + urlCategory[1..]
	};

	/// <summary>A category's title when the schema gives none: "TextFile" reads "Text File".</summary>
	public static string CategoryTitle(string category) => UpperCase().Replace(category, " $1").Trim();

	/// <summary>
	/// The label a property shows: the schema's display name, unless it is missing, still in
	/// snake_case, or only the property's name again — then a title made from that.
	/// </summary>
	public static string DisplayName(PropertyMetadata property) =>
		string.IsNullOrEmpty(property.DisplayName) ||
		property.DisplayName.Contains('_') ||
		property.DisplayName == property.Name
			? PropertyTitle(property.DisplayName ?? property.Name)
			: property.DisplayName;

	/// <summary>"max_depth" reads "Max Depth", "depth" reads "Depth", "MaxDepth" reads "Max Depth".</summary>
	public static string PropertyTitle(string name)
	{
		if (string.IsNullOrEmpty(name)) return name;

		if (name.Contains('_'))
			return string.Join(" ", name.Split('_').Select(word => char.ToUpper(word[0]) + word[1..].ToLower()));

		if (name.All(char.IsLower))
			return char.ToUpper(name[0]) + name[1..];

		return UpperCase().Replace(name, " $1").Trim();
	}

	/// <summary>The schema has no single-character flag; README §6.4 keys the narrow input on this pattern.</summary>
	public static bool IsSingleChar(PropertyMetadata property) => property.Pattern == "^.$";

	/// <summary>"min–max", or the one bound the schema gives; null when it gives neither.</summary>
	public static string? RangeText(int? min, int? max) => (min, max) switch
	{
		(null, null) => null,
		({ } lo, { } hi) => $"{lo}–{hi}",
		({ } lo, null) => $"≥{lo}",
		(null, { } hi) => $"≤{hi}",
	};

	/// <summary>The "group-…" anchor a group's card carries, which the table of contents links to.</summary>
	public static string GroupAnchor(string groupName) => "group-" + Slug(groupName);

	/// <summary>Lower-case, with every run of anything but letters and digits made one hyphen.</summary>
	public static string Slug(string text) => NotSlug().Replace(text.ToLowerInvariant(), "-").Trim('-');

	[GeneratedRegex("([A-Z])")]
	private static partial Regex UpperCase();

	[GeneratedRegex("[^a-z0-9]+")]
	private static partial Regex NotSlug();
}
