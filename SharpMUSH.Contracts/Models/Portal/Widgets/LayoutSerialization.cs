using System.Text.Json;

namespace SharpMUSH.Library.Models.Portal.Widgets;

/// <summary>
/// Shared (de)serialization for the persisted layout blob, used by every database provider so the
/// stored shape stays identical across backends. A <see cref="LayoutConfiguration"/> is stored as a
/// single JSON string keyed by scope; these helpers are the single definition of how that string is
/// produced and read back.
/// </summary>
public static class LayoutSerialization
{
	/// <summary>JSON options for the stored blob. camelCase to match the client's wire format.</summary>
	public static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	/// <summary>Serializes a layout to its stored JSON form.</summary>
	public static string Serialize(LayoutConfiguration layout)
		=> JsonSerializer.Serialize(layout, Options);

	/// <summary>Deserializes a stored JSON blob back to a layout, or null when the blob is empty/corrupt.</summary>
	public static LayoutConfiguration? Deserialize(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<LayoutConfiguration>(json, Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
