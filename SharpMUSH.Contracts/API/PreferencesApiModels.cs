using System.Globalization;
using System.Text.Json;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.API;

/// <summary>
/// A preference group as <c>api/preferences</c> answers it: what the package declares, and the caller's values.
/// </summary>
/// <param name="Id">The group's id, <c>&lt;package&gt;.&lt;group&gt;</c>.</param>
/// <param name="Package">The package that declares it.</param>
/// <param name="Scope"><c>account</c> or <c>character</c>: where the values are kept.</param>
/// <param name="Label">What to call it; its name when the package gives none.</param>
/// <param name="Description">What it is for, or empty.</param>
/// <param name="Values">Each key's value in effect, typed as JSON (a string, number, true or false, or any JSON value for a json preference); null when <paramref name="Unavailable"/>.</param>
/// <param name="Preferences">Each preference's declaration and value, in declaration order.</param>
/// <param name="Unavailable">Why there are no values to show, such as a character group with no character chosen; null when there are.</param>
public sealed record PreferenceGroupResponse(
	string Id,
	string Package,
	string Scope,
	string Label,
	string Description,
	IReadOnlyDictionary<string, JsonElement>? Values,
	IReadOnlyList<PreferenceResponse> Preferences,
	string? Unavailable);

/// <summary>One preference: what it takes, and the caller's value.</summary>
/// <param name="Key">Its name.</param>
/// <param name="Type">text, number, integer, boolean, choice or json.</param>
/// <param name="Label">What to call it; its key when the package gives none.</param>
/// <param name="Description">What it changes, or empty.</param>
/// <param name="Value">The value in effect, typed; null when there is none to show.</param>
/// <param name="Default">The default, typed; null when it has none.</param>
/// <param name="IsSet">Whether the caller set it, rather than having the default.</param>
/// <param name="Choices">A choice's allowed values; empty otherwise.</param>
/// <param name="Min">A number's lowest allowed value, or null.</param>
/// <param name="Max">A number's highest allowed value, or null.</param>
/// <param name="MaxLength">Text's or JSON's longest allowed value, or null.</param>
public sealed record PreferenceResponse(
	string Key,
	string Type,
	string Label,
	string Description,
	JsonElement? Value,
	JsonElement? Default,
	bool IsSet,
	IReadOnlyList<string> Choices,
	decimal? Min,
	decimal? Max,
	int? MaxLength);

/// <summary>
/// The body of <c>PUT api/preferences/{group}</c>: the values to set, by key. Keys left out keep their values. A value
/// may be written typed (<c>true</c>, <c>12</c>) or as text (<c>"on"</c>, <c>"12"</c>).
/// </summary>
public sealed record PreferenceUpdateRequest(IReadOnlyDictionary<string, JsonElement> Values);

/// <summary>How preference values cross the API: the stored text one way, typed JSON the other.</summary>
public static class PreferenceJson
{
	/// <summary>
	/// <paramref name="stored"/> (a preference's stored form) typed for <paramref name="spec"/>; JSON null for an empty
	/// value of any type but text, which is one with no default that the player has not set.
	/// </summary>
	public static JsonElement Typed(PackagePreferenceSpec spec, string stored)
	{
		var json = spec.Type switch
		{
			not PreferenceType.Text when stored.Length == 0 => "null",
			PreferenceType.Number or PreferenceType.Integer
				when decimal.TryParse(stored, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)
				=> stored,
			PreferenceType.Boolean => stored == "1" ? "true" : "false",
			PreferenceType.Json when IsJson(stored) => stored,
			_ => JsonSerializer.Serialize(stored)
		};
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	/// <summary>
	/// The text a request's <paramref name="value"/> for <paramref name="spec"/> is checked as. A json preference takes
	/// the value itself, so <c>"dark"</c> is the JSON string. Any other takes a string as it is, <c>true</c>/<c>false</c>
	/// as 1/0, and anything else as its JSON.
	/// </summary>
	public static string Text(PackagePreferenceSpec spec, JsonElement value) => spec.Type == PreferenceType.Json
		? value.GetRawText()
		: value.ValueKind switch
		{
			JsonValueKind.String => value.GetString() ?? string.Empty,
			JsonValueKind.True => "1",
			JsonValueKind.False => "0",
			_ => value.GetRawText()
		};

	private static bool IsJson(string text)
	{
		try
		{
			using var _ = JsonDocument.Parse(text);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
