using System.Text.Json;

namespace SharpMUSH.Client.Components.Schema;

/// <summary>
/// How a schema field's value is held while it is edited, and how it goes back to JSON. A text field holds a
/// string, a switch a bool, a list or multiselect a list of strings, a number a double (or the JSON number it was
/// given), and a rows or graph field the JSON it was given.
/// </summary>
public static class SchemaValues
{
	public static string AsString(object? value) => value switch
	{
		IEnumerable<string> list => string.Join(", ", list),
		_ => value?.ToString() ?? ""
	};

	public static bool AsBool(object? value) => value is true;

	public static double? AsNumber(object? value) => value switch
	{
		null => null,
		JsonElement { ValueKind: JsonValueKind.Number } number when number.TryGetDouble(out var d) => d,
		double d => d,
		_ => double.TryParse(value.ToString(), out var parsed) ? parsed : null
	};

	public static IEnumerable<string> AsMulti(object? value) => value is IEnumerable<string> list ? list : [];

	public static DateTime? AsDate(object? value) => DateTime.TryParse(AsString(value), out var d) ? d : null;

	public static string? FromDate(DateTime? date) => date?.ToString("yyyy-MM-dd");

	/// <summary>The JSON a value held as an object or array, else <c>null</c>.</summary>
	public static JsonElement? AsJson(object? value) => value switch
	{
		JsonElement { ValueKind: JsonValueKind.Object or JsonValueKind.Array } element => element,
		IEnumerable<string> list => JsonSerializer.SerializeToElement(list),
		_ => null
	};

	/// <summary>A value as the form holds it: a JSON list of strings as its items (multiselect, list), a JSON
	/// boolean as a bool; numbers, objects and mixed arrays retain their JSON types.</summary>
	public static object? FromJson(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.Array when value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) => value.EnumerateArray().Select(item => item.GetString()!).ToList(),
		JsonValueKind.Array => value.Clone(),
		JsonValueKind.Number => value.Clone(),
		JsonValueKind.Null or JsonValueKind.Undefined => null,
		JsonValueKind.Object => value.Clone(),
		JsonValueKind.True => true,
		JsonValueKind.False => false,
		_ => SchemaViewRenderer.ValueToString(value)
	};

	/// <summary>A held value as JSON, the way it is posted.</summary>
	public static JsonElement ToJson(object? value) => value switch
	{
		JsonElement element => element,
		null => JsonSerializer.SerializeToElement<object?>(null),
		_ => JsonSerializer.SerializeToElement(value, value.GetType())
	};
}
