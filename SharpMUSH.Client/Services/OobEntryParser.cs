using System.Globalization;
using System.Text.Json;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads the <c>room-contents</c> OOB payloads (<c>room.info</c>, <c>room.contents</c>,
/// <c>room.exits</c>) into <see cref="RoomInfo"/>, <see cref="RoomOccupant"/> and <see cref="RoomExit"/>.
/// The contract is <c>docs/softcode/room-contents-handler.md</c>.
/// </summary>
/// <remarks>
/// <para>A payload whose <c>v</c> is 2 or more is read for every v2 key. A payload without it is v1 and is
/// read for the v1 keys only — <c>dbref</c>, <c>name</c> and <c>cmd</c> — so every other member comes back
/// null, false or empty.</para>
/// <para>The payload is text a game's softcode wrote, so nothing in it is trusted to be well formed, and
/// damage is contained to the smallest piece: a malformed member is dropped on its own (a bad
/// <c>image</c> leaves the row, a bad <c>focal</c> leaves the image), and a malformed row is dropped on
/// its own (the rest of the list survives). Only a payload that is not a JSON object at all, or whose
/// list is not an array, reads as empty.</para>
/// <para>URLs are kept exactly as sent. Whether a picture may be shown is decided where it is rendered.
/// A colour is kept only as <c>#rrggbb</c>, because it is written into a CSS custom property.</para>
/// </remarks>
public static class OobEntryParser
{
	/// <summary>The package carrying the room itself.</summary>
	public const string RoomInfoPackage = "room.info";

	/// <summary>The package carrying the room's occupants, under <c>who</c>.</summary>
	public const string RoomContentsPackage = "room.contents";

	/// <summary>The package carrying the room's exits, under <c>exits</c>.</summary>
	public const string RoomExitsPackage = "room.exits";

	/// <summary>Parses a <c>room.contents</c> payload. Never throws; anything unreadable is empty.</summary>
	public static IReadOnlyList<RoomOccupant> ParseOccupants(string? dataJson) =>
		ParseList(dataJson, "who", ReadOccupant);

	/// <summary>Parses a <c>room.exits</c> payload. Never throws; anything unreadable is empty.</summary>
	public static IReadOnlyList<RoomExit> ParseExits(string? dataJson) =>
		ParseList(dataJson, "exits", ReadExit);

	/// <summary>Parses a <c>room.info</c> payload, or null when it is not a JSON object.</summary>
	public static RoomInfo? ParseRoomInfo(string? dataJson)
	{
		if (Open(dataJson) is not { } document) return null;

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return null;

			var name = Str(root, "name") ?? string.Empty;
			var dbref = Str(root, "dbref");
			if (!IsV2(root)) return new RoomInfo(dbref, name, null, null, null, null, null);

			return new RoomInfo(
				dbref,
				name,
				ObjId: Str(root, "objid"),
				Area: Str(root, "area"),
				Image: Image(root, "image"),
				Desc: Description(root),
				Scene: Scene(root));
		}
	}

	private static IReadOnlyList<T> ParseList<T>(string? dataJson, string arrayProperty,
		Func<JsonElement, bool, T?> readRow) where T : class
	{
		if (Open(dataJson) is not { } document) return [];

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty(arrayProperty, out var array)
				|| array.ValueKind != JsonValueKind.Array)
				return [];

			var v2 = IsV2(root);
			var rows = new List<T>();
			foreach (var item in array.EnumerateArray())
			{
				if (readRow(item, v2) is { } row) rows.Add(row);
			}

			return rows;
		}
	}

	private static JsonDocument? Open(string? dataJson)
	{
		if (string.IsNullOrWhiteSpace(dataJson)) return null;

		try
		{
			return JsonDocument.Parse(dataJson);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static bool IsV2(JsonElement root) =>
		root.TryGetProperty("v", out var v)
		&& v.ValueKind == JsonValueKind.Number
		&& v.TryGetDouble(out var version)
		&& version >= 2;

	private static RoomOccupant? ReadOccupant(JsonElement item, bool v2)
	{
		if (item.ValueKind == JsonValueKind.String)
			return new RoomOccupant(null, item.GetString() ?? string.Empty, null, null, null, null, null, null, null,
				false, false, []);
		if (item.ValueKind != JsonValueKind.Object) return null;

		var dbref = Str(item, "dbref");
		var name = Str(item, "name") ?? string.Empty;
		var cmd = Str(item, "cmd");
		if (!v2)
			return new RoomOccupant(dbref, name, cmd, null, null, null, null, null, null, false, false, []);

		return new RoomOccupant(
			dbref,
			name,
			cmd,
			ObjId: Str(item, "objid"),
			Type: Str(item, "type"),
			Color: Color(item),
			Image: Image(item, "image"),
			Status: Str(item, "status"),
			Idle: Count(item, "idle"),
			Profile: True(item, "profile"),
			You: True(item, "you"),
			Actions: Actions(item));
	}

	private static RoomExit? ReadExit(JsonElement item, bool v2)
	{
		if (item.ValueKind == JsonValueKind.String)
			return new RoomExit(null, item.GetString() ?? string.Empty, null, null, [], null, null, null, null);
		if (item.ValueKind != JsonValueKind.Object) return null;

		var dbref = Str(item, "dbref");
		var name = Str(item, "name") ?? string.Empty;
		var cmd = Str(item, "cmd");
		if (!v2) return new RoomExit(dbref, name, cmd, null, [], null, null, null, null);

		return new RoomExit(
			dbref,
			name,
			cmd,
			ObjId: Str(item, "objid"),
			Aliases: Aliases(item),
			State: State(item),
			Hint: Str(item, "hint"),
			Confirm: Str(item, "confirm"),
			Dest: Destination(item));
	}

	private static string? Str(JsonElement parent, string property) =>
		parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	/// <summary>A string that is present and not empty — for the members an empty value makes meaningless.</summary>
	private static string? Text(JsonElement parent, string property) =>
		Str(parent, property) is { Length: > 0 } text ? text : null;

	/// <summary>A JSON boolean, or null when the property is absent or not a boolean.</summary>
	private static bool? Flag(JsonElement parent, string property) =>
		parent.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;

	private static bool True(JsonElement parent, string property) =>
		parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

	/// <summary>A whole number of zero or more (idle seconds, head counts).</summary>
	private static int? Count(JsonElement parent, string property) =>
		parent.TryGetProperty(property, out var value)
		&& value.ValueKind == JsonValueKind.Number
		&& value.TryGetInt32(out var number)
		&& number >= 0
			? number
			: null;

	/// <summary>A whole number above zero (pixel sizes).</summary>
	private static int? Size(JsonElement parent, string property) =>
		Count(parent, property) is > 0 and var size ? size : null;

	private static string? Color(JsonElement parent) =>
		Str(parent, "color") is { Length: 7 } color
		&& color[0] == '#'
		&& color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") is false
			? color
			: null;

	private static ImageRef? Image(JsonElement parent, string property)
	{
		if (!parent.TryGetProperty(property, out var image)
			|| image.ValueKind != JsonValueKind.Object
			|| Text(image, "url") is not { } url)
			return null;

		return new ImageRef(url, Str(image, "alt"), Focal(image), Size(image, "width"), Size(image, "height"));
	}

	private static (double X, double Y)? Focal(JsonElement image)
	{
		if (!image.TryGetProperty("focal", out var focal)
			|| focal.ValueKind != JsonValueKind.Array
			|| focal.GetArrayLength() != 2)
			return null;

		return Fraction(focal[0]) is { } x && Fraction(focal[1]) is { } y ? (x, y) : null;
	}

	private static double? Fraction(JsonElement value) =>
		value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number is >= 0 and <= 1
			? number
			: null;

	private static IReadOnlyList<OccupantAction> Actions(JsonElement row)
	{
		if (!row.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array) return [];

		return
		[
			.. actions.EnumerateArray()
				.Where(action => action.ValueKind == JsonValueKind.Object)
				.Select(action => Text(action, "label") is { } label && Text(action, "cmd") is { } cmd
					? new OccupantAction(label, cmd)
					: null)
				.OfType<OccupantAction>()
		];
	}

	private static IReadOnlyList<string> Aliases(JsonElement row)
	{
		if (!row.TryGetProperty("aliases", out var aliases) || aliases.ValueKind != JsonValueKind.Array) return [];

		return
		[
			.. aliases.EnumerateArray()
				.Where(alias => alias.ValueKind == JsonValueKind.String)
				.Select(alias => alias.GetString() ?? string.Empty)
				.Where(text => text.Length > 0)
		];
	}

	private static ExitState? State(JsonElement row) =>
		Str(row, "state")?.ToLowerInvariant() switch
		{
			"open" => ExitState.Open,
			"locked" => ExitState.Locked,
			"closed" => ExitState.Closed,
			_ => null,
		};

	private static ExitDestination? Destination(JsonElement row)
	{
		if (!row.TryGetProperty("dest", out var dest) || dest.ValueKind != JsonValueKind.Object) return null;

		return new ExitDestination(
			Str(dest, "name"),
			Str(dest, "area"),
			Image(dest, "image"),
			Str(dest, "desc"),
			Count(dest, "here"));
	}

	private static RoomDescription? Description(JsonElement root)
	{
		if (!root.TryGetProperty("desc", out var desc)
			|| desc.ValueKind != JsonValueKind.Object
			|| Str(desc, "text") is not { } text)
			return null;

		return new RoomDescription(Text(desc, "format") ?? "text", text);
	}

	private static RoomScene? Scene(JsonElement root)
	{
		if (!root.TryGetProperty("scene", out var scene)
			|| scene.ValueKind != JsonValueKind.Object
			|| SceneId(scene) is not { } id)
			return null;

		return new RoomScene(id, Str(scene, "title"), Count(scene, "cast"), Str(scene, "role"), Flag(scene, "focus"));
	}

	/// <summary>The contract's id is a string; board <c>12</c> drew a number, so a whole number reads too.</summary>
	private static string? SceneId(JsonElement scene)
	{
		if (!scene.TryGetProperty("id", out var id)) return null;

		return id.ValueKind switch
		{
			JsonValueKind.String => id.GetString() is { Length: > 0 } text ? text : null,
			JsonValueKind.Number when id.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
			_ => null,
		};
	}
}
