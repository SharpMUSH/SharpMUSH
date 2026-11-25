using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Client.Models;

/// <summary>
/// A named set of channels the Play page shows as one channel view, each line labelled with its channel. It
/// belongs to the account, not a character: a channel the playing character is not on simply brings no lines.
/// </summary>
/// <param name="Name">What the player called it, unique among their groups whatever the case.</param>
/// <param name="Channels">The channels' names, in the order the player chose them; at least two.</param>
public sealed record CommGroup(string Name, IReadOnlyList<string> Channels)
{
	/// <summary>The longest a group's name may be.</summary>
	public const int NameLimit = 32;

	/// <summary>How many groups an account may keep.</summary>
	public const int GroupLimit = 20;

	/// <summary>The fewest channels a group holds: one channel is just that channel.</summary>
	public const int MinimumChannels = 2;

	/// <summary>
	/// The most channels a group holds. With <see cref="GroupLimit"/> groups of channels named as long as PennMUSH
	/// allows, the stored list stays under the 16 KiB the account's Play setting keeps.
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

	/// <summary>The groups as stored: a JSON array of <c>{"name":…,"channels":[…]}</c>.</summary>
	public static string ToJson(IEnumerable<CommGroup> groups) =>
		new JsonArray(groups.Select(group => (JsonNode)new JsonObject
		{
			["name"] = group.Name,
			["channels"] = new JsonArray(group.Channels.Select(channel => (JsonNode?)JsonValue.Create(channel)).ToArray()),
		}).ToArray()).ToJsonString();

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
			return channels.Length is >= MinimumChannels and <= ChannelLimit ? new CommGroup(name, channels) : null;
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
