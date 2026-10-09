using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Plugins.Scene.Models;

/// <summary>
/// One pose type as the catalogue defines it: <c>TYPE`&lt;KEY&gt;</c> on the Scene Logger, holding JSON. How the type
/// reads on telnet is softcode beside it, <c>TYPE`&lt;KEY&gt;`FORMAT</c>, which the scene package runs; this is what
/// the portal and the filters need.
/// </summary>
/// <param name="Key">The type's key, lower case: what a pose records.</param>
/// <param name="Label">Its name in the portal's Show menu, the admin page and recall.</param>
/// <param name="Presentation">The portal layout: one of <see cref="PoseTypes.Presentations"/>.</param>
/// <param name="Tone">A theme colour's name (<see cref="ToneMarkup.Roles"/>), or empty for the theme's text colour.</param>
/// <param name="Icon">One of <see cref="PoseTypes.Icons"/>, or empty for none.</param>
/// <param name="Hidden">Whether a reader who has not chosen starts with this type hidden.</param>
/// <param name="Order">Its place in lists, lowest first.</param>
public sealed record PoseType(string Key, string Label, string Presentation, string Tone, string Icon, bool Hidden, int Order);

/// <summary>A <c>TYPE`</c> attribute the catalogue left out, and why.</summary>
public sealed record PoseTypeProblem(string Key, string Reason);

/// <summary>Every pose type that reads, in order, and the ones that do not.</summary>
public sealed record PoseTypeCatalogue(IReadOnlyList<PoseType> Types, IReadOnlyList<PoseTypeProblem> Problems)
{
	public static readonly PoseTypeCatalogue Empty = new([], []);

	/// <summary>The type <paramref name="key"/> names, or one drawn as in character when the catalogue has none.</summary>
	public PoseType For(string key) =>
		Types.FirstOrDefault(t => t.Key == key) ?? PoseTypes.Unlisted(key);
}

/// <summary>Pose type keys, and reading the catalogue's JSON.</summary>
public static partial class PoseTypes
{
	/// <summary>The type a pose has when none is given, and that a pose stored before types has.</summary>
	public const string InCharacter = "ic";

	/// <summary>The type the <c>ooc</c> command records.</summary>
	public const string OutOfCharacter = "ooc";

	public const string InvalidKey = "A pose type is 1 to 32 letters, digits, - and _, starting with a letter or digit.";

	/// <summary>The portal layouts a type can take.</summary>
	public static readonly IReadOnlyList<string> Presentations = ["prose", "band", "message", "aside", "notice"];

	/// <summary>The icons a type can show in the portal.</summary>
	public static readonly IReadOnlyList<string> Icons =
		["radio", "phone", "chat", "dice", "book", "scroll", "megaphone", "eye", "mask", "music", "star", "bolt"];

	private const int DefaultOrder = 50;

	[GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,31}$")]
	private static partial Regex KeyPattern();

	/// <summary><paramref name="key"/> as a type is kept: lower case, <c>ic</c> when empty; an error when it cannot be one.</summary>
	public static Result<string> Normalize(string? key)
	{
		var trimmed = (key ?? "").Trim().ToLowerInvariant();
		if (trimmed.Length == 0) return InCharacter;
		return KeyPattern().IsMatch(trimmed) ? trimmed : new Error<string>(InvalidKey);
	}

	/// <summary>A type the catalogue does not list: drawn as in character, named by its key.</summary>
	public static PoseType Unlisted(string key) => new(key, key, "prose", "", "", false, DefaultOrder);

	/// <summary>The type <paramref name="json"/> defines for <paramref name="key"/>, or why it does not read.</summary>
	public static Result<PoseType> Read(string key, string json)
	{
		if (Normalize(key) is not string normal || normal != key.ToLowerInvariant()) return new Error<string>("its name is not a type key");

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

			var type = new PoseType(normal, normal, "prose", "", "", false, DefaultOrder);
			foreach (var property in document.RootElement.EnumerateObject())
			{
				var value = property.Value;
				Result<PoseType> read = property.Name switch
				{
					"label" => Text(value) is { Length: > 0 } label ? type with { Label = label } : Bad("label must be text"),
					"presentation" => Text(value) is { } p && Presentations.Contains(p)
						? type with { Presentation = p }
						: Bad($"presentation must be one of {string.Join(", ", Presentations)}"),
					"tone" => Text(value) is { } tone && (tone.Length == 0 || ToneMarkup.TryParse(tone, out _))
						? type with { Tone = tone.ToLowerInvariant() }
						: Bad($"tone must be one of {string.Join(", ", ToneNames)}"),
					"icon" => Text(value) is { } icon && (icon.Length == 0 || Icons.Contains(icon))
						? type with { Icon = icon }
						: Bad($"icon must be one of {string.Join(", ", Icons)}"),
					"hidden" => value.ValueKind is JsonValueKind.True or JsonValueKind.False
						? type with { Hidden = value.GetBoolean() }
						: Bad("hidden must be true or false"),
					"order" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var order)
						? type with { Order = order }
						: Bad("order must be a whole number"),
					_ => Bad($"'{property.Name}' is not a field; the fields are label, presentation, tone, icon, hidden, order"),
				};
				switch (read)
				{
					case PoseType next:
						type = next;
						break;
					case Error<string> error:
						return error;
				}
			}
			return type;
		}
	}

	/// <summary>The tone names a type may use, as <c>+scene/type/set</c> lists them.</summary>
	public static IEnumerable<string> ToneNames => ToneMarkup.Roles.Select(ThemePalette.RoleName).Order();

	private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : null;

	private static Result<PoseType> Bad(string reason) => new Error<string>(reason);
}
