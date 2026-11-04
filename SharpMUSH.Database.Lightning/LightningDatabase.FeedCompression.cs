using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Feed lines as stored: a line's record as JSON, compressed with a feed line dictionary (see
/// <see cref="FeedLines"/>). Its dictionary ships as <c>Resources/feed-lines-0.zdict</c>.
/// </summary>
public partial class LightningDatabase
{
	/// <summary>A line as stored, compressed with the current feed line dictionary.</summary>
	private byte[] EncodeLine(ITx tx, FeedMessageRecord record) => EncodeValue(tx, FeedLines, Codec.Serialize(record));

	/// <summary>
	/// A stored line, compressed or not. A line that cannot be read (its dictionary gone, or a form this server does not
	/// know) reads as a line saying so, under its id from <paramref name="key"/>, rather than failing the read.
	/// </summary>
	internal FeedMessageRecord DecodeLine(ITx tx, ReadOnlySpan<byte> key, byte[] value)
		=> DecodeValue(tx, FeedLines, value) is { Length: > 0 } json && json[0] == (byte)'{'
			? Codec.Deserialize<FeedMessageRecord>(json)
			: UnreadableLine(key);

	private static FeedMessageRecord UnreadableLine(ReadOnlySpan<byte> key) => new()
	{
		Id = Keys.ReadDbref(key[^8..]),
		Text = MarkupTextSerializer.Serialize(MarkupText.Plain("This line can't be read."))
	};

	/// <summary>A line's JSON as the store writes it before compressing, for <c>tools/BundledDictionaries</c> to train on.</summary>
	internal static byte[] FeedLineJson(SharpFeedMessage message) => Codec.Serialize(ToRecord(message));

	/// <summary>Keeps each feed's stored size right after a rewrite pass changed its lines' sizes.</summary>
	private static void AdjustFeedStoredBytes(ITx tx, List<(byte[] Key, long Delta)> resized)
	{
		var changed = new Dictionary<(string Kind, string Key), long>();
		foreach (var (key, delta) in resized)
		{
			var feed = SplitFeed(key.AsSpan(0, key.Length - 9));
			changed[feed] = changed.GetValueOrDefault(feed) + delta;
		}

		foreach (var ((kind, key), delta) in changed)
		{
			var feed = ReadFeed(tx, kind, key);
			tx.Put(Tables.Feed, FeedKey(kind, key), Codec.Serialize(feed with { StoredBytes = feed.StoredBytes + delta }));
		}
	}
}
