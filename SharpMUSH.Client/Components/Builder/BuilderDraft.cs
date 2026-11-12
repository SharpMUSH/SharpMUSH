using SharpMUSH.Library.Models.Portal.Applications;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Components.Builder;

/// <summary>
/// What the application builder is making: a Portal Schema Document held as JSON nodes, so keys the builder has no
/// input for survive a load and a save, plus the attribute maps the server's built-application routes read (<c>api/built</c>).
/// </summary>
/// <remarks>
/// Stored on the author's object as <c>DATA`APP`&lt;SLUG&gt;</c> (the document), <c>…`SOURCES</c> (field key to the
/// attribute read from the viewed character), <c>…`SAVE</c> (field key to the attribute written on the sender) and
/// <c>…`REQUIRED</c> (the keys a sent form must fill). The builder decides nothing a route relies on: the server
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

	/// <summary>
	/// For an application the builder did not make, the attribute its document is kept in beside its route
	/// (<c>DATA`PROFILE`SCHEMA</c>); null for one the builder made. Such a document is saved as it is: its own routes
	/// supply its values and take its actions, so the builder keeps no maps for it and leaves its actions alone.
	/// </summary>
	public string? KeptAt { get; set; }

	/// <summary>The attribute holding the document; for an application the builder made, the maps are its leaves.</summary>
	public string BaseAttribute => KeptAt ?? $"DATA`APP`{Slug.ToUpperInvariant()}";

	/// <summary>Starts over from <paramref name="schema"/>, a whole document.</summary>
	public void Load(JsonObject schema)
	{
		Schema = schema;
		KeptAt = null;
		Schema["schema_version"] ??= 1;
		Schema["kind"] ??= "view";
		if (Pages.Count == 0) AddPage();
	}

	/// <summary>Starts over from stored attribute values; false, with nothing changed, when the document is not a JSON object.</summary>
	public bool LoadStored(string schema, string? sources, string? save, string? keptAt = null)
	{
		if (Parse(schema) is not JsonObject document) return false;
		Load(document);
		KeptAt = keptAt;
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
			foreach (var key in ElementKeys(element).OfType<string>().Where(key => !string.IsNullOrWhiteSpace(key) && seen.Add(key)))
				yield return key;
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
	public IReadOnlyList<string> Problems() => [.. Issues().Select(issue => issue.Text)];

	/// <summary>
	/// What stops the draft being saved, each with the step it is fixed on and the page, section or element it is
	/// about, when it is about one.
	/// </summary>
	public IReadOnlyList<BuilderIssue> Issues()
	{
		var issues = new List<BuilderIssue>();
		// A stored document keeps its registry slug and its keys, which its own route answers to: the builder's own
		// naming rules do not apply, only duplicate keys and what schemacheck() refuses.
		var own = KeptAt is null;
		if (own && !IsSlug(Slug)) issues.Add(new("The address name is lower-case letters, digits, _ and -.", BuilderIssue.KindStep));
		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var field in InputFields())
		{
			var key = (string)field["key"]!;
			if (own && !IsKey(key)) issues.Add(new($"The key \"{key}\" is lower-case letters, digits and _.", BuilderIssue.LayoutStep, field));
			if (!keys.Add(key)) issues.Add(new($"Two fields use the key \"{key}\".", BuilderIssue.LayoutStep, field));
			if (OptionTypes.Contains((string?)field["type"] ?? "") && (field["options"] as JsonArray)?.Count is null or 0)
				issues.Add(new($"\"{(string?)field["label"] ?? key}\" has no choices.", BuilderIssue.LayoutStep, field));
		}
		issues.AddRange(from item in AllElements()
										where own && !IsField(item.Element)
										from key in ElementKeys(item.Element).OfType<string>()
										where !IsKey(key) && !keys.Contains(key)
										select new BuilderIssue($"The key \"{key}\" is lower-case letters, digits and _.", BuilderIssue.LayoutStep, item.Element));
		var live = DataKeys().ToHashSet(StringComparer.Ordinal);
		issues.AddRange(Sources.Concat(Save).Where(p => live.Contains(p.Key) && p.Value.Length > 0 && !IsAttribute(p.Value))
			.Select(p => new BuilderIssue($"\"{p.Value}\" is not an attribute name.", BuilderIssue.ValuesStep)));
		if (IsForm && !InputFields().Any()) issues.Add(new("A form needs at least one field.", BuilderIssue.LayoutStep));
		// What schemacheck() would say, where the checks above have not said it already in plainer words.
		issues.AddRange(PortalSchema.Check(Stored())
			.Where(problem => !problem.Contains(" is used twice", StringComparison.Ordinal) && !problem.Contains(" needs options", StringComparison.Ordinal))
			.Select(problem => new BuilderIssue(problem, BuilderIssue.LayoutStep, NodeAt(problem))));
		return issues.DistinctBy(issue => (issue.Text, issue.Node)).ToList();
	}

	/// <summary>The page, section or element a <c>schemacheck()</c> line names by its path, such as <c>pages[0].sections[1].elements[2]</c>.</summary>
	private JsonObject? NodeAt(string problem)
	{
		if (CheckPath().Match(problem) is not { Success: true } match) return null;
		var page = Pages.ElementAtOrDefault(int.Parse(match.Groups[1].Value)) as JsonObject;
		if (page is null || !match.Groups[2].Success) return page;
		var section = Sections(page).ElementAtOrDefault(int.Parse(match.Groups[2].Value)) as JsonObject;
		if (section is null || !match.Groups[3].Success) return section ?? page;
		return Elements(section).ElementAtOrDefault(int.Parse(match.Groups[3].Value)) as JsonObject ?? section;
	}

	[GeneratedRegex(@"^pages\[(\d+)\](?:\.sections\[(\d+)\](?:\.elements\[(\d+)\])?)?")]
	private static partial Regex CheckPath();

	/// <summary>A key made from <paramref name="label"/>: lower case, other characters as <c>_</c>.</summary>
	public static string KeyFrom(string? label)
	{
		var key = Regex.Replace((label ?? string.Empty).Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
		return key.Length > 40 ? key[..40].TrimEnd('_') : key;
	}

	/// <summary>Whether <paramref name="field"/>'s key is the one its label makes, so a new label should change it too.</summary>
	public static bool KeyFollowsLabel(JsonObject field) =>
		(string?)field["key"] is { } key && (key == KeyFrom((string?)field["label"]) || key.Length == 0);

	/// <summary>
	/// Sets <paramref name="field"/>'s label, and its key with it while the key still follows the label and the new key
	/// is free.
	/// </summary>
	public void SetLabel(JsonObject field, string label)
	{
		var follows = IsField(field) && KeyFollowsLabel(field);
		field["label"] = label;
		if (!follows || KeyFrom(label) is not { Length: > 0 } key) return;
		field["key"] = key == (string?)field["key"] || !DataKeys().Contains(key) ? key : UniqueKey(key);
	}

	/// <summary>A copy of <paramref name="node"/> placed after it in <paramref name="list"/>, its keys made unique.</summary>
	public JsonObject Duplicate(JsonArray list, JsonObject node)
	{
		var copy = (JsonObject)node.DeepClone();
		var inside = copy["kind"] is null && copy["sections"] is JsonArray ? AllIn(copy) : copy["elements"] is JsonArray elements ? elements.OfType<JsonObject>() : [copy];
		foreach (var element in inside.Where(element => IsField(element) && (string?)element["key"] is { Length: > 0 }).ToList())
			element["key"] = UniqueKey((string)element["key"]!);
		if ((string?)copy["key"] is { Length: > 0 } pageKey && copy["sections"] is JsonArray) copy["key"] = $"{pageKey}_copy";
		list.Insert(list.IndexOf(node) + 1, copy);
		return copy;
	}

	private static IEnumerable<JsonObject> AllIn(JsonObject page) =>
		Sections(page).OfType<JsonObject>().SelectMany(section => Elements(section).OfType<JsonObject>());

	/// <summary>The draft as one JSON text, for undo and for keeping it in the browser.</summary>
	public string Snapshot() => new JsonObject
	{
		["schema"] = Schema.DeepClone(),
		["slug"] = Slug,
		["sources"] = new JsonObject(Sources.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
		["save"] = new JsonObject(Save.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
		["kept_at"] = KeptAt,
	}.ToJsonString();

	/// <summary>Goes back to a <see cref="Snapshot"/>; false, with nothing changed, when it is not one.</summary>
	public bool Restore(string snapshot)
	{
		if (Parse(snapshot) is not JsonObject { } saved || saved["schema"] is not JsonObject schema) return false;
		Load((JsonObject)schema.DeepClone());
		Slug = (string?)saved["slug"] ?? string.Empty;
		KeptAt = (string?)saved["kept_at"];
		ReadMap(saved["sources"]?.ToJsonString(), Sources);
		ReadMap(saved["save"]?.ToJsonString(), Save);
		return true;
	}

	/// <summary>Where <paramref name="node"/> sits, as page, section and element indexes (-1 for none), to find it again after an undo.</summary>
	public (int Page, int Section, int Element) PathOf(JsonObject? node)
	{
		for (var p = 0; p < Pages.Count; p++)
		{
			if (Pages[p] is not JsonObject page) continue;
			if (ReferenceEquals(page, node)) return (p, -1, -1);
			var sections = Sections(page);
			for (var s = 0; s < sections.Count; s++)
			{
				if (sections[s] is not JsonObject section) continue;
				if (ReferenceEquals(section, node)) return (p, s, -1);
				var index = Elements(section).IndexOf(node);
				if (index >= 0) return (p, s, index);
			}
		}
		return (-1, -1, -1);
	}

	/// <summary>The page, section and element at a <see cref="PathOf"/>, as far as each still exists.</summary>
	public (JsonObject? Page, JsonObject? Section, JsonObject? Element) At((int Page, int Section, int Element) path)
	{
		var page = path.Page >= 0 ? Pages.ElementAtOrDefault(path.Page) as JsonObject : null;
		var section = page is not null && path.Section >= 0 ? Sections(page).ElementAtOrDefault(path.Section) as JsonObject : null;
		var element = section is not null && path.Element >= 0 ? Elements(section).ElementAtOrDefault(path.Element) as JsonObject : null;
		return (page, section, element);
	}

	/// <summary>The names a click's command can fill from a value of <paramref name="element"/> (<see cref="SchemaClick"/>).</summary>
	public static IReadOnlyList<string> ClickNames(JsonObject element) => ((string?)element["kind"]) switch
	{
		"table" => [.. new[] { "key" }.Concat((element["columns"] as JsonArray ?? []).OfType<JsonObject>()
			.Select(column => (string?)column["key"]).OfType<string>().Where(key => key.Length > 0)).Distinct()],
		"stats" => ["key", "label", "value"],
		"chips" => ["text"],
		"keyvalue" => ["key", "value"],
		"button" => [.. new[] { "key", "label" }.Concat((element["values"] as JsonObject ?? []).Select(pair => pair.Key))],
		_ => [],
	};

	/// <summary>The document as stored, with the submit action pointed at the server's route.</summary>
	public JsonObject Stored()
	{
		var copy = (JsonObject)Schema.DeepClone();
		copy["schema_version"] = 1;
		if (IsForm && KeptAt is null)
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

	public static string SchemaRoute(string slug) => $"api/built/{slug}/schema";
	public static string DataRoute(string slug, bool widget) => $"api/built/{slug}/data" + (widget ? "?objid={objid}" : "");
	public static string ActRoute(string slug) => $"api/built/{slug}/act";

	/// <summary>Where a registered application shows: as a widget, and on which layout scope (<c>home</c> for any).</summary>
	public static (bool Widget, string Scope) Placement(PortalApplication registered) =>
		registered.KindEnum == ApplicationKind.Widget ? (true, registered.Scope ?? "home") : (false, "profile");

	/// <summary>
	/// The registration this draft sends, held by <paramref name="holder"/>. What the builder has no input for (icon, role,
	/// navigation, zones, order, permission) comes from <paramref name="existing"/>, so a re-register changes only the draft.
	/// </summary>
	public PortalApplication Registration(string holder, bool widget, string scope, PortalApplication? existing) => new(
		Slug,
		string.IsNullOrWhiteSpace(Title) ? Slug : Title.Trim(),
		existing?.Icon ?? (IsForm ? "edit_note" : "article"),
		widget ? ApplicationKind.Widget.ToString() : ApplicationKind.Page.ToString(),
		SchemaRoute(Slug),
		DataRoute(Slug, widget),
		IsForm ? ActRoute(Slug) : null,
		existing?.MinimumRole ?? "Player",
		widget ? null : existing?.NavPlacement ?? "Play",
		widget ? existing is { Zones.Length: > 0 } ? existing.Zones : ["MainContent"] : [],
		existing?.Order ?? 100,
		RenderKind: ApplicationRenderKind.Built,
		Scope: widget && scope != "home" ? scope : null,
		Permission: existing?.Permission,
		Config: JsonSerializer.SerializeToElement(new { holder }));

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
		foreach (var column in (table["columns"] as JsonArray ?? []).OfType<JsonObject>().Where(column => (string?)column["key"] is { Length: > 0 }))
		{
			var key = (string)column["key"]!;
			row[key] = $"{column["label"] ?? key} {n}";
		}
		return row;
	}
}

/// <summary>One thing that stops a draft being saved: what it is, the builder step that fixes it, and the node it is about.</summary>
public sealed record BuilderIssue(string Text, int Step, JsonObject? Node = null)
{
	public const int KindStep = 1;
	public const int LayoutStep = 2;
	public const int ValuesStep = 3;
}
