using System.Globalization;
using System.Text.Json;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Models.Configuration;

/// <summary>
/// One entry of a dictionary-valued config property, as the editor holds it while being typed.
/// </summary>
/// <remarks>
/// A list rather than a dictionary on purpose: two rows may carry the same key, or none at all,
/// halfway through an edit. Keys are deduplicated and blanks dropped only when the draft is
/// committed back to a value — see <see cref="ConfigValues.ToDictionary"/>.
/// </remarks>
public sealed class ConfigDictEntry
{
	public string Key { get; set; } = string.Empty;

	public List<string> Values { get; set; } = [];

	/// <summary>What the operator has typed into this row's "add a value" box but not yet committed.</summary>
	public string ValueDraft { get; set; } = string.Empty;
}

/// <summary>
/// Reading and coercing the values behind <c>/admin/config/{category}</c>.
/// </summary>
/// <remarks>
/// <para>The values arrive as <see cref="object"/> — sometimes a CLR value the client set, sometimes a
/// <see cref="JsonElement"/> straight from the server's response, and typed differently from what the
/// property metadata claims (a <c>uint</c> where the editor wants an <c>int</c>, an array where it
/// wants a list of strings). Every accessor here is the same shape: the current value if it is
/// usable, the property's default if it is not.</para>
///
/// <para>This lived in <c>DynamicConfig.razor</c>'s 691-line <c>@code</c> block, where none of it
/// could be reached by a test — which is why it is out here now, as pure functions over a value map.
/// Nothing in this file touches rendering or state.</para>
/// </remarks>
public static class ConfigValues
{
	public static bool Bool(IReadOnlyDictionary<string, object?> values, PropertyMetadata property) =>
		values.TryGetValue(property.Path, out var value) && value is bool current
			? current
			: property.DefaultValue is bool fallback && fallback;

	/// <summary>
	/// Null only when the stored value is explicitly null — an absent or unusable one falls back to
	/// the property's default, and then to zero. <c>uint</c> and <c>long</c> are accepted because the
	/// server's options carry both and the editor's field is an <c>int</c>.
	/// </summary>
	public static int? NullableInt(IReadOnlyDictionary<string, object?> values, PropertyMetadata property)
	{
		if (values.TryGetValue(property.Path, out var value))
		{
			switch (value)
			{
				case null: return null;
				case int current: return current;
				case uint current: return (int)current;
				case long current: return (int)current;
				case JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed):
					return parsed;
			}
		}

		return property.DefaultValue switch
		{
			int fallback => fallback,
			uint fallback => (int)fallback,
			_ => 0
		};
	}

	public static double? NullableDouble(IReadOnlyDictionary<string, object?> values, PropertyMetadata property)
	{
		if (values.TryGetValue(property.Path, out var value))
		{
			switch (value)
			{
				case null: return null;
				case double current: return current;
				case JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDouble(out var parsed):
					return parsed;
			}
		}

		return property.DefaultValue is double fallback ? fallback : 0.0;
	}

	public static string String(IReadOnlyDictionary<string, object?> values, PropertyMetadata property) =>
		values.TryGetValue(property.Path, out var value) && value is not null
			? value.ToString() ?? string.Empty
			: property.DefaultValue?.ToString() ?? string.Empty;

	public static List<string> StringList(IReadOnlyDictionary<string, object?> values, PropertyMetadata property) =>
		values.TryGetValue(property.Path, out var value) && value is not null
			? NonBlank(CoerceToStringList(value))
			: [];

	/// <summary>
	/// What the numeric input shows. The invariant culture is not cosmetic: the same string is parsed
	/// back by <see cref="ParseNumeric"/>, and a locale that writes <c>1,5</c> would round-trip a
	/// double into a different number or into nothing at all.
	/// </summary>
	public static string NumericString(IReadOnlyDictionary<string, object?> values, PropertyMetadata property) =>
		property.Type == "integer"
			? NullableInt(values, property)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
			: NullableDouble(values, property)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

	/// <summary>
	/// The value a typed numeric field should store, or <see langword="false"/> with no value when the
	/// text is not a number yet — mid-edit states like <c>"-"</c> must leave the stored value alone
	/// rather than blanking it under the operator's cursor.
	/// </summary>
	/// <remarks>
	/// <para>Cleared text is a real edit, not an unparseable one: it means null for an optional
	/// property, and zero for a required one, which has nowhere to put "no value".</para>
	///
	/// <para>The zero for a required integer is boxed as an <see cref="int"/>, deliberately and not
	/// incidentally. The inlined version of this wrote <c>prop.Required ? (prop.Type == "integer" ? 0
	/// : 0.0) : null</c>, whose ternary takes <see cref="double"/> as its common type — so clearing a
	/// required integer field stored a boxed <c>0.0</c> where every other path on that property
	/// stores an <see cref="int"/>. <c>Equals(0.0, 0)</c> is false, so the field then counted as
	/// changed against an unchanged original, and the save request sent a double for an integer
	/// option.</para>
	/// </remarks>
	public static bool ParseNumeric(PropertyMetadata property, string? raw, out object? value)
	{
		raw = raw?.Trim();
		if (string.IsNullOrEmpty(raw))
		{
			value = property.Required ? property.Type == "integer" ? 0 : (object)0.0 : null;
			return true;
		}

		if (property.Type == "integer")
		{
			var parsed = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer);
			value = parsed ? integer : null;
			return parsed;
		}

		var ok = double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number);
		value = ok ? number : null;
		return ok;
	}

	/// <summary>The bound for a numeric field, across the shapes a min/max arrives in.</summary>
	public static int? IntBound(object? bound) => bound switch
	{
		int value => value,
		uint value => (int)value,
		JsonElement { ValueKind: JsonValueKind.Number } element => element.GetInt32(),
		_ => null
	};

	/// <summary>Anything list-shaped, as strings. An unrecognised shape is an empty list, never null.</summary>
	public static List<string> CoerceToStringList(object value) => value switch
	{
		string[] array => [.. array],
		IEnumerable<string> strings => [.. strings],
		JsonElement { ValueKind: JsonValueKind.Array } element =>
			[.. element.EnumerateArray().Select(x => x.GetString() ?? string.Empty)],
		System.Collections.IEnumerable enumerable and not string =>
			[.. enumerable.Cast<object?>().Select(x => x?.ToString() ?? string.Empty)],
		_ => []
	};

	/// <summary>Anything dictionary-shaped, as editable rows. An unrecognised shape yields no rows.</summary>
	public static List<ConfigDictEntry> BuildDictEntries(object? value)
	{
		var result = new List<ConfigDictEntry>();
		switch (value)
		{
			case Dictionary<string, string[]> dictionary:
				foreach (var pair in dictionary)
					result.Add(new ConfigDictEntry { Key = pair.Key, Values = NonBlank(pair.Value) });
				break;
			case JsonElement { ValueKind: JsonValueKind.Object } element:
				foreach (var property in element.EnumerateObject())
					result.Add(new ConfigDictEntry
					{
						Key = property.Name,
						Values = NonBlank(CoerceToStringList(property.Value))
					});
				break;
			case System.Collections.IDictionary dictionary:
				foreach (System.Collections.DictionaryEntry entry in dictionary)
					result.Add(new ConfigDictEntry
					{
						Key = entry.Key?.ToString() ?? string.Empty,
						Values = entry.Value is not null ? NonBlank(CoerceToStringList(entry.Value)) : []
					});
				break;
		}

		return result;
	}

	/// <summary>
	/// Commits editable rows back to a value. Rows with a blank key are dropped — a half-typed row is
	/// not a setting — and a repeated key keeps the last row, matching what the operator sees.
	/// </summary>
	public static Dictionary<string, string[]> ToDictionary(IEnumerable<ConfigDictEntry> entries)
	{
		var result = new Dictionary<string, string[]>();
		foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.Key)))
			result[entry.Key] = [.. entry.Values];

		return result;
	}

	public static List<string> NonBlank(IEnumerable<string> values) =>
		[.. values.Where(value => !string.IsNullOrWhiteSpace(value))];

	/// <summary>
	/// The properties whose value differs from the one loaded. This is what the "N unsaved changes"
	/// counter reports and what the save request sends, so the two cannot disagree about which
	/// properties changed.
	/// </summary>
	public static Dictionary<string, object?> Changes(
		IReadOnlyDictionary<string, object?> current, IReadOnlyDictionary<string, object?> original) =>
		current
			.Where(pair => !Equals(pair.Value, original.GetValueOrDefault(pair.Key)))
			.ToDictionary(pair => pair.Key, pair => pair.Value);
}
