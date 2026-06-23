using System.Text.Json;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Parses an OOB array payload into <see cref="OobEntry"/> items. The client imposes no schema
/// beyond "an array under <paramref name="arrayProperty"/> of objects or bare strings"; anything
/// else yields an empty list rather than throwing.
/// </summary>
public static class OobEntryParser
{
	public static IReadOnlyList<OobEntry> Parse(string? dataJson, string arrayProperty)
	{
		if (string.IsNullOrWhiteSpace(dataJson)) return [];

		try
		{
			using var doc = JsonDocument.Parse(dataJson);
			if (doc.RootElement.ValueKind != JsonValueKind.Object
				|| !doc.RootElement.TryGetProperty(arrayProperty, out var arr)
				|| arr.ValueKind != JsonValueKind.Array)
				return [];

			var result = new List<OobEntry>();
			foreach (var item in arr.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.String)
				{
					result.Add(new OobEntry(string.Empty, item.GetString() ?? string.Empty, null));
				}
				else if (item.ValueKind == JsonValueKind.Object)
				{
					var dbref = item.TryGetProperty("dbref", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : string.Empty;
					var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : string.Empty;
					var cmd = item.TryGetProperty("cmd", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
					result.Add(new OobEntry(dbref, name, cmd));
				}
			}
			return result;
		}
		catch (JsonException)
		{
			return [];
		}
	}
}
