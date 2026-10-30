using System.Globalization;
using System.Text.Json.Nodes;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.World.Common;

/// <summary>A terrain type as the map's legend describes it.</summary>
/// <param name="Closed">Why a place of this type cannot be entered; null when it can.</param>
/// <param name="Char">The character a text drawing shows for the type, whatever its icon is.</param>
public sealed record WorldTerrain(string Type, string Label, string? Tone, string? Icon, string? Closed, string? Char = null)
{
	/// <summary>
	/// The one character a text drawing shows for the type: its <c>`CHAR</c>, else its icon when that is a character
	/// or two, else its label's first letter.
	/// </summary>
	public string Glyph => Char is { Length: > 0 } glyph
		? StringInfo.GetNextTextElement(glyph)
		: Icon is { Length: > 0 } icon && new StringInfo(icon).LengthInTextElements <= 2
		? StringInfo.GetNextTextElement(icon)
		: Label.Length > 0 ? Label[..1].ToUpperInvariant() : "?";
}

/// <summary>A lock on moving between two terrain types: its key, and what a refused mover is told.</summary>
public sealed record WorldTerrainLock(string Key, string? Reason);

/// <summary>
/// The legend of a stored map: <c>MAP`LEGEND`&lt;type&gt;`LABEL</c>, <c>`TONE</c>, <c>`ICON</c>, <c>`CHAR</c> and <c>`CLOSED</c> on
/// the map, read as the map, as the World map reads them, and <c>`LOCK</c> and <c>`FAILURE</c>, which gate moving
/// into a type. A type the legend does not name is labelled by itself.
/// </summary>
public sealed class WorldLegend
{
	/// <summary>The attribute naming how the World map draws an object's places.</summary>
	public const string KindAttribute = "MAP`KIND";

	private readonly Func<string, ValueTask<string?>> _read;
	private readonly Dictionary<string, WorldTerrain> _terrain = new(StringComparer.Ordinal);

	public WorldLegend(IAttributeService attributes, AnySharpObject map) : this(async name =>
		await attributes.GetAttributeAsync(map, map, name, IAttributeService.AttributeMode.Read, parent: false)
			is SharpAttribute[] { Length: > 0 } chain && chain[^1].Value.ToPlainText().Trim() is { Length: > 0 } value
			? value
			: null)
	{
	}

	/// <param name="read">The text of a legend attribute on the map by its name, or null when it has none.</param>
	public WorldLegend(Func<string, ValueTask<string?>> read) => _read = read;

	public async ValueTask<WorldTerrain> TerrainAsync(string type)
	{
		if (_terrain.TryGetValue(type, out var known)) return known;
		var label = await ValueAsync(type, "LABEL");
		var terrain = new WorldTerrain(type, label ?? type, (await ValueAsync(type, "TONE"))?.ToLowerInvariant(),
			await ValueAsync(type, "ICON"), await ValueAsync(type, "CLOSED"), await ValueAsync(type, "CHAR"));
		_terrain[type] = terrain;
		return terrain;
	}

	/// <summary>
	/// The lock on entering a place of type <paramref name="to"/> from one of type <paramref name="from"/> (null when
	/// nothing is painted there): <c>`LOCK`&lt;from&gt;</c> of <paramref name="to"/>'s legend, else, coming from
	/// another type, its <c>`LOCK</c>. Its reason is <c>`FAILURE`&lt;from&gt;</c>, else <c>`FAILURE</c>. Null when no
	/// key applies.
	/// </summary>
	public async ValueTask<WorldTerrainLock?> LockAsync(string to, string? from)
	{
		var key = from is null ? null : await ValueAsync(to, $"LOCK`{from.ToUpperInvariant()}");
		if (key is null && from != to) key = await ValueAsync(to, "LOCK");
		if (key is null) return null;
		var reason = (from is null ? null : await ValueAsync(to, $"FAILURE`{from.ToUpperInvariant()}")) ?? await ValueAsync(to, "FAILURE");
		return new WorldTerrainLock(key, reason);
	}

	private ValueTask<string?> ValueAsync(string type, string field) => _read($"MAP`LEGEND`{type.ToUpperInvariant()}`{field}");
}

/// <summary>Reads the reserved keys of a place's data: <c>render</c>, how it is drawn, and <c>info</c>, its panel.</summary>
public static class WorldPlaceData
{
	/// <summary>The most bytes of JSON one place may hold.</summary>
	public const int DefaultMaxBytes = 65536;

	public static JsonObject? Parse(string? data)
	{
		if (string.IsNullOrWhiteSpace(data)) return null;
		try
		{
			return JsonNode.Parse(data) as JsonObject;
		}
		catch (System.Text.Json.JsonException)
		{
			return null;
		}
	}

	public static string? Render(JsonObject? data, string key) =>
		data?["render"] is JsonObject render && render[key] is JsonValue value && value.TryGetValue<string>(out var text)
		&& text.Trim() is { Length: > 0 } trimmed
			? trimmed
			: null;

	public static bool Hidden(JsonObject? data) =>
		data?["render"] is JsonObject render && render["hidden"] is JsonValue value && value.TryGetValue<bool>(out var hidden) && hidden;

	/// <summary>The panel's labelled values: an object of label to value, or a list of <c>{label, value}</c>.</summary>
	public static IReadOnlyList<MapInfoDto> Info(JsonObject? data) => data?["info"] switch
	{
		JsonObject info => [.. info.Where(entry => entry.Value is JsonValue).Select(entry => new MapInfoDto(entry.Key, Text(entry.Value)))],
		JsonArray list =>
		[
			.. list.OfType<JsonObject>()
				.Where(entry => entry["label"] is JsonValue)
				.Select(entry => new MapInfoDto(Text(entry["label"]), Text(entry["value"]))),
		],
		_ => [],
	};

	private static string Text(JsonNode? node) => node switch
	{
		JsonValue value when value.TryGetValue<string>(out var text) => text,
		null => "",
		_ => node.ToJsonString(),
	};

	/// <summary>
	/// <paramref name="patch"/> merged into <paramref name="target"/> as RFC 7386 has it: a null removes a key, an
	/// object merges into the object under its key, anything else replaces it.
	/// </summary>
	public static JsonObject Merge(JsonObject? target, JsonObject patch)
	{
		var merged = target?.DeepClone().AsObject() ?? [];
		foreach (var (key, value) in patch)
		{
			if (value is null) merged.Remove(key);
			else if (value is JsonObject inner) merged[key] = Merge(merged[key] as JsonObject, inner);
			else merged[key] = value.DeepClone();
		}
		return merged;
	}

	/// <summary>
	/// The data a reader may see: all of it for one who may examine the map, else only the reserved keys the World map
	/// shows everyone.
	/// </summary>
	public static string? Visible(string? data, bool examine)
	{
		if (examine || Parse(data) is not { } parsed) return examine ? data : null;
		var shown = new JsonObject();
		foreach (var entry in parsed.Where(entry => entry.Key is "render" or "info" && entry.Value is not null))
		{
			shown[entry.Key] = entry.Value?.DeepClone();
		}
		return shown.Count > 0 ? shown.ToJsonString() : null;
	}
}

/// <summary>A place's id, as a mapper keys it: the map's objid and the position.</summary>
public static class WorldPlaceId
{
	public static string Of(DBRef map, int x, int y, int z) =>
		$"#{map.Number}:{map.CreationMilliseconds ?? 0}/{x},{y},{z}";
}
