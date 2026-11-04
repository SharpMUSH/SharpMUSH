using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Library.Models.Portal.Applications;

/// <summary>
/// What a Portal Schema Document may say, and the checker that holds a document to it. The portal draws from
/// this vocabulary, <c>schemarender()</c> draws it in the terminal and <c>schemacheck()</c> and the installer
/// report what falls outside it, so the hosts cannot come to disagree about what a document means.
/// </summary>
/// <remarks>
/// Keys the checker does not know are never a problem: a document written for a newer portal still passes, and
/// one the builder loaded keeps what it had no input for.
/// </remarks>
public static class PortalSchema
{
	/// <summary>The element kinds besides <c>field</c>: what a view draws from its data rather than asks for.</summary>
	public static FrozenSet<string> DisplayKinds { get; } =
		new[] { "divider", "markdown", "image", "table", "keyvalue", "timeline", "graph", "button", "stats", "chips" }
			.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>The input types a <c>field</c> may have; a field that names none is <c>text</c>.</summary>
	public static FrozenSet<string> FieldTypes { get; } =
		new[] { "text", "textarea", "mstring", "number", "slider", "boolean", "select", "radio", "multiselect", "list", "rows", "date", "graph", "hidden" }
			.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>The document kinds: <c>form</c> asks, <c>view</c> shows.</summary>
	public static FrozenSet<string> DocumentKinds { get; } = new[] { "form", "view" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>An element's kind, lower case: <c>field</c> when it names none.</summary>
	public static string KindOf(JsonObject element) =>
		(Text(element, "kind") is { Length: > 0 } kind ? kind : "field").ToLowerInvariant();

	/// <summary>
	/// Every problem with <paramref name="document"/>, one <c>&lt;path&gt;: &lt;problem&gt;</c> each, in document
	/// order; none when it is fine. Given the data a route answers (<paramref name="data"/>, the
	/// <c>{"fields":{...}}</c> object), it also names what an element reads that the data does not hold.
	/// </summary>
	public static IReadOnlyList<string> Check(JsonNode? document, JsonNode? data = null)
	{
		var problems = new List<string>();
		if (document is not JsonObject root)
		{
			problems.Add("document: not a JSON object");
			return problems;
		}

		var fields = data is JsonObject { } payload && payload["fields"] is JsonObject held ? held : null;
		if (Text(root, "kind") is { Length: > 0 } kind && !DocumentKinds.Contains(kind))
			problems.Add($"kind: unknown document kind \"{kind}\"");

		var actions = root["actions"] as JsonObject;
		if (root["actions"] is { } declared && actions is null) problems.Add("actions: not an object");
		foreach (var (name, action) in actions ?? [])
		{
			if (action is not JsonObject body) problems.Add($"actions.{name}: not an object");
			else if (Text(body, "route") is { Length: > 0 } route && !IsRelative(route))
				problems.Add($"actions.{name}: route \"{route}\" is not relative");
		}

		if (root["pages"] is not JsonArray pages)
		{
			if (root["pages"] is not null) problems.Add("pages: not a list");
			return problems;
		}

		var pageKeys = pages.OfType<JsonObject>().Select(page => Text(page, "key")).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
		var fieldKeys = new HashSet<string>(StringComparer.Ordinal);
		for (var p = 0; p < pages.Count; p++)
		{
			var pagePath = $"pages[{p}]";
			if (pages[p] is not JsonObject page)
			{
				problems.Add($"{pagePath}: not an object");
				continue;
			}
			foreach (var link in (string[])["next", "prev"])
			{
				if (Text(page, link) is { Length: > 0 } target && !pageKeys.Contains(target))
					problems.Add($"{pagePath}: {link} names no page \"{target}\"");
			}
			if (page["sections"] is not JsonArray sections)
			{
				if (page["sections"] is not null) problems.Add($"{pagePath}.sections: not a list");
				continue;
			}
			for (var s = 0; s < sections.Count; s++)
			{
				var sectionPath = $"{pagePath}.sections[{s}]";
				if (sections[s] is not JsonObject section)
				{
					problems.Add($"{sectionPath}: not an object");
					continue;
				}
				var columns = Number(section, "columns") ?? 1;
				if (columns < 1) problems.Add($"{sectionPath}: columns must be 1 or more");
				if (section["elements"] is not JsonArray elements)
				{
					if (section["elements"] is not null) problems.Add($"{sectionPath}.elements: not a list");
					continue;
				}
				for (var e = 0; e < elements.Count; e++)
				{
					var elementPath = $"{sectionPath}.elements[{e}]";
					if (elements[e] is not JsonObject element)
					{
						problems.Add($"{elementPath}: not an object");
						continue;
					}
					CheckElement(element, elementPath, columns, actions, fields, fieldKeys, problems);
				}
			}
		}
		return problems;
	}

	private static void CheckElement(JsonObject element, string path, int columns, JsonObject? actions, JsonObject? fields,
		HashSet<string> fieldKeys, List<string> problems)
	{
		var kind = KindOf(element);
		if (kind != "field" && !DisplayKinds.Contains(kind))
		{
			problems.Add($"{path}: unknown kind \"{kind}\"");
			return;
		}

		if (Number(element, "span") is { } span && columns > 1 && span > columns)
			problems.Add($"{path}: span {span} is wider than the section's {columns} columns");
		CheckAction(element, "triggers_action", path, actions, problems);
		if (element["reorder"] is JsonObject reorder) CheckAction(reorder, "action", $"{path}.reorder", actions, problems);

		switch (kind)
		{
			case "field":
				CheckField(element, path, problems);
				if (Text(element, "key") is { Length: > 0 } key && !fieldKeys.Add(key))
					problems.Add($"{path}: key \"{key}\" is used twice");
				break;
			case "button":
				if (Text(element, "action") is not { Length: > 0 }) problems.Add($"{path}: a button needs an action");
				else CheckAction(element, "action", path, actions, problems);
				break;
			case "table":
				Reads(element, "rows_field", path, fields, problems);
				if (element["columns"] is not JsonArray { Count: > 0 }) problems.Add($"{path}: a table needs columns");
				break;
			case "stats" or "timeline":
				Reads(element, "rows_field", path, fields, problems);
				break;
			case "chips" or "graph":
				Reads(element, "src_field", path, fields, problems);
				break;
			case "image":
				if (Text(element, "src_field") is { Length: > 0 }) Reads(element, "src_field", path, fields, problems);
				else if (Text(element, "value") is not { Length: > 0 }) problems.Add($"{path}: an image needs src_field or value");
				break;
			case "keyvalue":
				if (element["fields"] is not JsonArray { Count: > 0 } keys)
				{
					problems.Add($"{path}: keyvalue needs fields");
					break;
				}
				foreach (var name in keys.Select(k => k is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>())
				{
					if (fields is not null && !fields.ContainsKey(name)) problems.Add($"{path}: \"{name}\" is not in the data");
				}
				break;
		}
	}

	private static void CheckField(JsonObject element, string path, List<string> problems)
	{
		if (Text(element, "key") is not { Length: > 0 }) problems.Add($"{path}: a field needs a key");
		var type = Text(element, "type") is { Length: > 0 } named ? named : "text";
		if (!FieldTypes.Contains(type))
		{
			problems.Add($"{path}: unknown field type \"{type}\"");
			return;
		}
		if (type.ToLowerInvariant() is "select" or "multiselect" or "radio" && element["options"] is not JsonArray { Count: > 0 }
			&& Text(element, "options_from") is not { Length: > 0 })
			problems.Add($"{path}: a {type.ToLowerInvariant()} needs options");
		if (element["item_fields"] is JsonArray items)
		{
			for (var i = 0; i < items.Count; i++)
			{
				if (items[i] is JsonObject item) CheckField(item, $"{path}.item_fields[{i}]", problems);
			}
		}
	}

	private static void CheckAction(JsonObject element, string property, string path, JsonObject? actions, List<string> problems)
	{
		if (Text(element, property) is { Length: > 0 } action && actions?.ContainsKey(action) != true)
			problems.Add($"{path}: {property} \"{action}\" is not among the actions");
	}

	private static void Reads(JsonObject element, string property, string path, JsonObject? fields, List<string> problems)
	{
		if (Text(element, property) is not { Length: > 0 } key) problems.Add($"{path}: {KindOf(element)} needs {property}");
		else if (fields is not null && !fields.ContainsKey(key)) problems.Add($"{path}: {property} \"{key}\" is not in the data");
	}

	/// <summary>A route the portal may call: a path on this server, not an address elsewhere.</summary>
	private static bool IsRelative(string route) =>
		!route.StartsWith('/') && !route.StartsWith('\\') && !route.Contains("://", StringComparison.Ordinal) && !route.StartsWith("//", StringComparison.Ordinal);

	/// <summary>A property's text, empty when it is missing or not a string.</summary>
	public static string Text(JsonObject node, string property) =>
		node[property] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : string.Empty;

	private static int? Number(JsonObject node, string property) =>
		node[property] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number) ? number : null;
}
