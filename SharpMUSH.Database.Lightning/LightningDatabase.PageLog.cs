using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IPageLogStore"/> in three tables:
/// <list type="bullet">
/// <item><see cref="Tables.PageLog"/>: one row per copy, keyed dbref + 0x00 + conversation + 0x00 + id, so a
/// character's copies are one prefix range (the object-delete cascade drops them with the object's other
/// rows), and a conversation's are a range under it in the order they were sent.</item>
/// <item><see cref="Tables.PageConversation"/>: one row per character and conversation, keyed dbref + 0x00 +
/// conversation, naming who it is with and its latest page, so listing them reads no pages.</item>
/// <item><see cref="Tables.PageConversationLatest"/>: dbref + the conversation's last id + conversation →
/// the summary's key, one entry per conversation, so the listing reads a character's latest conversations
/// newest first and stops at the limit.</item>
/// <item><see cref="Tables.PageLogTime"/>: when sent (ms) + id + dbref → the copy's key, so the retention
/// purge reads only what it deletes, oldest first, and stops at the first page young enough to keep. It is
/// keyed by the time sent, not the id alone: an id can run ahead of the clock (the id source reserves a block
/// across a restart, and holds its value when the clock steps back), so an id is no measure of age.</item>
/// </list>
/// The conversation is the others' objids (<see cref="PageConversation.Key"/>); an objid holds neither a
/// space nor 0x00, so no conversation's prefix is another's.
/// </summary>
public partial class LightningDatabase
{
	/// <summary>How many copies one purge transaction deletes, so a long-unpurged log does not hold the writer.</summary>
	private const int PageLogPurgeBatch = 2000;

	public async ValueTask RecordPageAsync(SharpPage page, IReadOnlyList<DBRef> owners,
		CancellationToken cancellationToken = default)
	{
		var participants = page.Participants;
		foreach (var owner in owners)
		{
			OwnerCreation(owner);
			if (!participants.Contains(owner))
			{
				throw new ArgumentException($"{owner} is not in the page, so it cannot have a copy of it.", nameof(owners));
			}
		}

		var record = new PageLogRecord
		{
			Id = page.Id,
			Sender = page.Sender.ToString(),
			SenderName = page.SenderName,
			Recipients = page.Recipients.Select(recipient => recipient.ToString()).ToArray(),
			RecipientNames = page.RecipientNames.ToArray(),
			Style = page.Style,
			Message = page.Message,
			TimestampMs = page.Timestamp.ToUnixTimeMilliseconds()
		};

		await Store.WriteAsync(tx =>
		{
			foreach (var owner in owners.Distinct())
			{
				var creation = OwnerCreation(owner);
				var with = page.ConversationFor(owner);
				var conversation = PageConversation.Key(with);
				var key = PageLogKey(owner.Number, conversation, page.Id);

				tx.Put(Tables.PageLog, key, Codec.Serialize(record with { CharacterCreationTime = creation }));
				tx.Put(Tables.PageLogTime, PageLogTimeKey(record.TimestampMs, page.Id, owner.Number), key);

				// The summary follows the latest page; one left by an earlier holder of the dbref is replaced.
				var conversationKey = Keys.Composite(owner.Number, conversation);
				PageConversationRecord? existing = tx.TryGet(Tables.PageConversation, conversationKey, out var bytes)
					? Codec.Deserialize<PageConversationRecord>(bytes)
					: null;
				if (existing is not null && existing.CharacterCreationTime == creation && existing.LastId > page.Id)
				{
					continue;
				}

				PutConversation(tx, owner.Number, conversation, existing, new PageConversationRecord
				{
					CharacterCreationTime = creation,
					With = with.Select(other => other.ToString()).ToArray(),
					Names = with.Select(page.NameOf).ToArray(),
					LastId = page.Id,
					LastAtMs = page.Timestamp.ToUnixTimeMilliseconds()
				});
			}
		}, cancellationToken);
	}

	public ValueTask<IReadOnlyList<SharpPage>> GetPageLogAsync(DBRef character, IReadOnlyList<DBRef> with, int lines,
		CancellationToken cancellationToken = default)
	{
		var creation = OwnerCreation(character);
		var others = with.Where(other => other != character).ToArray();
		var conversation = PageConversation.Key(others.Length > 0 ? others : [character]);
		var prefix = Keys.Concat(Keys.Dbref(character.Number), Keys.Sep, Keys.Str(conversation), Keys.Sep);

		// Newest first, so only the lines asked for are read, however long the conversation has run.
		var newest = Store.Read(tx => tx.RangeReverse(Tables.PageLog, prefix)
			.Select(entry => Codec.Deserialize<PageLogRecord>(entry.Value))
			.Where(record => record.CharacterCreationTime == creation)
			.Take(lines > 0 ? lines : int.MaxValue)
			.Select(ToPage)
			.ToList());
		newest.Reverse();
		IReadOnlyList<SharpPage> pages = newest;
		return ValueTask.FromResult(pages);
	}

	public ValueTask<IReadOnlyList<SharpPageConversation>> GetPageConversationsAsync(DBRef character, int limit = 0,
		CancellationToken cancellationToken = default)
	{
		var creation = OwnerCreation(character);
		// Latest first through the latest-order index, reading only the summaries listed.
		IReadOnlyList<SharpPageConversation> conversations = Store.Read(tx => tx
			.RangeReverse(Tables.PageConversationLatest, Keys.Dbref(character.Number))
			.Select(entry => tx.TryGet(Tables.PageConversation, entry.Value, out var bytes)
				? Codec.Deserialize<PageConversationRecord>(bytes)
				: null)
			.OfType<PageConversationRecord>()
			.Where(record => record.CharacterCreationTime == creation)
			.Take(limit > 0 ? limit : int.MaxValue)
			.Select(record => new SharpPageConversation(
				record.With.Select(DBRef.Parse).ToArray(),
				record.Names,
				record.LastId,
				DateTimeOffset.FromUnixTimeMilliseconds(record.LastAtMs)))
			.ToList());
		return ValueTask.FromResult(conversations);
	}

	public async ValueTask<int> PurgePageLogAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
	{
		var cutoff = before.ToUnixTimeMilliseconds();
		var purged = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			var batch = await Store.WriteAsync(tx => PurgePageLogBatch(tx, cutoff), cancellationToken);
			purged += batch;
			if (batch < PageLogPurgeBatch) break;
		}

		return purged;
	}

	/// <summary>Deletes up to <see cref="PageLogPurgeBatch"/> copies sent before <paramref name="cutoff"/> (ms), oldest first.</summary>
	private static int PurgePageLogBatch(ITx tx, long cutoff)
	{
		var expired = tx.Range(Tables.PageLogTime, [])
			.TakeWhile(entry => Keys.ReadDbref(entry.Key) < cutoff)
			.Take(PageLogPurgeBatch)
			.ToList();

		var emptied = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (timeKey, logKey) in expired)
		{
			tx.Delete(Tables.PageLogTime, timeKey);
			tx.Delete(Tables.PageLog, logKey);
			emptied.Add(Convert.ToBase64String(ConversationPrefix(logKey)));
		}

		// A conversation with no page left in it is no longer one of the character's conversations. One with
		// pages left is summarised again from the newest of them: ids keep rising when the clock steps back,
		// so the page with the highest id can be the oldest by time, and be the one just purged.
		foreach (var prefix in emptied.Select(Convert.FromBase64String))
		{
			var conversationKey = prefix.AsSpan(0, prefix.Length - 1);
			var newest = tx.RangeReverse(Tables.PageLog, prefix).Select(entry => entry.Value).FirstOrDefault();
			if (!tx.TryGet(Tables.PageConversation, conversationKey, out var bytes)) continue;

			var summary = Codec.Deserialize<PageConversationRecord>(bytes);
			var number = Keys.ReadDbref(prefix);
			var conversation = Keys.ReadStr(conversationKey[9..]);
			if (newest is null)
			{
				tx.Delete(Tables.PageConversation, conversationKey);
				tx.Delete(Tables.PageConversationLatest, PageConversationLatestKey(number, summary.LastId, conversation));
				continue;
			}

			var page = ToPage(Codec.Deserialize<PageLogRecord>(newest));
			if (summary.LastId != page.Id)
			{
				PutConversation(tx, number, conversation, summary, summary with
				{
					Names = summary.With.Select(other => page.NameOf(DBRef.Parse(other))).ToArray(),
					LastId = page.Id,
					LastAtMs = page.Timestamp.ToUnixTimeMilliseconds()
				});
			}
		}

		return expired.Count;
	}

	/// <summary>
	/// The cascade's part for a destroyed object: its copies, the purge index entries that point at them,
	/// and its conversations.
	/// </summary>
	private static void DeletePageLog(ITx tx, long number)
	{
		var prefix = Keys.Composite(number, "");
		foreach (var (_, value) in tx.Range(Tables.PageLog, prefix).ToList())
		{
			var record = Codec.Deserialize<PageLogRecord>(value);
			tx.Delete(Tables.PageLogTime, PageLogTimeKey(record.TimestampMs, record.Id, number));
		}

		tx.DeletePrefix(Tables.PageLog, prefix);
		tx.DeletePrefix(Tables.PageConversation, prefix);
		tx.DeletePrefix(Tables.PageConversationLatest, Keys.Dbref(number));
	}

	/// <summary>
	/// Writes a conversation's summary and moves its latest-order entry from where <paramref name="previous"/>
	/// (the summary it replaces, if any) put it.
	/// </summary>
	private static void PutConversation(ITx tx, long number, string conversation, PageConversationRecord? previous,
		PageConversationRecord summary)
	{
		var conversationKey = Keys.Composite(number, conversation);
		if (previous is not null)
		{
			tx.Delete(Tables.PageConversationLatest, PageConversationLatestKey(number, previous.LastId, conversation));
		}

		tx.Put(Tables.PageConversation, conversationKey, Codec.Serialize(summary));
		tx.Put(Tables.PageConversationLatest, PageConversationLatestKey(number, summary.LastId, conversation), conversationKey);
	}

	private static byte[] PageConversationLatestKey(long number, long lastId, string conversation) =>
		Keys.Concat(Keys.Dbref(number), Keys.Dbref(lastId), Keys.Str(conversation));

	private static byte[] PageLogKey(long number, string conversation, long id) =>
		Keys.Concat(Keys.Dbref(number), Keys.Sep, Keys.Str(conversation), Keys.Sep, Keys.Dbref(id));

	private static byte[] PageLogTimeKey(long sentMs, long id, long number) =>
		Keys.Concat(Keys.Dbref(sentMs), Keys.Dbref(id), Keys.Dbref(number));

	/// <summary>A copy's key without its id: dbref + 0x00 + conversation + 0x00.</summary>
	private static byte[] ConversationPrefix(byte[] logKey) => logKey[..^8];

	private static long OwnerCreation(DBRef character) =>
		character.CreationMilliseconds
		?? throw new ArgumentException($"A page log belongs to a character by objid; {character} has no creation time.",
			nameof(character));

	private static SharpPage ToPage(PageLogRecord record) => new(
		record.Id,
		DBRef.Parse(record.Sender),
		record.SenderName,
		record.Recipients.Select(DBRef.Parse).ToArray(),
		record.RecipientNames,
		record.Style,
		record.Message,
		DateTimeOffset.FromUnixTimeMilliseconds(record.TimestampMs));
}
