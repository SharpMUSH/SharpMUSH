using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models.Packages;

/// <summary>Where a preference group keeps a player's values.</summary>
public enum PreferenceScope
{
	/// <summary>On the account (<c>#acct:&lt;key&gt;</c>): every character on it, and every computer, sees the same values.</summary>
	Account,

	/// <summary>On the character: each character has its own.</summary>
	Character
}

/// <summary>What a preference holds, and so how a value written to it is checked.</summary>
public enum PreferenceType
{
	/// <summary>Any text, up to the preference's <c>max_length</c>.</summary>
	Text,

	/// <summary>A decimal number, within <c>min</c> and <c>max</c> when given.</summary>
	Number,

	/// <summary>A whole number, within <c>min</c> and <c>max</c> when given.</summary>
	Integer,

	/// <summary>On or off: stored as <c>1</c> or <c>0</c>, as softcode writes a truth value.</summary>
	Boolean,

	/// <summary>One of the preference's <c>values</c>.</summary>
	Choice,

	/// <summary>A JSON value (an object, a list, a string, a number, true, false or null), up to <c>max_length</c>.</summary>
	Json
}

/// <summary>
/// A group of preferences a package declares (the <c>preferences:</c> block, format 1.21): settings a player chooses
/// for themselves, kept as attributes on their account or character, read and written through <c>api/preferences</c>,
/// <c>@preference</c> and <c>pref()</c>.
/// </summary>
/// <param name="Name">The group's name within its package, lowercase, such as <c>keyboard</c>.</param>
/// <param name="Scope">Whether the values are on the account or on each character.</param>
/// <param name="Label">What the portal calls the group, or empty for its name.</param>
/// <param name="Description">What the group is for, or empty.</param>
/// <param name="Keys">The preferences in the group, in manifest order.</param>
public sealed record PackagePreferenceGroupSpec(
	string Name,
	PreferenceScope Scope,
	string Label,
	string Description,
	IReadOnlyList<PackagePreferenceSpec> Keys)
{
	/// <summary>The preference named <paramref name="key"/>, matched without case, or null.</summary>
	public PackagePreferenceSpec? Find(string key)
		=> Keys.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));

	/// <summary>Whether <paramref name="other"/> holds the same values, the lists compared by content.</summary>
	public bool SameAs(PackagePreferenceGroupSpec other)
		=> Name == other.Name && Scope == other.Scope && Label == other.Label && Description == other.Description
			&& Keys.Count == other.Keys.Count && Keys.Zip(other.Keys).All(pair => pair.First.SameAs(pair.Second));
}

/// <summary>One preference in a <see cref="PackagePreferenceGroupSpec"/>.</summary>
/// <param name="Key">The preference's name, lowercase, such as <c>zoom_in</c>.</param>
/// <param name="Type">What it holds.</param>
/// <param name="Default">The value it has until the player sets one, in its stored form; null for none (empty).</param>
/// <param name="Values">For a <see cref="PreferenceType.Choice"/>, the values it allows; empty otherwise.</param>
/// <param name="Min">For a number, the lowest value allowed, or null.</param>
/// <param name="Max">For a number, the highest value allowed, or null.</param>
/// <param name="MaxLength">For text or JSON, the most characters a value may have.</param>
/// <param name="Label">What the portal calls it, or empty for its key.</param>
/// <param name="Description">What it changes, or empty.</param>
public sealed record PackagePreferenceSpec(
	string Key,
	PreferenceType Type,
	string? Default,
	IReadOnlyList<string> Values,
	decimal? Min,
	decimal? Max,
	int MaxLength,
	string Label,
	string Description)
{
	/// <summary>Whether <paramref name="other"/> holds the same values, the lists compared by content.</summary>
	public bool SameAs(PackagePreferenceSpec other)
		=> this with { Values = other.Values } == other && Values.SequenceEqual(other.Values, StringComparer.Ordinal);
}

/// <summary>
/// The rules every reader and writer of a preference shares: its names, where it is stored, and how a value is checked
/// and turned into its stored form.
/// </summary>
public static partial class Preferences
{
	/// <summary>The attribute every preference is kept under: <c>PREF`&lt;PACKAGE&gt;`&lt;GROUP&gt;`&lt;KEY&gt;</c>.</summary>
	public const string AttributeRoot = "PREF";

	/// <summary>The longest group or key name.</summary>
	public const int MaxNameLength = 32;

	/// <summary>The most preferences one group may hold.</summary>
	public const int MaxKeys = 64;

	/// <summary>The most choices one preference may list.</summary>
	public const int MaxChoices = 100;

	/// <summary>The <c>max_length</c> a text or JSON preference has when it names none.</summary>
	public const int DefaultMaxLength = 4096;

	/// <summary>The largest <c>max_length</c> a preference may name: an attribute value's limit.</summary>
	public const int LargestMaxLength = 65536;

	/// <summary>The type names a manifest writes.</summary>
	public static IReadOnlyList<string> TypeNames { get; } = ["text", "number", "integer", "boolean", "choice", "json"];

	[GeneratedRegex("^[a-z][a-z0-9-]*$")]
	private static partial Regex GroupNameRegex();

	[GeneratedRegex("^[a-z][a-z0-9_]*$")]
	private static partial Regex KeyNameRegex();

	/// <summary>Whether <paramref name="name"/> may name a group: lowercase letters, digits and hyphens, a letter first.</summary>
	public static bool IsValidGroupName(string name) => name.Length <= MaxNameLength && GroupNameRegex().IsMatch(name);

	/// <summary>Whether <paramref name="name"/> may name a preference: lowercase letters, digits and underscores, a letter first.</summary>
	public static bool IsValidKeyName(string name) => name.Length <= MaxNameLength && KeyNameRegex().IsMatch(name);

	/// <summary>The type <paramref name="name"/> names, or null.</summary>
	public static PreferenceType? ParseType(string? name) => name?.Trim().ToLowerInvariant() switch
	{
		"text" => PreferenceType.Text,
		"number" => PreferenceType.Number,
		"integer" => PreferenceType.Integer,
		"boolean" => PreferenceType.Boolean,
		"choice" => PreferenceType.Choice,
		"json" => PreferenceType.Json,
		_ => null
	};

	/// <summary>How a manifest writes <paramref name="type"/>.</summary>
	public static string TypeName(PreferenceType type) => type.ToString().ToLowerInvariant();

	/// <summary>The scope <paramref name="name"/> names (<c>account</c> or <c>character</c>), or null.</summary>
	public static PreferenceScope? ParseScope(string? name) => name?.Trim().ToLowerInvariant() switch
	{
		"account" => PreferenceScope.Account,
		"character" => PreferenceScope.Character,
		_ => null
	};

	/// <summary>How a manifest writes <paramref name="scope"/>.</summary>
	public static string ScopeName(PreferenceScope scope) => scope.ToString().ToLowerInvariant();

	/// <summary>A group's id: <c>&lt;package&gt;.&lt;group&gt;</c>.</summary>
	public static string GroupId(string packageId, string group) => $"{packageId}.{group}";

	/// <summary>
	/// The package and group <paramref name="groupId"/> names, read without case; <see cref="NotFound"/> when it is not
	/// <c>&lt;package&gt;.&lt;group&gt;</c>.
	/// </summary>
	public static Found<PreferenceGroupRef> ParseGroupId(string groupId)
	{
		var dot = groupId.LastIndexOf('.');
		if (dot <= 0 || dot == groupId.Length - 1)
		{
			return new NotFound();
		}

		var package = groupId[..dot].Trim().ToLowerInvariant();
		var group = groupId[(dot + 1)..].Trim().ToLowerInvariant();
		return IsValidGroupName(group) && GroupNameRegex().IsMatch(package)
			? new PreferenceGroupRef(package, group)
			: new NotFound();
	}

	/// <summary>The attribute a preference is kept in, as path segments.</summary>
	public static string[] AttributePath(string packageId, string group, string key)
		=> [AttributeRoot, packageId.ToUpperInvariant(), group.ToUpperInvariant(), key.ToUpperInvariant()];

	/// <summary>The attribute a group's preferences are kept under, as path segments.</summary>
	public static string[] GroupAttributePath(string packageId, string group)
		=> [AttributeRoot, packageId.ToUpperInvariant(), group.ToUpperInvariant()];

	/// <summary>
	/// <paramref name="value"/> as <paramref name="spec"/> stores it, or why it is refused. Text is kept as written; a
	/// number in its plain invariant form; a truth value as <c>1</c> or <c>0</c> (from 1, 0, true, false, yes, no, on
	/// or off); a choice as the allowed value it names, matched without case; JSON compacted.
	/// </summary>
	public static Result<string> Check(PackagePreferenceSpec spec, string value)
	{
		switch (spec.Type)
		{
			case PreferenceType.Text:
				return value.Length > spec.MaxLength
					? new Error<string>($"{spec.Key} is at most {spec.MaxLength} characters long.")
					: value;
			case PreferenceType.Number or PreferenceType.Integer:
				if (!decimal.TryParse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
						CultureInfo.InvariantCulture, out var number)
					|| (spec.Type == PreferenceType.Integer && number != decimal.Truncate(number)))
				{
					return new Error<string>(spec.Type == PreferenceType.Integer
						? $"{spec.Key} is a whole number."
						: $"{spec.Key} is a number.");
				}

				if (spec.Min is { } min && number < min)
				{
					return new Error<string>($"{spec.Key} is at least {Format(min)}.");
				}

				return spec.Max is { } max && number > max
					? new Error<string>($"{spec.Key} is at most {Format(max)}.")
					: Format(number);
			case PreferenceType.Boolean:
				return value.Trim().ToLowerInvariant() switch
				{
					"1" or "true" or "yes" or "on" => "1",
					"0" or "false" or "no" or "off" => "0",
					_ => new Error<string>($"{spec.Key} is on or off: 1 or 0.")
				};
			case PreferenceType.Choice:
				return spec.Values.FirstOrDefault(v => string.Equals(v, value.Trim(), StringComparison.OrdinalIgnoreCase)) is { } choice
					? choice
					: new Error<string>($"{spec.Key} is one of: {string.Join(", ", spec.Values)}.");
			default:
				return CheckJson(spec, value);
		}
	}

	/// <summary>Compact JSON that leaves letters outside ASCII as they are written.</summary>
	private static readonly JsonSerializerOptions CompactJson = new()
	{
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static Result<string> CheckJson(PackagePreferenceSpec spec, string value)
	{
		try
		{
			using var document = JsonDocument.Parse(value);
			var compact = JsonSerializer.Serialize(document.RootElement, CompactJson);
			return compact.Length > spec.MaxLength
				? new Error<string>($"{spec.Key} is at most {spec.MaxLength} characters long.")
				: compact;
		}
		catch (JsonException)
		{
			return new Error<string>($"{spec.Key} is JSON, such as {{\"a\":1}} or [1,2].");
		}
	}

	/// <summary>
	/// The value a preference has: <paramref name="stored"/> when it is one <paramref name="spec"/> still takes (a value
	/// written by hand, or before the package changed the preference, may not be), else its default.
	/// </summary>
	public static string Effective(PackagePreferenceSpec spec, string? stored)
		=> stored is not null && Check(spec, stored) is string accepted
			? accepted
			: spec.Default ?? string.Empty;

	/// <summary><paramref name="number"/> as a preference stores it: invariant, no trailing zeros.</summary>
	public static string Format(decimal number)
		=> (number / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
}

/// <summary>A preference group named by its id, <c>&lt;package&gt;.&lt;group&gt;</c>.</summary>
public readonly record struct PreferenceGroupRef(string Package, string Group)
{
	/// <summary>The group's id.</summary>
	public override string ToString() => Preferences.GroupId(Package, Group);
}

/// <summary>The stored form of the groups a package declared, as a registry row keeps them.</summary>
public static class PreferenceGroupsJson
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	/// <summary>The stored form.</summary>
	public static string ToJson(IReadOnlyList<PackagePreferenceGroupSpec> groups) => JsonSerializer.Serialize(groups, Json);

	/// <summary>Reads what <see cref="ToJson"/> wrote; nothing for text that is not that.</summary>
	public static IReadOnlyList<PackagePreferenceGroupSpec> FromJson(string json)
	{
		try
		{
			return JsonSerializer.Deserialize<List<PackagePreferenceGroupSpec>>(json, Json) ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}
}
