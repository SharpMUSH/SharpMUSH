using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMUSH.Client.Models.Applications;

/// <summary>
/// Client-side models for the Portal Schema Document (Area 21) — the JSON a game's softcode serves
/// to describe a form or view. The portal is a pure renderer: these records carry no game policy.
/// Deserialized with <see cref="SchemaJson.Options"/> (snake_case), so softcode field names like
/// <c>schema_version</c>, <c>visible_to</c>, <c>src_field</c>, and <c>on_success</c> map directly.
/// See <c>docs/design/dynamic-applications.md</c>.
/// </summary>
public static class SchemaJson
{
	/// <summary>Shared options: snake_case property names, case-insensitive, lenient numbers.</summary>
	public static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		PropertyNameCaseInsensitive = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
}

/// <summary>The whole schema document. <c>Kind</c> is "form" or "view".</summary>
public sealed record PortalSchemaDocument(
	string? Kind,
	int SchemaVersion,
	string? Title,
	string? DataSource,
	IReadOnlyList<SchemaPage>? Pages,
	IReadOnlyDictionary<string, SchemaAction>? Actions)
{
	/// <summary>True for an input form (vs. a read-only view).</summary>
	public bool IsForm => string.Equals(Kind, "form", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One page of a (possibly multi-step) document.</summary>
public sealed record SchemaPage(
	string? Key,
	string? Title,
	int Order,
	IReadOnlyList<SchemaSection>? Sections,
	string? Next,
	string? Prev);

/// <summary>A section grouping related elements.</summary>
public sealed record SchemaSection(
	string? Name,
	int Order,
	[property: JsonPropertyName("visible_to")] string? VisibleTo,
	IReadOnlyList<SchemaElement>? Elements);

/// <summary>
/// A single element. <c>Kind</c> discriminates: "field" (an input/value) or a display element
/// ("markdown", "image", "table", "keyvalue", "divider", "button"). Unused properties stay null.
/// </summary>
public sealed record SchemaElement(
	string? Kind = null,
	// field
	string? Key = null,
	string? Label = null,
	string? Type = null,
	IReadOnlyList<SchemaOption>? Options = null,
	JsonElement? Default = null,
	string? Help = null,
	SchemaValidation? Validation = null,
	[property: JsonPropertyName("visible_to")] string? VisibleTo = null,
	// markdown / static value
	string? Value = null,
	// image
	[property: JsonPropertyName("src_field")] string? SrcField = null,
	string? Alt = null,
	// table
	[property: JsonPropertyName("rows_field")] string? RowsField = null,
	IReadOnlyList<SchemaColumn>? Columns = null,
	// keyvalue
	IReadOnlyList<string>? Fields = null,
	// button
	string? Action = null);

/// <summary>A select/radio/multiselect choice.</summary>
public sealed record SchemaOption(string Value, string Label);

/// <summary>A table column definition.</summary>
public sealed record SchemaColumn(string Key, string Label);

/// <summary>Advisory client-side validation hints. Softcode is the authoritative validator.</summary>
public sealed record SchemaValidation(
	bool Required,
	double? Min,
	double? Max,
	[property: JsonPropertyName("max_length")] int? MaxLength,
	string? Pattern);

/// <summary>An action (button/submit). Transport is always "http" in v1.</summary>
public sealed record SchemaAction(
	string? Transport,
	string? Method,
	string? Route,
	string? Payload,
	[property: JsonPropertyName("on_success")] SchemaActionSuccess? OnSuccess,
	[property: JsonPropertyName("on_error")] SchemaActionError? OnError);

/// <summary>What to do when an action succeeds.</summary>
public sealed record SchemaActionSuccess(
	string? Navigate,
	string? Toast,
	[property: JsonPropertyName("merge_fields")] bool MergeFields);

/// <summary>What to do when an action fails.</summary>
public sealed record SchemaActionError(
	[property: JsonPropertyName("bind_field_errors")] bool BindFieldErrors);

/// <summary>
/// The action response envelope returned by a softcode POST handler. The <c>errors</c> shape
/// matches DynamicConfig.razor's parser: <c>_global</c> → snackbar, keyed → per-field.
/// </summary>
public sealed record SchemaActionResult(
	bool Ok,
	IReadOnlyDictionary<string, string>? Errors,
	IReadOnlyDictionary<string, JsonElement>? Fields,
	PortalSchemaDocument? Schema,
	string? Redirect,
	string? Message);

/// <summary>
/// A data payload for a view / form prefill — values keyed like the profile API
/// (<c>Fields[key] = { value, visible }</c>). <c>Value</c> is a raw element to allow scalars,
/// arrays (tables), and objects.
/// </summary>
public sealed record SchemaData(IReadOnlyDictionary<string, SchemaFieldValue>? Fields);

/// <summary>One datum: its value plus whether the viewer may see it (softcode-owned).</summary>
public sealed record SchemaFieldValue(JsonElement? Value, bool Visible = true);
