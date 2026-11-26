using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Client.Models;

/// <summary>
/// A named set of channels the Play page shows as one channel view, each line labelled with its channel. It
/// belongs to the account, not a character: a channel the playing character is not on simply brings no lines.
/// </summary>
/// <param name="Name">What the player called it, unique among their groups whatever the case.</param>
/// <param name="Channels">The channels' names, in the order the player chose them; at least two.</param>
/// <param name="Folded">Whether the Play sidebar shows the group as its heading alone, its channels folded away.</param>
/// <param name="Colors">
/// The colour the player chose for a channel, one of <see cref="Hues"/>, by channel name whatever the case. A channel
/// with none takes one of its own (<see cref="HueOf"/>).
/// </param>
public sealed record CommGroup(string Name, IReadOnlyList<string> Channels, bool Folded = false,
	IReadOnlyDictionary<string, string>? Colors = null)
{
	/// <summary>
	/// The colours a channel can take, as the theme's <c>--hue-&lt;name&gt;</c> tokens name them, in the order channels
	/// with none chosen take them: blue and orange first, which differ in lightness as well as hue.
	/// </summary>
	public static readonly IReadOnlyList<string> Hues = ["blue", "orange", "purple", "green", "cyan", "yellow", "pink", "red"];

	/// <summary>
	/// The colour <paramref name="channel"/> shows in, or null when the group does not hold it: the one chosen for it,
	/// else the next of <see cref="Hues"/> no channel of the group chose, in the group's order. Past every colour
	/// they start again; the channel's name still tells them apart.
	/// </summary>
	public string? HueOf(string channel)
	{
		if (!Holds(channel)) return null;
		if (Chosen(channel) is { } chosen) return chosen;
		var free = Hues.Where(hue => !Channels.Any(c => Chosen(c) == hue)).ToList();
		if (free.Count == 0) free = [.. Hues];
		var place = Channels.Where(c => Chosen(c) is null).ToList()
			.FindIndex(c => string.Equals(c, channel, StringComparison.OrdinalIgnoreCase));
		return free[place % free.Count];
	}

	/// <summary>The colour chosen for <paramref name="channel"/>, or null for none.</summary>
	public string? Chosen(string channel) =>
		Colors?.FirstOrDefault(pair => string.Equals(pair.Key, channel, StringComparison.OrdinalIgnoreCase)).Value is { } hue
		&& Hues.Contains(hue) ? hue : null;

	/// <summary>The longest a group's name may be.</summary>
	public const int NameLimit = 32;

	/// <summary>How many groups an account may keep.</summary>
	public const int GroupLimit = 20;

	/// <summary>The fewest channels a group holds: one channel is just that channel.</summary>
	public const int MinimumChannels = 2;

	/// <summary>
	/// The most channels a group holds. <see cref="GroupLimit"/> groups at every limit (longest names, a colour for
	/// each channel, folded) take about 17 KB, inside the 32 KiB the account's Play setting keeps.
	/// </summary>
	public const int ChannelLimit = 16;

	/// <summary>
	/// A group's key in the comm feed's key space. A channel name holds no space and a conversation's key starts
	/// <c>page </c>, so a key starting <c>group </c> is never either.
	/// </summary>
	public const string KeyPrefix = "group ";

	/// <summary>The key the Play page opens this group's view by.</summary>
	public string Key => KeyPrefix + Name;

	/// <summary>Whether <paramref name="key"/> names a group rather than a channel or a conversation.</summary>
	public static bool IsGroupKey(string? key) => key is not null && key.StartsWith(KeyPrefix, StringComparison.Ordinal);

	/// <summary>Whether this group holds <paramref name="channel"/>, by name whatever the case.</summary>
	public bool Holds(string? channel) =>
		channel is not null && Channels.Any(c => string.Equals(c, channel, StringComparison.OrdinalIgnoreCase));

	/// <summary>The groups as stored: a JSON array of <c>{"name":…,"channels":[…]}</c>, with <c>"folded":true</c> on a folded one.</summary>
	public static string ToJson(IEnumerable<CommGroup> groups) =>
		new JsonArray(groups.Select(Entry).ToArray()).ToJsonString();

	private static JsonNode Entry(CommGroup group)
	{
		var entry = new JsonObject
		{
			["name"] = group.Name,
			["channels"] = new JsonArray(group.Channels.Select(channel => (JsonNode?)JsonValue.Create(channel)).ToArray()),
		};
		if (group.Folded) entry["folded"] = true;
		var colors = group.Channels.Where(c => group.Chosen(c) is not null).ToList();
		if (colors.Count > 0)
			entry["colors"] = new JsonObject(colors.Select(c => KeyValuePair.Create(c, (JsonNode?)group.Chosen(c))));
		return entry;
	}

	/// <summary>
	/// Reads stored groups. Anything unreadable is left out rather than refused: a group with no name, fewer than
	/// <see cref="MinimumChannels"/> or more than <see cref="ChannelLimit"/> channels, or a name already read; past <see cref="GroupLimit"/>, the rest.
	/// Nothing stored, or not a JSON array, is no groups.
	/// </summary>
	public static IReadOnlyList<CommGroup> FromJson(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return [];
		JsonNode? root;
		try
		{
			root = JsonNode.Parse(json);
		}
		catch (JsonException)
		{
			return [];
		}
		if (root is not JsonArray array) return [];

		var groups = new List<CommGroup>();
		foreach (var node in array)
		{
			if (groups.Count >= GroupLimit) break;
			if (Read(node) is not { } group
				|| groups.Any(g => string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase))) continue;
			groups.Add(group);
		}
		return groups;
	}

	/// <summary>One stored group, or null when it is not one <see cref="FromJson"/> keeps.</summary>
	private static CommGroup? Read(JsonNode? node)
	{
		if (node is not JsonObject entry) return null;
		try
		{
			if (Text(entry["name"]) is not { Length: <= NameLimit } name) return null;
			var channels = entry["channels"] is JsonArray listed
				? listed.Select(Text).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
				: [];
			var folded = entry["folded"] is JsonValue flag && flag.TryGetValue<bool>(out var isFolded) && isFolded;
			var colors = entry["colors"] is JsonObject chosen
				? channels.Select(c => (Channel: c, Hue: chosen.FirstOrDefault(p => string.Equals(p.Key, c, StringComparison.OrdinalIgnoreCase)).Value))
					.Where(pair => Text(pair.Hue) is { } hue && Hues.Contains(hue))
					.ToDictionary(pair => pair.Channel, pair => Text(pair.Hue)!, StringComparer.OrdinalIgnoreCase)
				: null;
			return channels.Length is >= MinimumChannels and <= ChannelLimit
				? new CommGroup(name, channels, folded, colors is { Count: > 0 } ? colors : null)
				: null;
		}
		catch (ArgumentException)
		{
			// The object names a property twice.
			return null;
		}
	}

	private static string? Text(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue<string>(out var text) && text.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
