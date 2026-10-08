using System.Text.Json;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads the <c>comm-feed</c> OOB payloads (<c>comm.channels</c>, <c>comm.message</c>, <c>comm.who</c>). The contract is
/// <c>docs/softcode/comm-feed-handler.md</c>.
/// </summary>
/// <remarks>
/// <para>As with <see cref="OobEntryParser"/>, the payload is text a game's softcode wrote and nothing in it
/// is trusted to be well formed. A malformed member falls back on its own (an unreadable time is the time
/// the line arrived, a non-string name in <c>to</c> is skipped); a malformed channel row is skipped on its
/// own. What cannot be filed at all — a line with no kind the feed knows, a channel line with no channel,
/// a list with no list — reads as null, and the feed keeps what it had.</para>
/// <para><c>v</c> is not required. There is no earlier version of these payloads, so a payload without it
/// is read exactly as one with <c>"v": 2</c>.</para>
/// </remarks>
public static class CommPayloadParser
{
	/// <summary>The package carrying the viewer's channel list.</summary>
	public const string ChannelsPackage = "comm.channels";

	/// <summary>The package carrying one channel line or page.</summary>
	public const string MessagePackage = "comm.message";

	/// <summary>The package carrying one change to a channel's member list.</summary>
	public const string WhoPackage = "comm.who";

	/// <summary>The <see cref="CommMessage.Kind"/> of a channel line.</summary>
	public const string ChannelKind = "channel";

	/// <summary>The <see cref="CommMessage.Kind"/> of a page.</summary>
	public const string PageKind = "page";

	/// <summary>Parses a <c>comm.channels</c> payload, or null when it holds no list.</summary>
	public static CommChannelList? ParseChannels(string? dataJson)
	{
		if (Open(dataJson) is not { } document) return null;

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("channels", out var rows)
				|| rows.ValueKind != JsonValueKind.Array)
				return null;

			var channels = new List<CommChannel>();
			var serverUnread = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (var row in rows.EnumerateArray())
			{
				if (ReadChannel(row) is not { } channel) continue;

				channels.Add(channel);
				if (row.ValueKind == JsonValueKind.Object && Count(row, "unread") is { } unread)
					serverUnread[channel.Name] = unread;
			}

			return new CommChannelList(Participant(root, "viewer"), channels, serverUnread);
		}
	}

	/// <summary>
	/// Parses a <c>comm.message</c> payload, or null when it cannot be filed. <paramref name="receivedAt"/>
	/// stands in for a missing or unreadable <c>ts</c>.
	/// </summary>
	public static CommEntry? ParseMessage(string? dataJson, DateTimeOffset receivedAt)
	{
		if (Open(dataJson) is not { } document) return null;

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return null;

			if (Str(root, "kind")?.ToLowerInvariant() is not { } kind) return null;

			string? channel;
			switch (kind)
			{
				case ChannelKind:
					if (Text(root, "channel") is not { } name) return null;
					channel = name;
					break;
				case PageKind:
					channel = null;
					break;
				default:
					return null;
			}

			var recipients = Recipients(root);
			var message = new CommMessage(
				kind,
				channel,
				recipients.Select(r => r.Name).ToArray(),
				Str(root, "from") ?? string.Empty,
				Text(root, "fromObjid"),
				Str(root, "text") ?? string.Empty,
				Time(root) ?? receivedAt,
				Id(root));

			return new CommEntry(message, recipients);
		}
	}

	/// <summary>
	/// Parses a <c>comm.who</c> payload, or null when it names no channel, no member with an objid, or no
	/// <c>online</c> true or false: a member list is kept by objid, so a member without one cannot be placed.
	/// </summary>
	public static CommWhoChange? ParseWho(string? dataJson)
	{
		if (Open(dataJson) is not { } document) return null;

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| Text(root, "channel") is not { } channel
				|| Participant(root, "member") is not { ObjId: not null } member
				|| !root.TryGetProperty("online", out var online)
				|| online.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
				return null;

			return new CommWhoChange(channel, member, online.ValueKind == JsonValueKind.True);
		}
	}

	private static CommChannel? ReadChannel(JsonElement row)
	{
		if (row.ValueKind == JsonValueKind.String)
			return row.GetString() is { Length: > 0 } bare ? new CommChannel(bare, 0) : null;
		if (row.ValueKind != JsonValueKind.Object || Text(row, "name") is not { } name) return null;

		var joined = !(row.TryGetProperty("joined", out var value) && value.ValueKind == JsonValueKind.False);
		var gagged = row.TryGetProperty("gagged", out var gag) && gag.ValueKind == JsonValueKind.True;
		return new CommChannel(name, Count(row, "unread") ?? 0, joined, gagged, Count(row, "members"),
			Text(row, "description") ?? string.Empty);
	}

	/// <summary>
	/// <c>to</c>, each name paired with the objid at the same place in <c>toObjids</c> — but only when the
	/// two arrays are the same length. Otherwise nothing says which objid is whose, and none is guessed.
	/// </summary>
	private static IReadOnlyList<CommParticipant> Recipients(JsonElement root)
	{
		if (!root.TryGetProperty("to", out var to) || to.ValueKind != JsonValueKind.Array) return [];

		var objids = root.TryGetProperty("toObjids", out var ids)
			&& ids.ValueKind == JsonValueKind.Array
			&& ids.GetArrayLength() == to.GetArrayLength()
				? ids.EnumerateArray().ToArray()
				: null;

		var recipients = new List<CommParticipant>();
		var index = 0;
		foreach (var item in to.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
			{
				var objid = objids?[index] is { ValueKind: JsonValueKind.String } id && id.GetString() is { Length: > 0 } text
					? text
					: null;
				recipients.Add(new CommParticipant(name, objid));
			}

			index++;
		}

		return recipients;
	}

	private static CommParticipant? Participant(JsonElement parent, string property) =>
		parent.TryGetProperty(property, out var value)
		&& value.ValueKind == JsonValueKind.Object
		&& Text(value, "name") is { } name
			? new CommParticipant(name, Text(value, "objid"))
			: null;

	/// <summary>
	/// <c>id</c>, the id the recall endpoint returns for the same line: a whole number, or none. A line
	/// without one (a page, or a game whose package predates it) is never taken for another.
	/// </summary>
	private static long? Id(JsonElement root) =>
		root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var value)
			? value
			: null;

	private static DateTimeOffset? Time(JsonElement root)
	{
		if (!root.TryGetProperty("ts", out var ts)
			|| ts.ValueKind != JsonValueKind.Number
			|| !ts.TryGetInt64(out var milliseconds))
			return null;

		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
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

	private static string? Str(JsonElement parent, string property) =>
		parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	/// <summary>A string that is present and not empty.</summary>
	private static string? Text(JsonElement parent, string property) =>
		Str(parent, property) is { Length: > 0 } text ? text : null;

	/// <summary>A whole number of zero or more.</summary>
	private static int? Count(JsonElement parent, string property) =>
		parent.TryGetProperty(property, out var value)
		&& value.ValueKind == JsonValueKind.Number
		&& value.TryGetInt32(out var number)
		&& number >= 0
			? number
			: null;
}
