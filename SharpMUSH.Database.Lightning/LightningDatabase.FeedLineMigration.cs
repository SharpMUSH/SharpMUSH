using System.Text.Json.Nodes;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	internal const string FeedLineMigrationId = "0011_short_feed_lines";

	/// <summary>
	/// Rewrites every feed line written with full property names and its markup as an escaped string into the short
	/// <see cref="FeedMessageRecord"/>, and each feed's stored size to the sum of its lines' new sizes.
	/// </summary>
	private static void MigrateFeedLines(ITx tx)
	{
		var marker = Keys.Str("mig:" + FeedLineMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;

		var stored = new Dictionary<(string Kind, string Key), long>();
		foreach (var (key, value) in tx.Range(Tables.FeedMessage, []).ToList())
		{
			var feed = SplitFeed(key.AsSpan(0, key.Length - 9));
			var line = value.Length > 0 && value[0] == (byte)'{' && JsonNode.Parse(value) is JsonObject old && old.ContainsKey("Id")
				? Codec.Serialize(FromLongRecord(feed.Kind, feed.Key, old))
				: value;
			tx.Put(Tables.FeedMessage, key, line);
			stored[feed] = stored.GetValueOrDefault(feed) + StoredSize(key, line);
		}

		foreach (var (key, value) in tx.Range(Tables.Feed, []).ToList())
		{
			var feed = Codec.Deserialize<FeedRecord>(value);
			tx.Put(Tables.Feed, key, Codec.Serialize(feed with { StoredBytes = stored.GetValueOrDefault((feed.Kind, feed.Key)) }));
		}

		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = FeedLineMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}

	/// <summary>
	/// A line as the long record held it, in the short one. Its markup is kept as the bytes it was stored as (only the
	/// split of the delivered line around the message is checked against them), and its size against the feed's
	/// byte limit is unchanged.
	/// </summary>
	private static FeedMessageRecord FromLongRecord(string kind, string key, JsonObject old)
	{
		string Str(string name) => old[name]?.GetValue<string>() ?? "";
		var identity = old["ExternalIdentity"] is JsonObject external
			? new ExternalFeedIdentity(external["Provider"]?.GetValue<string>() ?? "", external["Subject"]?.GetValue<string>() ?? "",
				external["Name"]?.GetValue<string>() ?? "", external["Picture"]?.GetValue<string>() ?? "")
			: null;
		var location = old["Location"]?.GetValue<string>();
		var text = Str("Text");
		var line = old["Line"]?.GetValue<string>();
		var message = new SharpFeedMessage(
			old["Id"]!.GetValue<long>(), kind, key, DateTimeOffset.FromUnixTimeMilliseconds(old["AtMs"]?.GetValue<long>() ?? 0),
			identity is null ? DBRef.Parse(Str("Speaker")) : new DBRef(-1), Str("SpeakerName"),
			DBRef.Parse(Str("Executor")), Str("ExecutorName"),
			location is null ? null : DBRef.Parse(location), Str("LocationName"), Str("Style"),
			MarkupTextSerializer.Deserialize(text), Str("DisplayName"),
			line is null ? null : MarkupTextSerializer.Deserialize(line), Str("Audience"), identity, Str("Picture"));

		var record = Fields(message) with { Text = text, Bytes = old["Bytes"]?.GetValue<long>() ?? Keys.Str(text).Length };
		return message.Line is { } delivered ? WithLine(record, message.Text, delivered, line!) : record;
	}
}
