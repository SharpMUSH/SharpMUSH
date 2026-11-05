using System.Text.Json;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Components.Schema;

/// <summary>
/// The parts of an action reply that change a page without replacing it: <c>replace</c> swaps keyed sections
/// and elements in the document, <c>data_merge</c> lays fields over the data key by key.
/// </summary>
public static class SchemaPatch
{
	/// <summary>
	/// <paramref name="document"/> with each replacement made, and the keys that named nothing on it (or
	/// whose <c>with</c> was not a section or element).
	/// </summary>
	public static (PortalSchemaDocument Document, IReadOnlyList<string> Skipped) Replace(
		PortalSchemaDocument document, IReadOnlyList<SchemaReplacement> replacements)
	{
		var skipped = new List<string>();
		foreach (var replacement in replacements)
		{
			var (next, done) = replacement switch
			{
				{ Section: { Length: > 0 } key, With: { } with } when Read<SchemaSection>(with) is { } section
					=> Sections(document, found => found.Key == key ? section with { Key = key } : found),
				{ Element: { Length: > 0 } key, With: { } with } when Read<SchemaElement>(with) is { } element
					=> Sections(document, found => found.Elements?.Any(old => old.Key == key) == true
						? found with { Elements = [.. found.Elements.Select(old => old.Key == key ? element with { Key = key } : old)] }
						: found),
				_ => (document, false),
			};
			if (done) document = next;
			else skipped.Add(replacement.Section ?? replacement.Element ?? "?");
		}
		return (document, skipped);
	}

	/// <summary><paramref name="data"/> with each field of <paramref name="merge"/> laid over it; the rest kept.</summary>
	public static SchemaData Merge(SchemaData? data, SchemaData merge)
	{
		var fields = new Dictionary<string, SchemaFieldValue>(data?.Fields ?? new Dictionary<string, SchemaFieldValue>(), StringComparer.Ordinal);
		foreach (var (key, value) in merge.Fields ?? new Dictionary<string, SchemaFieldValue>()) fields[key] = value;
		return new SchemaData(fields);
	}

	/// <summary>Every section passed through <paramref name="change"/>; done when any came back as another one.</summary>
	private static (PortalSchemaDocument, bool) Sections(PortalSchemaDocument document, Func<SchemaSection, SchemaSection> change)
	{
		var done = false;
		var pages = (document.Pages ?? []).Select(page => page with
		{
			Sections = page.Sections?.Select(section =>
			{
				var changed = change(section);
				done |= !ReferenceEquals(changed, section);
				return changed;
			}).ToList(),
		}).ToList();
		return (document with { Pages = pages }, done);
	}

	private static T? Read<T>(JsonElement json) where T : class
	{
		if (json.ValueKind != JsonValueKind.Object) return null;
		try
		{
			return json.Deserialize<T>(SchemaJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
