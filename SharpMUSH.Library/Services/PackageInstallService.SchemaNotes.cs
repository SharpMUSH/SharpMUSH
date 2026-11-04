using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Library.Services;

public partial class PackageInstallService
{
	/// <summary>
	/// The problems <see cref="PortalSchema.Check"/> finds in the portal documents an apply writes: an attribute
	/// that is one whole, and each <c>lit()</c> template of a form or view in softcode that fills one in. A
	/// template's empty lists are filled at run time, so what it lacks is not a problem; a document softcode
	/// builds some other way is checked by <c>schemacheck()</c> at run time instead.
	/// </summary>
	internal static IEnumerable<string> SchemaNotes(PackageChangeset changeset)
	{
		foreach (var change in changeset.Attributes)
		{
			if (change.NewValue is not { Length: > 0 } value) continue;
			foreach (var (document, template) in Documents(value))
			{
				foreach (var problem in PortalSchema.Check(document).Where(problem => !template || !problem.Contains(" needs ", StringComparison.Ordinal)))
					yield return $"Portal document in {change.TargetRef}/{change.Attribute}: {problem}";
			}
		}
	}

	private static IEnumerable<(JsonObject Document, bool Template)> Documents(string value)
	{
		if (value.TrimStart().StartsWith('{') && Parse(value) is { } whole)
		{
			yield return (whole, false);
			yield break;
		}
		for (var at = value.IndexOf("lit(", StringComparison.OrdinalIgnoreCase); at >= 0; at = value.IndexOf("lit(", at + 4, StringComparison.OrdinalIgnoreCase))
		{
			if (Argument(value, at + 4) is { } argument && argument.TrimStart().StartsWith('{') && Parse(argument) is { } template)
				yield return (template, true);
		}
	}

	/// <summary>A form or view, or nothing when the text is not one.</summary>
	private static JsonObject? Parse(string text)
	{
		try
		{
			return JsonNode.Parse(text) is JsonObject document && document["pages"] is not null
				&& PortalSchema.DocumentKinds.Contains(PortalSchema.Text(document, "kind")) ? document : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>The text from <paramref name="start"/> to the parenthesis that closes it, outside JSON strings.</summary>
	private static string? Argument(string value, int start)
	{
		var depth = 0;
		var quoted = false;
		for (var i = start; i < value.Length; i++)
		{
			switch (value[i])
			{
				case '\\' when quoted:
					i++;
					break;
				case '"':
					quoted = !quoted;
					break;
				case '(' when !quoted:
					depth++;
					break;
				case ')' when !quoted && depth == 0:
					return value[start..i];
				case ')' when !quoted:
					depth--;
					break;
			}
		}
		return null;
	}
}
