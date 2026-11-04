using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Components.Builder;

/// <summary>
/// What the application builder is making: a Portal Schema Document held as JSON nodes, so keys the builder has no
/// input for survive a load and a save, plus the attribute maps the <c>app-builder</c> package reads.
/// </summary>
/// <remarks>
/// Stored on the author's object as <c>DATA`APP`&lt;SLUG&gt;</c> (the document), <c>…`SOURCES</c> (field key to the
/// attribute read from the viewed character), <c>…`SAVE</c> (field key to the attribute written on the sender) and
/// <c>…`REQUIRED</c> (the keys a sent form must fill). The builder decides nothing a route relies on: the package
/// checks every value again.
/// </remarks>
public sealed partial class BuilderDraft
{
	/// <summary>The element kinds that are not fields, in palette order.</summary>
	public static readonly IReadOnlyList<string> DisplayKinds =
		["markdown", "keyvalue", "stats", "chips", "table", "timeline", "image", "divider", "button"];

	/// <summary>The field types the builder offers, in palette order. <c>rows</c> and <c>graph</c> are kept when loaded but not offered.</summary>
	public static readonly IReadOnlyList<string> FieldTypes =
		["text", "textarea", "mstring", "number", "slider", "boolean", "select", "radio", "multiselect", "list", "date", "hidden"];

	/// <summary>Field types whose value is one of the element's <c>options</c>.</summary>
	public static readonly IReadOnlySet<string> OptionTypes = new HashSet<string>(["select", "radio", "multiselect"]);

	public JsonObject Schema { get; private set; } = new();

	/// <summary>Field key to the attribute read from the viewed character (<c>…`SOURCES</c>).</summary>
	public Dictionary<string, string> Sources { get; } = new(StringComparer.Ordinal);

	/// <summary>Field key to the attribute written on whoever sends the form (<c>…`SAVE</c>).</summary>
	public Dictionary<string, string> Save { get; } = new(StringComparer.Ordinal);

	public string Slug { get; set; } = string.Empty;

	public bool IsForm
	{
		get => string.Equals((string?)Schema["kind"], "form", StringComparison.OrdinalIgnoreCase);
		set => Schema["kind"] = value ? "form" : "view";
	}

	public string Title
	{
		get => (string?)Schema["title"] ?? string.Empty;
		set => Schema["title"] = value;
	}

	public bool Tabbed
	{
		get => string.Equals((string?)Schema["layout"], "tabs", StringComparison.OrdinalIgnoreCase);
		set
		{
			if (value) Schema["layout"] = "tabs";
			else Schema.Remove("layout");
		}
	}

	public JsonArray Pages => Schema["pages"] as JsonArray ?? (JsonArray)(Schema["pages"] = new JsonArray());

	/// <summary>The attribute holding the document; the maps are its leaves.</summary>
	public string BaseAttribute => $"DATA`APP`{Slug.ToUpperInvariant()}";

	/// <summary>Starts over from <paramref name="schema"/>, a whole document.</summary>
	public void Load(JsonObject schema)
	{
		Schema = schema;
		Schema["schema_version"] ??= 1;
		Schema["kind"] ??= "view";
		if (Pages.Count == 0) AddPage();
	}

	/// <summary>Starts over from stored attribute values; false, with nothing changed, when the document is not a JSON object.</summary>
	public bool LoadStored(string schema, string? sources, string? save)
	{
		if (Parse(schema) is not JsonObject document) return false;
		Load(document);
		ReadMap(sources, Sources);
		ReadMap(save, Save);
		return true;
	}

	private static void ReadMap(string? json, Dictionary<string, string> map)
	{
		map.Clear();
		if (Parse(json) is not JsonObject obj) return;
		foreach (var (key, value) in obj)
		{
			if (value is JsonValue v && v.TryGetValue<string>(out var text)) map[key] = text;
		}
	}

	private static JsonNode? Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;
		try { return JsonNode.Parse(json); }
		catch (JsonException) { return null; }
	}

	public JsonObject AddPage()
	{
		var number = Pages.Count + 1;
		var page = new JsonObject { ["key"] = $"page{number}", ["title"] = $"Page {number}", ["order"] = number, ["sections"] = new JsonArray() };
		Pages.Add(page);
		AddSection(page);
		return page;
	}

	public static JsonArray Sections(JsonObject page) => page["sections"] as JsonArray ?? (JsonArray)(page["sections"] = new JsonArray());

	public static JsonArray Elements(JsonObject section) => section["elements"] as JsonArray ?? (JsonArray)(section["elements"] = new JsonArray());

	public JsonObject AddSection(JsonObject page)
	{
		var sections = Sections(page);
		var section = new JsonObject { ["name"] = "", ["order"] = sections.Count + 1, ["elements"] = new JsonArray() };
		sections.Add(section);
		return section;
	}

	/// <summary>Adds a field of <paramref name="type"/>, or a display element when <paramref name="type"/> is one of <see cref="DisplayKinds"/>.</summary>
	public JsonObject AddElement(JsonObject section, string type)
	{
		var element = DisplayKinds.Contains(type) ? NewDisplay(type) : NewField(type);
		Elements(section).Add(element);
		return element;
	}

	private JsonObject NewField(string type)
	{
		var key = UniqueKey(type == "hidden" ? "hidden" : "field");
		var field = new JsonObject { ["kind"] = "field", ["key"] = key, ["label"] = Label(key), ["type"] = type };
		if (OptionTypes.Contains(type))
		{
			field["options"] = new JsonArray(Option("one", "One"), Option("two", "Two"));
		}
		if (type == "slider") field["validation"] = new JsonObject { ["min"] = 0, ["max"] = 10 };
		return field;
	}

	private JsonObject NewDisplay(string kind) => kind switch
	{
		"markdown" => new JsonObject { ["kind"] = "markdown", ["value"] = "Write here. **Bold** and [links](https://example.com) work." },
		"keyvalue" => new JsonObject { ["kind"] = "keyvalue", ["fields"] = new JsonArray(UniqueKey("value")) },
		"stats" => new JsonObject { ["kind"] = "stats", ["rows_field"] = UniqueKey("stats") },
		"chips" => new JsonObject { ["kind"] = "chips", ["src_field"] = UniqueKey("chips"), ["label"] = "Traits" },
		"table" => new JsonObject
		{
			["kind"] = "table", ["rows_field"] = UniqueKey("rows"), ["empty"] = "Nothing yet.",
			["columns"] = new JsonArray(new JsonObject { ["key"] = "name", ["label"] = "Name" }, new JsonObject { ["key"] = "value", ["label"] = "Value" })
		},
		"timeline" => new JsonObject { ["kind"] = "timeline", ["rows_field"] = UniqueKey("entries"), ["empty"] = "Nothing yet." },
		"image" => new JsonObject { ["kind"] = "image", ["src_field"] = UniqueKey("image"), ["alt"] = "" },
		"button" => new JsonObject { ["kind"] = "button", ["label"] = "Send", ["action"] = "submit" },
		_ => new JsonObject { ["kind"] = kind }
	};

	private static JsonObject Option(string value, string label) => new() { ["value"] = value, ["label"] = label };

	private static string Label(string key) => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..].Replace('_', ' ');

	/// <summary><paramref name="stem"/>, or <paramref name="stem"/> with the lowest number no element uses yet.</summary>
	public string UniqueKey(string stem)
	{
		var used = DataKeys().ToHashSet(StringComparer.Ordinal);
		if (!used.Contains(stem)) return stem;
		for (var n = 2; ; n++)
		{
			if (!used.Contains($"{stem}{n}")) return $"{stem}{n}";
		}
	}

	/// <summary>Every element of every section of every page, with where it sits.</summary>
	public IEnumerable<(JsonObject Page, JsonObject Section, JsonObject Element)> AllElements() =>
		from page in Pages.OfType<JsonObject>()
		from section in Sections(page).OfType<JsonObject>()
		from element in Elements(section).OfType<JsonObject>()
		select (page, section, element);

	/// <summary>Whether <paramref name="element"/> is an input (kind <c>field</c>).</summary>
	public static bool IsField(JsonObject element) => (string?)element["kind"] is null or "field";

	/// <summary>
	/// The data keys the document reads, each once, in document order: a field's <c>key</c>, a display element's
	/// <c>src_field</c> or <c>rows_field</c>, and a keyvalue's <c>fields</c>.
	/// </summary>
	public IEnumerable<string> DataKeys()
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (_, _, element) in AllElements())
		{
			foreach (var key in ElementKeys(element))
			{
				if (!string.IsNullOrWhiteSpace(key) && seen.Add(key)) yield return key;
			}
		}
	}

	private static IEnumerable<string?> ElementKeys(JsonObject element)
	{
		if (IsField(element)) yield return (string?)element["key"];
		yield return (string?)element["src_field"];
		yield return (string?)element["rows_field"];
		if (element["fields"] is JsonArray fields)
		{
			foreach (var field in fields) yield return field is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
		}
	}

	/// <summary>The input fields a form sends, by key.</summary>
	public IEnumerable<JsonObject> InputFields() =>
		AllElements().Select(e => e.Element).Where(e => IsField(e) && !string.IsNullOrWhiteSpace((string?)e["key"]));

	/// <summary>The keys of fields marked required, space-separated, for <c>…`REQUIRED</c>.</summary>
	public string RequiredKeys() => string.Join(' ', InputFields()
		.Where(f => f["validation"] is JsonObject v && v["required"] is JsonValue r && r.TryGetValue<bool>(out var required) && required)
		.Select(f => (string)f["key"]!));

	[GeneratedRegex("^[a-z0-9_-]+$")]
	private static partial Regex SlugPattern();

	[GeneratedRegex("^[a-z0-9_]+$")]
	private static partial Regex KeyPattern();

	[GeneratedRegex("^[A-Za-z0-9_`.-]+$")]
	private static partial Regex AttributePattern();

	public static bool IsSlug(string? slug) => slug is { Length: > 0 and <= 40 } && SlugPattern().IsMatch(slug);

	public static bool IsKey(string? key) => key is { Length: > 0 and <= 40 } && KeyPattern().IsMatch(key);

	public static bool IsAttribute(string? name) => name is { Length: > 0 and <= 200 } && AttributePattern().IsMatch(name) && !name.Contains("``");

	/// <summary>A slug made from <paramref name="title"/>: lower case, words joined with <c>-</c>.</summary>
	public static string SlugFrom(string title)
	{
		var slug = Regex.Replace(title.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
		return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
	}

	/// <summary>What stops the draft being saved, one line each; none when it can be.</summary>
	public IReadOnlyList<string> Problems()
	{
		var problems = new List<string>();
		if (!IsSlug(Slug)) problems.Add("The address name is lower-case letters, digits, _ and -.");
		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var field in InputFields())
		{
			var key = (string)field["key"]!;
			if (!IsKey(key)) problems.Add($"The key \"{key}\" is lower-case letters, digits and _.");
			if (!keys.Add(key)) problems.Add($"Two fields use the key \"{key}\".");
			if (OptionTypes.Contains((string?)field["type"] ?? "") && (field["options"] as JsonArray)?.Count is null or 0)
				problems.Add($"\"{(string?)field["label"] ?? key}\" has no choices.");
		}
		foreach (var key in DataKeys().Where(k => !IsKey(k)))
		{
			if (!keys.Contains(key)) problems.Add($"The key \"{key}\" is lower-case letters, digits and _.");
		}
		problems.AddRange(Sources.Concat(Save).Where(p => p.Value.Length > 0 && !IsAttribute(p.Value))
			.Select(p => $"\"{p.Value}\" is not an attribute name."));
		if (IsForm && !InputFields().Any()) problems.Add("A form needs at least one field.");
		return problems;
	}

	/// <summary>The document as stored, with the submit action pointed at the package's route.</summary>
	public JsonObject Stored()
	{
		var copy = (JsonObject)Schema.DeepClone();
		copy["schema_version"] = 1;
		if (IsForm)
		{
			if (copy["actions"] is not JsonObject actions)
			{
				actions = new JsonObject();
				copy["actions"] = actions;
			}
			actions["submit"] = new JsonObject
			{
				["transport"] = "http", ["method"] = "POST", ["route"] = ActRoute(Slug), ["payload"] = "fields",
				["on_success"] = new JsonObject { ["toast"] = "Saved." },
				["on_error"] = new JsonObject { ["bind_field_errors"] = true }
			};
		}
		return copy;
	}

	public static string SchemaRoute(string slug) => $"http/built/schema?app={slug}";
	public static string DataRoute(string slug, bool widget) => $"http/built/data?app={slug}" + (widget ? "&objid={objid}" : "");
	public static string ActRoute(string slug) => $"http/built/act?app={slug}";

	/// <summary>A JSON object of the non-empty entries of <paramref name="map"/> whose key the document still reads.</summary>
	public string MapJson(IReadOnlyDictionary<string, string> map, IEnumerable<string> keys)
	{
		var live = keys.ToHashSet(StringComparer.Ordinal);
		var obj = new JsonObject();
		foreach (var (key, attribute) in map.Where(p => live.Contains(p.Key) && !string.IsNullOrWhiteSpace(p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal))
		{
			obj[key] = attribute.Trim();
		}
		return obj.Count == 0 ? string.Empty : obj.ToJsonString();
	}

	/// <summary>The document the preview draws: no actions, so nothing in it can post.</summary>
	public PortalSchemaDocument? Preview()
	{
		var copy = (JsonObject)Schema.DeepClone();
		copy.Remove("actions");
		try { return copy.Deserialize<PortalSchemaDocument>(SchemaJson.Options); }
		catch (JsonException) { return null; }
	}

	/// <summary>Made-up values for every key the document reads, so the preview shows something in each place.</summary>
	public SchemaData SampleData()
	{
		var fields = new Dictionary<string, SchemaFieldValue>(StringComparer.Ordinal);
		foreach (var (_, _, element) in AllElements())
		{
			foreach (var key in ElementKeys(element).OfType<string>().Where(k => k.Length > 0 && !fields.ContainsKey(k)))
			{
				fields[key] = new SchemaFieldValue(Sample(element, key));
			}
		}
		return new SchemaData(fields);
	}

	private static JsonElement Sample(JsonObject element, string key)
	{
		JsonNode node = ((string?)element["kind"] ?? "field") switch
		{
			"stats" => new JsonArray(
				new JsonObject { ["label"] = "Health", ["value"] = "12 / 20", ["current"] = 12, ["max"] = 20, ["color"] = "success" },
				new JsonObject { ["label"] = "Luck", ["value"] = "2", ["current"] = 2, ["max"] = 3, ["pips"] = true, ["color"] = "warning" }),
			"chips" => new JsonArray("Brave", "Curious"),
			"table" => new JsonArray(SampleRow(element, 1), SampleRow(element, 2)),
			"timeline" => new JsonArray(new JsonObject { ["author"] = "Ada", ["time"] = 1760000000, ["body"] = "A first entry." }),
			"image" => "https://picsum.photos/seed/sharpmush/320/180",
			_ when (string?)element["kind"] == "keyvalue" => $"Sample {key.Replace('_', ' ')}",
			_ => (string?)element["type"] switch
			{
				"number" or "slider" => 3,
				"boolean" => true,
				"list" => new JsonArray("First", "Second"),
				"multiselect" => new JsonArray(),
				"date" => "2026-01-01",
				"select" or "radio" => (element["options"] as JsonArray)?.FirstOrDefault()?["value"]?.DeepClone() ?? "",
				_ => $"Sample {key.Replace('_', ' ')}"
			}
		};
		return JsonSerializer.SerializeToElement(node);
	}

	private static JsonObject SampleRow(JsonObject table, int n)
	{
		var row = new JsonObject();
		foreach (var column in (table["columns"] as JsonArray ?? []).OfType<JsonObject>())
		{
			if ((string?)column["key"] is { Length: > 0 } key) row[key] = $"{column["label"] ?? key} {n}";
		}
		return row;
	}
}
