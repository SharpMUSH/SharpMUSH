using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Components.Schema;

/// <summary>
/// What a click on a value sends: the element's <c>command</c> filled from the clicked value, or the address of its
/// <c>dialog</c>. A click carries what the viewer meant and nothing more: the command runs as the viewer, exactly as if
/// typed, and every number it rolls or reads stays with the server.
/// </summary>
public static partial class SchemaClick
{
	/// <summary>
	/// The command <paramref name="template"/> with each <c>{name}</c> filled from <paramref name="datum"/>, or null when a
	/// name is not in it or its value is empty. A line break in a value becomes <c>%r</c>, so one click runs one command, as
	/// the viewer.
	/// </summary>
	public static string? Fill(string template, IReadOnlyDictionary<string, string> datum)
	{
		var refused = false;
		var filled = Placeholder().Replace(template, match =>
		{
			if (datum.TryGetValue(match.Groups[1].Value, out var value) && value.Length > 0) return LineBreak().Replace(value, "%r");
			refused = true;
			return string.Empty;
		});
		return refused || string.IsNullOrWhiteSpace(filled) ? null : filled.Trim();
	}

	/// <summary>
	/// The address of a <c>dialog</c> route for the value named <paramref name="key"/>: the route with <c>key</c> added to
	/// its query, or null when there is no key or the route is not on this server.
	/// </summary>
	public static string? DialogUrl(string route, string? key)
	{
		if (string.IsNullOrWhiteSpace(key) || !SharpMUSH.Library.Models.Portal.Applications.PortalSchema.OnThisServer(route)) return null;
		var separator = route.Contains('?') ? '&' : '?';
		return $"{route.TrimStart('/')}{separator}key={Uri.EscapeDataString(key)}";
	}

	/// <summary>The scalar fields of a data row (a stat tile, a table row, a chip), as text, keyed by name.</summary>
	public static IReadOnlyDictionary<string, string> Datum(JsonElement row)
	{
		var datum = new Dictionary<string, string>(StringComparer.Ordinal);
		if (row.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in row.EnumerateObject().Where(p => p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)))
				datum[property.Name] = SchemaViewRenderer.ValueToString(property.Value);
		}
		else if (row.ValueKind is JsonValueKind.String or JsonValueKind.Number)
		{
			datum["text"] = SchemaViewRenderer.ValueToString(row);
		}
		var label = datum.GetValueOrDefault("label") ?? datum.GetValueOrDefault("text");
		if (label is not null) datum.TryAdd("label", label);
		if (label is not null) datum.TryAdd("key", label);
		return datum;
	}

	/// <summary>A keyvalue row: its field name as both <c>key</c> and <c>label</c>, and its value as <c>value</c>.</summary>
	public static IReadOnlyDictionary<string, string> Datum(string field, string value) =>
		new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = field, ["label"] = field, ["value"] = value };

	/// <summary>A button: its own <c>key</c> and <c>label</c>, and its <c>values</c>.</summary>
	public static IReadOnlyDictionary<string, string> Datum(SchemaElement button)
	{
		var datum = (button.Values ?? new Dictionary<string, JsonElement>())
			.Where(p => p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
			.ToDictionary(p => p.Key, p => SchemaViewRenderer.ValueToString(p.Value), StringComparer.Ordinal);
		if (button.Key is { Length: > 0 } key) datum["key"] = key;
		if (button.Label is { Length: > 0 } label) datum["label"] = label;
		return datum;
	}

	[GeneratedRegex(@"\r\n?|\n")]
	private static partial Regex LineBreak();

	[GeneratedRegex(@"\{([A-Za-z0-9_]+)\}")]
	private static partial Regex Placeholder();
}
