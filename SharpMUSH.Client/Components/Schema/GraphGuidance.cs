using System.Text.Json;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Components.Schema;

/// <summary>
/// What softcode says about a graph, read from the page's data: the Check's guidance, a reviewer's notes, and what
/// a revision changed. Each line may name the step (<c>node</c>) or the outcome (<c>edge</c>, <c>"step/port"</c>)
/// it is about.
/// </summary>
/// <remarks>
/// Guidance is <c>[{ node, edge, severity, message }]</c>, or <c>{ issues: [...], hints: [...] }</c> where a hint
/// is a line or a message with no step. Severity is <c>error</c>, <c>warning</c> or <c>hint</c>. Notes are
/// <c>[{ node, edge, author, text }]</c>; changes <c>[{ node, change, text }]</c> with change <c>added</c> or
/// <c>changed</c>.
/// </remarks>
public sealed record GraphGuidance(
	IReadOnlyList<GraphGuidance.Issue> Issues,
	IReadOnlyList<GraphGuidance.Note> Notes,
	IReadOnlyList<GraphGuidance.Change> Changes)
{
	public static readonly GraphGuidance None = new([], [], []);

	/// <summary>One line of guidance.</summary>
	public sealed record Issue(string? Node, string? Port, string Severity, string Message);

	/// <summary>A reviewer's note, pinned to a step or an outcome.</summary>
	public sealed record Note(string? Node, string? Port, string? Author, string Text);

	/// <summary>A step a revision added or changed.</summary>
	public sealed record Change(string Node, string Kind, string? Text);

	/// <summary>Reads the three from <paramref name="data"/> under the names <paramref name="spec"/> gives them.</summary>
	public static GraphGuidance Read(SchemaGraphSpec? spec, SchemaData? data)
	{
		if (spec is null || data?.Fields is not { } fields) return None;
		return new GraphGuidance(
			ReadIssues(Value(fields, spec.IssuesField)),
			[.. Rows(Value(fields, spec.NotesField)).Select(row => (Where(row), Text(row, "text"), Text(row, "author")))
				.Where(note => !string.IsNullOrWhiteSpace(note.Item2))
				.Select(note => new Note(note.Item1.Node, note.Item1.Port, note.Item3, note.Item2!))],
			[.. Rows(Value(fields, spec.ChangesField)).Select(row => (Text(row, "node"), Text(row, "change")?.ToLowerInvariant(), Text(row, "text")))
				.Where(change => !string.IsNullOrEmpty(change.Item1) && change.Item2 is "added" or "changed")
				.Select(change => new Change(change.Item1!, change.Item2!, change.Item3))]);
	}

	/// <summary>The guidance on one step, or on one of its outcomes.</summary>
	public IEnumerable<Issue> On(string node) => Issues.Where(issue => issue.Node == node);

	public IEnumerable<Note> NotesOn(string node) => Notes.Where(note => note.Node == node);

	public Change? ChangeOf(string node) => Changes.FirstOrDefault(change => change.Node == node);

	private static IReadOnlyList<Issue> ReadIssues(JsonElement? value)
	{
		if (value is not { } element) return [];
		if (element.ValueKind == JsonValueKind.Object)
		{
			var issues = element.TryGetProperty("issues", out var list) ? ReadIssues(list) : [];
			var hints = element.TryGetProperty("hints", out var hintList) && hintList.ValueKind == JsonValueKind.Array
				? hintList.EnumerateArray().Select(Hint).OfType<Issue>()
				: [];
			return [.. issues, .. hints];
		}

		return [.. Rows(element).Select(row => (Where(row), Text(row, "message"), Text(row, "severity")))
			.Where(issue => !string.IsNullOrWhiteSpace(issue.Item2))
			.Select(issue => new Issue(issue.Item1.Node, issue.Item1.Port, Severity(issue.Item3), issue.Item2!))];
	}

	private static Issue? Hint(JsonElement hint) => hint.ValueKind switch
	{
		JsonValueKind.String when !string.IsNullOrWhiteSpace(hint.GetString()) => new Issue(null, null, "hint", hint.GetString()!),
		JsonValueKind.Object when Text(hint, "message") is { Length: > 0 } message => new Issue(Where(hint).Node, Where(hint).Port, "hint", message),
		_ => null
	};

	private static string Severity(string? severity) => severity?.ToLowerInvariant() switch
	{
		"error" => "error",
		"hint" or "info" => "hint",
		_ => "warning"
	};

	// An edge is "step/port"; its step is the line's step when the line names no step of its own.
	private static (string? Node, string? Port) Where(JsonElement row)
	{
		var node = Text(row, "node");
		if (Text(row, "edge") is not { } edge) return (node, null);
		var slash = edge.LastIndexOf('/');
		return slash <= 0 ? (node, null) : (node ?? edge[..slash], edge[(slash + 1)..]);
	}

	private static JsonElement? Value(IReadOnlyDictionary<string, SchemaFieldValue> fields, string? key)
		=> key is not null && fields.TryGetValue(key, out var datum) && datum.Visible ? datum.Value : null;

	private static IEnumerable<JsonElement> Rows(JsonElement? value)
		=> value is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object) : [];

	private static string? Text(JsonElement row, string name)
		=> row.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;
}
