using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Plugins.Scene.Models;

/// <summary>
/// One scene detail as the catalogue defines it: <c>DETAIL`&lt;KEY&gt;</c> on the Scene Logger, holding JSON. A detail is
/// a key of the scene's <see cref="Scene.Meta"/> bag that the game names and explains, such as the verse or canon a
/// scene is set in. The catalogue says what a detail is called and how it is shown; the value is the scene's.
/// </summary>
/// <param name="Key">The detail's key, lower case: the <see cref="Scene.Meta"/> key its value is kept under.</param>
/// <param name="Label">Its name for people: the row on the scene's card, the field in the portal.</param>
/// <param name="Hint">One line saying what goes in it, or empty.</param>
/// <param name="Choices">The values it allows; empty when any text will do.</param>
/// <param name="Many">Whether a scene may give it several values, kept split by <see cref="SceneDetails.Separator"/>.</param>
/// <param name="Listed">Whether it is shown beside the title in scene lists and on the portal's scene cards.</param>
/// <param name="Tone">
/// A tone's name (<see cref="PoseTypes.ToneNames"/>) its values are shown in, in +scene and the portal, or empty for the
/// surrounding text's colour. A content warning takes <c>warning</c>.
/// </param>
/// <param name="Order">Its place in lists, lowest first.</param>
/// <param name="Hidden">
/// Whether it is kept for code only: never shown on a scene's card, in its log or lists, or in the portal, and never
/// searched. A sort key (<paramref name="Sort"/>) is one.
/// </param>
/// <param name="Sort">
/// The key of the detail that puts scenes in this one's order, a number for each scene, or empty when this one's own
/// value orders them. The timeline package's <c>icdate</c> is the words a scene's IC date is written in and sorts by
/// <c>icsecs</c>, the IC moment in seconds.
/// </param>
public sealed record SceneDetail(string Key, string Label, string Hint, IReadOnlyList<string> Choices, bool Many, bool Listed,
	string Tone, int Order, bool Hidden = false, string Sort = "");

/// <summary>A <c>DETAIL`</c> attribute the catalogue left out, and why.</summary>
public sealed record SceneDetailProblem(string Key, string Reason);

/// <summary>Every scene detail that reads, in order, and the ones that do not.</summary>
public sealed record SceneDetailCatalogue(IReadOnlyList<SceneDetail> Details, IReadOnlyList<SceneDetailProblem> Problems)
{
	public static readonly SceneDetailCatalogue Empty = new([], []);
}

/// <summary>Scene detail keys, and reading the catalogue's JSON.</summary>
public static partial class SceneDetails
{
	/// <summary>What splits the values of a detail that takes several.</summary>
	public const char Separator = '|';

	public const string InvalidKey = "A scene detail is 1 to 32 letters, digits, - and _, starting with a letter.";

	/// <summary>
	/// The scene fields a detail can't be named for: the ones <c>scene()</c> answers itself and the keys
	/// <c>@scene/set</c> routes to a field or an edge, plus the title and pitch, which have commands of their own.
	/// </summary>
	public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
	{
		"status", "id", "public", "istemp", "scheduledfor", "startedat", "lastactivityat", "posecount", "owner",
		"ownername", "starter", "startername", "room", "roomname", "plot", "title", "summary", "image", "details",
	};

	private const int DefaultOrder = 50;

	[GeneratedRegex("^[a-z][a-z0-9_-]{0,31}$")]
	private static partial Regex KeyPattern();

	/// <summary><paramref name="key"/> as a detail is kept, lower case; an error when it can't be one.</summary>
	public static Result<string> Normalize(string? key)
	{
		var trimmed = (key ?? "").Trim().ToLowerInvariant();
		if (!KeyPattern().IsMatch(trimmed)) return new Error<string>(InvalidKey);
		return Reserved.Contains(trimmed)
			? new Error<string>($"'{trimmed}' is a scene field already, so it can't be a detail")
			: trimmed;
	}

	/// <summary>The values of <paramref name="value"/>, split for a detail that takes several, trimmed, blanks left out.</summary>
	public static IReadOnlyList<string> Values(SceneDetail detail, string value) =>
		detail.Many
			? [.. value.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
			: value.Trim() is { Length: > 0 } one ? [one] : [];

	/// <summary>The detail <paramref name="json"/> defines for <paramref name="key"/>, or why it does not read.</summary>
	public static Result<SceneDetail> Read(string key, string json)
		=> Normalize(key) switch
		{
			string normal => ReadJson(normal, json),
			Error<string> invalid => invalid,
		};

	private static Result<SceneDetail> ReadJson(string normal, string json)
	{

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json);
		}
		catch (JsonException exception)
		{
			return new Error<string>($"not JSON: {exception.Message}");
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object) return new Error<string>("not a JSON object");

			var detail = new SceneDetail(normal, normal, "", [], false, false, "", DefaultOrder);
			foreach (var property in document.RootElement.EnumerateObject())
			{
				var value = property.Value;
				Result<SceneDetail> read = property.Name switch
				{
					"label" => Text(value) is { Length: > 0 } label ? detail with { Label = label } : Bad("label must be text"),
					"hint" => Text(value) is { } hint ? detail with { Hint = hint } : Bad("hint must be text"),
					"choices" => Choices(value) is { } choices ? detail with { Choices = choices } : Bad("choices must be a list of text"),
					"many" => value.ValueKind is JsonValueKind.True or JsonValueKind.False
						? detail with { Many = value.GetBoolean() }
						: Bad("many must be true or false"),
					"listed" => value.ValueKind is JsonValueKind.True or JsonValueKind.False
						? detail with { Listed = value.GetBoolean() }
						: Bad("listed must be true or false"),
					"tone" => Text(value) is { } tone && (tone.Length == 0 || PoseTypes.ToneNames.Contains(tone, StringComparer.OrdinalIgnoreCase))
						? detail with { Tone = tone.ToLowerInvariant() }
						: Bad($"tone must be one of {string.Join(", ", PoseTypes.ToneNames)}"),
					"order" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var order)
						? detail with { Order = order }
						: Bad("order must be a whole number"),
					"hidden" => value.ValueKind is JsonValueKind.True or JsonValueKind.False
						? detail with { Hidden = value.GetBoolean() }
						: Bad("hidden must be true or false"),
					"sort" => Text(value) is { } sort
						? SortKey(normal, sort) switch
						{
							string key => detail with { Sort = key },
							Error<string> error => error,
						}
						: Bad("sort must be text"),
					_ => Bad($"'{property.Name}' is not a field; the fields are label, hint, choices, many, listed, tone, order, hidden, sort"),
				};
				switch (read)
				{
					case SceneDetail next:
						detail = next;
						break;
					case Error<string> error:
						return error;
				}
			}
			return detail;
		}
	}

	/// <summary>
	/// <paramref name="value"/> as <paramref name="detail"/> keeps it: each value trimmed, joined by the separator, and
	/// each one written as its choice is when the detail has choices. An error naming the choices for one it doesn't allow.
	/// </summary>
	public static Result<string> Accept(SceneDetail detail, string value)
	{
		var values = Values(detail, value);
		if (detail.Choices.Count == 0) return string.Join(Separator, values);

		var accepted = new List<string>();
		foreach (var one in values)
		{
			if (detail.Choices.FirstOrDefault(c => c.Equals(one, StringComparison.OrdinalIgnoreCase)) is not { } choice)
				return new Error<string>($"{detail.Label} can be {string.Join(", ", detail.Choices)}");
			accepted.Add(choice);
		}
		return string.Join(Separator, accepted);
	}

	/// <summary>The key <paramref name="sort"/> names for detail <paramref name="key"/> to sort by: empty, or another detail's key.</summary>
	private static Result<string> SortKey(string key, string sort)
	{
		if (sort.Length == 0) return string.Empty;
		return Normalize(sort) switch
		{
			string other when other == key => new Error<string>("sort names another detail, not this one"),
			string other => other,
			Error<string> error => new Error<string>($"sort: {error.Value}"),
		};
	}

	/// <summary>
	/// The number <paramref name="value"/> holds as a sort key, or null when it holds none: a scene with no number sorts
	/// after every scene with one.
	/// </summary>
	public static decimal? SortValue(string? value) =>
		decimal.TryParse(value?.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number)
			? number
			: null;

	private static IReadOnlyList<string>? Choices(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Array) return null;
		var choices = new List<string>();
		foreach (var item in value.EnumerateArray())
		{
			if (Text(item) is not { Length: > 0 } choice || choice.Contains(Separator)) return null;
			choices.Add(choice);
		}
		return choices;
	}

	private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : null;

	private static Result<SceneDetail> Bad(string reason) => new Error<string>(reason);
}
