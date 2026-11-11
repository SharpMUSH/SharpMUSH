using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// A widget placement's config as the layout editor's settings form edits it: a JSON object read
/// and written one documented key at a time.
/// </summary>
/// <remarks>
/// <para>
/// Keys the form does not know are left exactly as they were, so a config written by hand (or by a
/// later version of the widget) survives being opened and saved in the form.
/// </para>
/// <para>
/// Widgets read their config case-insensitively, so a key is found whatever its casing and is
/// written back under the schema's camelCase name. Clearing a field removes its key, which hands it
/// back to the widget's default rather than saving an empty value over it.
/// </para>
/// </remarks>
public sealed class WidgetConfigDocument
{
	private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

	private readonly JsonObject _root;

	private WidgetConfigDocument(JsonObject root) => _root = root;

	/// <summary>An empty config.</summary>
	public static WidgetConfigDocument Empty() => new(new JsonObject());

	/// <summary>
	/// Reads a stored config. Blank is an empty config; anything that is not a JSON object is refused
	/// with the parser's message, or with <paramref name="notAnObject"/> for valid JSON of another kind.
	/// </summary>
	public static Result<WidgetConfigDocument> Parse(string? json, string notAnObject)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return Empty();
		}

		try
		{
			return JsonNode.Parse(json) is JsonObject root
				? new WidgetConfigDocument(root)
				: new Error<string>(notAnObject);
		}
		catch (JsonException ex)
		{
			return new Error<string>(ex.Message);
		}
	}

	/// <summary>The config as indented JSON, or null when it holds no keys (no config at all).</summary>
	public string? ToJson() => _root.Count == 0 ? null : _root.ToJsonString(Indented);

	/// <summary>The keys present that no documented field names, in their stored order.</summary>
	public IReadOnlyList<string> UnknownKeys(IReadOnlyList<WidgetConfigField> fields) =>
		_root.Select(p => p.Key)
			.Where(key => !fields.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)))
			.ToList();

	public string? GetString(string key) => Find(key) switch
	{
		JsonValue value when value.TryGetValue<string>(out var text) => text,
		JsonValue value => value.ToJsonString(),
		_ => null
	};

	/// <summary>Sets a text key; empty or null removes it.</summary>
	public void SetString(string key, string? value)
		=> Put(key, string.IsNullOrEmpty(value) ? null : JsonValue.Create(value));

	public bool? GetBool(string key) => Find(key) switch
	{
		JsonValue value when value.TryGetValue<bool>(out var flag) => flag,
		_ => null
	};

	public void SetBool(string key, bool value) => Put(key, JsonValue.Create(value));

	public double? GetNumber(string key) => Find(key) switch
	{
		// A value written here holds a long or a double, a parsed one a JsonElement: read the number's text.
		JsonValue value when value.GetValueKind() == JsonValueKind.Number
			&& double.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) => number,
		JsonValue value when value.TryGetValue<string>(out var text)
			&& double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
		_ => null
	};

	/// <summary>Sets a numeric key, as an integer when <paramref name="integer"/>; null removes it.</summary>
	public void SetNumber(string key, double? value, bool integer)
		=> Put(key, value switch
		{
			null => null,
			{ } n when integer => JsonValue.Create((long)Math.Round(n)),
			{ } n => JsonValue.Create(n)
		});

	/// <summary>A list of plain values, one string each; empty when the key is unset.</summary>
	public IReadOnlyList<string> GetLines(string key) => Find(key) switch
	{
		JsonArray array => array.Select(item => item switch
		{
			JsonValue value when value.TryGetValue<string>(out var text) => text,
			null => string.Empty,
			_ => item.ToJsonString()
		}).ToList(),
		_ => []
	};

	/// <summary>Sets a list of strings from its lines, skipping blank ones; no lines removes the key.</summary>
	public void SetLines(string key, IEnumerable<string> lines)
	{
		var items = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
		Put(key, items.Count == 0 ? null : new JsonArray(items.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()));
	}

	/// <summary>
	/// The objects of a list key, each editable in place: a change to one is a change to this config.
	/// An entry that is not an object is skipped here and kept in the stored list.
	/// </summary>
	public IReadOnlyList<WidgetConfigDocument> GetItems(string key) => Find(key) switch
	{
		JsonArray array => array.OfType<JsonObject>().Select(o => new WidgetConfigDocument(o)).ToList(),
		_ => []
	};

	/// <summary>Appends an empty object to a list key, creating the list when it is unset.</summary>
	public WidgetConfigDocument AddItem(string key)
	{
		if (Find(key) is not JsonArray array)
		{
			array = new JsonArray();
			Put(key, array);
		}

		var item = new JsonObject();
		array.Add(item);
		return new WidgetConfigDocument(item);
	}

	/// <summary>Removes one object from a list key; the key goes when the list is left empty.</summary>
	public void RemoveItem(string key, WidgetConfigDocument item)
	{
		if (Find(key) is not JsonArray array)
		{
			return;
		}

		array.Remove(item._root);
		if (array.Count == 0)
		{
			Put(key, null);
		}
	}

	/// <summary>Moves one object of a list key by <paramref name="offset"/> places, within the list.</summary>
	public void MoveItem(string key, WidgetConfigDocument item, int offset)
	{
		if (Find(key) is not JsonArray array)
		{
			return;
		}

		var from = array.IndexOf(item._root);
		var to = from + offset;
		if (from < 0 || to < 0 || to >= array.Count)
		{
			return;
		}

		array.RemoveAt(from);
		array.Insert(to, item._root);
	}

	private JsonNode? Find(string key) => StoredKey(key) is { } stored ? _root[stored] : null;

	private string? StoredKey(string key) =>
		_root.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

	/// <summary>Writes a key under its schema name, replacing it whatever its stored casing; null removes it.</summary>
	private void Put(string key, JsonNode? value)
	{
		if (StoredKey(key) is { } stored && !string.Equals(stored, key, StringComparison.Ordinal))
		{
			// Keep the key in place: removing and re-adding would move it to the end.
			var index = _root.IndexOf(stored);
			_root.RemoveAt(index);
			if (value is not null)
			{
				_root.Insert(index, key, value);
			}

			return;
		}

		if (value is null)
		{
			_root.Remove(key);
			return;
		}

		_root[key] = value;
	}
}
