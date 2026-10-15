using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Library.Models.Palette;

/// <summary>
/// A <c>PALETTE`&lt;NAME&gt;</c> attribute, read: one command the palette offers. The value is a JSON
/// object and is data; only <see cref="Lock"/> is ever evaluated, as a lock.
/// </summary>
/// <param name="Name">The attribute's last segment, upper case.</param>
/// <param name="Attribute">The <c>$</c>-command attribute the line runs, if it runs one.</param>
public sealed record PaletteEntry(
	string Name,
	string Label,
	string Line,
	string? Attribute,
	PaletteAction Action,
	string? Lock,
	string? Context,
	bool Always,
	IReadOnlyList<string> When,
	bool Urgent,
	string? Info,
	string? Group,
	string? Icon,
	string? Keywords,
	string? Help,
	int Order)
{
	/// <summary>The attribute tree palette entries live under.</summary>
	public const string Prefix = "PALETTE`";

	/// <summary>The character attribute holding its pins.</summary>
	public const string PinsAttribute = "PALETTE_PINS";

	/// <summary>How many entries one object offers; the rest are dropped by order, then name.</summary>
	public const int MaxPerObject = 32;

	/// <summary>How many entries one palette open offers; the farthest scopes are dropped first.</summary>
	public const int MaxPerOpen = 400;

	public const int MaxLabelLength = 100;
	public const int MaxLineLength = 1024;
	public const int MaxTextLength = 512;

	/// <summary>
	/// Reads the entry <paramref name="value"/> declares under <paramref name="name"/>, or why it is not one.
	/// </summary>
	public static Result<PaletteEntry> Parse(string name, string value)
	{
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(value);
		}
		catch (JsonException)
		{
			return new Error<string>("not a JSON object");
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object) return new Error<string>("not a JSON object");
			var root = document.RootElement;

			if (Text(root, "label") is not { Length: > 0 } label) return new Error<string>("no label");
			// The line keeps a trailing space, which an insert leaves for the player to type after.
			if (Text(root, "line", trimEnd: false) is not { } line || line.Trim().Length == 0) return new Error<string>("no line");
			if (label.Length > MaxLabelLength) return new Error<string>($"label longer than {MaxLabelLength}");
			if (line.Length > MaxLineLength) return new Error<string>($"line longer than {MaxLineLength}");
			// A client sends the line as it stands, and a line break would send a second line.
			if (line.Any(char.IsControl) || label.Any(char.IsControl)) return new Error<string>("a line break in the label or line");

			var action = Text(root, "action")?.ToLowerInvariant() switch
			{
				null or "" or "run" => PaletteAction.Run,
				"insert" => PaletteAction.Insert,
				_ => (PaletteAction?)null
			};
			if (action is not { } chosen) return new Error<string>("action is neither run nor insert");

			return new PaletteEntry(
				name.ToUpperInvariant(),
				label,
				line,
				Text(root, "attr")?.ToUpperInvariant() is { Length: > 0 } attribute ? attribute : null,
				chosen,
				Text(root, "lock") is { Length: > 0 } entryLock ? entryLock : null,
				Text(root, "context")?.ToLowerInvariant() is { Length: > 0 } context ? context : null,
				Flag(root, "always"),
				Words(root, "when"),
				Flag(root, "urgent"),
				Short(root, "info"),
				Short(root, "group"),
				Short(root, "icon"),
				Words(root, "keywords") is { Count: > 0 } keywords ? Bounded(string.Join(' ', keywords)) : null,
				Short(root, "help"),
				root.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Number
					&& order.TryGetInt32(out var number) ? number : 0);
		}
	}

	private static string? Text(JsonElement root, string key, bool trimEnd = true)
		=> root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
			? trimEnd ? value.GetString()?.Trim() : value.GetString()?.TrimStart()
			: null;

	private static string? Short(JsonElement root, string key)
		=> Text(root, key) is { Length: > 0 } text && !text.Any(char.IsControl) ? Bounded(text) : null;

	private static string Bounded(string text) => text.Length > MaxTextLength ? text[..MaxTextLength] : text;

	private static bool Flag(JsonElement root, string key)
		=> root.TryGetProperty(key, out var value) && value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.Number => value.TryGetDouble(out var number) && number != 0,
			JsonValueKind.String => value.GetString() is "1" or "true",
			_ => false
		};

	/// <summary>A list of words, written as a JSON array or as one space-separated string, lower case.</summary>
	private static IReadOnlyList<string> Words(JsonElement root, string key)
	{
		if (!root.TryGetProperty(key, out var value)) return [];
		IEnumerable<string?> words = value.ValueKind switch
		{
			JsonValueKind.Array => value.EnumerateArray()
				.Where(item => item.ValueKind == JsonValueKind.String)
				.Select(item => item.GetString()),
			JsonValueKind.String => value.GetString()?.Split(' ') ?? [],
			_ => []
		};
		return [.. words
			.Select(word => word?.Trim().ToLowerInvariant())
			.OfType<string>()
			.Where(word => word.Length > 0)
			.Distinct()];
	}
}
