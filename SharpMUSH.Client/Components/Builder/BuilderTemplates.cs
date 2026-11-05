using System.Text.Json.Nodes;

namespace SharpMUSH.Client.Components.Builder;

/// <summary>The starting points the builder offers. Each is an ordinary document the author then changes.</summary>
public static class BuilderTemplates
{
	/// <summary>One template: its key, the resource keys of its name and description, whether it is a form, and its document.</summary>
	public sealed record Template(string Key, string NameKey, string DescriptionKey, bool Form, Func<JsonObject> Document,
		IReadOnlyDictionary<string, string>? Sources = null, IReadOnlyDictionary<string, string>? Save = null);

	public static IReadOnlyList<Template> All { get; } =
	[
		new("blank", "LayBldTplBlank", "LayBldTplBlankHelp", false, () => Doc("view", Section("", []))),
		new("sheet", "LayBldTplSheet", "LayBldTplSheetHelp", false, () => Doc("view",
				Section("", [
					new JsonObject { ["kind"] = "keyvalue", ["fields"] = new JsonArray("concept", "home") },
					new JsonObject { ["kind"] = "chips", ["src_field"] = "traits", ["label"] = "Traits", ["empty"] = "None." }]),
				Section("Scores", [new JsonObject { ["kind"] = "stats", ["rows_field"] = "scores", ["empty"] = "No scores yet." }])),
			Sources: new Dictionary<string, string> { ["concept"] = "PROFILE`CONCEPT", ["home"] = "PROFILE`HOME", ["traits"] = "SHEET`TRAITS", ["scores"] = "SHEET`SCORES" }),
		new("request", "LayBldTplRequest", "LayBldTplRequestHelp", true, () => Doc("form",
				Section("", [
					Field("subject", "Subject", "text", required: true, maxLength: 80),
					Field("kind", "What it is about", "select", options: [("story", "A story idea"), ("help", "Help with the game"), ("other", "Something else")]),
					Field("details", "Details", "textarea", required: true)])),
			Save: new Dictionary<string, string> { ["subject"] = "REQUEST`SUBJECT", ["kind"] = "REQUEST`KIND", ["details"] = "REQUEST`DETAILS" }),
		new("profile", "LayBldTplProfile", "LayBldTplProfileHelp", true, () => Doc("form",
				Section("About you", [
					Field("concept", "Concept", "text", maxLength: 120),
					Field("pronouns", "Pronouns", "text", maxLength: 40),
					Field("likes", "Likes", "list"),
					Field("open_to", "Open to", "multiselect", options: [("action", "Action"), ("drama", "Drama"), ("mystery", "Mystery"), ("romance", "Romance")])])),
			Sources: new Dictionary<string, string> { ["concept"] = "PROFILE`CONCEPT", ["pronouns"] = "PROFILE`PRONOUNS", ["likes"] = "PROFILE`LIKES", ["open_to"] = "PROFILE`OPEN_TO" },
			Save: new Dictionary<string, string> { ["concept"] = "PROFILE`CONCEPT", ["pronouns"] = "PROFILE`PRONOUNS", ["likes"] = "PROFILE`LIKES", ["open_to"] = "PROFILE`OPEN_TO" }),
	];

	public static Template Named(string key) => All.First(t => t.Key == key);

	private static JsonObject Doc(string kind, params JsonObject[] sections)
	{
		var array = new JsonArray();
		var order = 1;
		foreach (var section in sections)
		{
			section["order"] = order++;
			array.Add(section);
		}
		return new JsonObject
		{
			["kind"] = kind, ["schema_version"] = 1, ["title"] = "",
			["pages"] = new JsonArray(new JsonObject { ["key"] = "page1", ["title"] = "Page 1", ["order"] = 1, ["sections"] = array })
		};
	}

	private static JsonObject Section(string name, JsonObject[] elements) =>
		new() { ["name"] = name, ["elements"] = new JsonArray([.. elements]) };

	private static JsonObject Field(string key, string label, string type, bool required = false, int? maxLength = null,
		(string Value, string Label)[]? options = null)
	{
		var field = new JsonObject { ["kind"] = "field", ["key"] = key, ["label"] = label, ["type"] = type };
		var validation = new JsonObject();
		if (required) validation["required"] = true;
		if (maxLength is { } max) validation["max_length"] = max;
		if (validation.Count > 0) field["validation"] = validation;
		if (options is not null)
			field["options"] = new JsonArray(options.Select(JsonNode? (o) => new JsonObject { ["value"] = o.Value, ["label"] = o.Label }).ToArray());
		return field;
	}
}
