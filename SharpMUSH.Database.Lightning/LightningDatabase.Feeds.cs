using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IFeedStore"/> in seven tables:
/// <list type="bullet">
/// <item><see cref="Tables.FeedKind"/>: one row per kind, by name.</item>
/// <item><see cref="Tables.Feed"/>: one row per feed, kind + 0x00 + key, with its settings and its line count,
/// size and newest id, so trimming never counts lines.</item>
/// <item><see cref="Tables.FeedMessage"/>: kind + 0x00 + key + 0x00 + id, so a feed's lines are one range in
/// the order they were sent, and the oldest is its first entry.</item>
/// <item><see cref="Tables.FeedMessageId"/>: id → the line's key.</item>
/// <item><see cref="Tables.FeedMember"/> and its reverse <see cref="Tables.FeedMemberOf"/> (dbref first), so a
/// member's feeds are one range and the object-delete cascade drops them.</item>
/// <item><see cref="Tables.FeedTap"/>: kind + 0x00 + objid + 0x00 + attribute.</item>
/// </list>
/// A kind name and a key hold no 0x00, so no feed's prefix is another's.
/// </summary>
public partial class LightningDatabase
{
	public ValueTask<IReadOnlyList<SharpFeedKind>> GetFeedKindsAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<SharpFeedKind> kinds = Store.Read(tx => tx.Range(Tables.FeedKind, [])
			.Select(entry => ToKind(Codec.Deserialize<FeedKindRecord>(entry.Value)))
			.ToList());
		return ValueTask.FromResult(kinds);
	}

	public ValueTask<Found<SharpFeedKind>> GetFeedKindAsync(string kind, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<SharpFeedKind>>(tx => tx.TryGet(Tables.FeedKind, Keys.Str(kind), out var bytes)
			? ToKind(Codec.Deserialize<FeedKindRecord>(bytes))
			: new NotFound()));

	public async ValueTask SetFeedKindAsync(SharpFeedKind kind, CancellationToken cancellationToken = default)
	{
		var record = new FeedKindRecord
		{
			Name = kind.Name,
			Owner = kind.Owner.ToString(),
			Description = kind.Description,
			Settings = ToRecord(kind.Settings),
			Locks = new Dictionary<string, string>(kind.Locks)
		};
		await Store.WriteAsync(tx => tx.Put(Tables.FeedKind, Keys.Str(kind.Name), Codec.Serialize(record)), cancellationToken);
	}

	public async ValueTask<bool> DeleteFeedKindAsync(string kind, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (!tx.Delete(Tables.FeedKind, Keys.Str(kind))) return false;

			foreach (var (_, value) in tx.Range(Tables.Feed, Keys.Concat(Keys.Str(kind), Keys.Sep)).ToList())
			{
				var feed = Codec.Deserialize<FeedRecord>(value);
				DeleteFeedRows(tx, feed.Kind, feed.Key);
			}

			tx.DeletePrefix(Tables.FeedTap, Keys.Concat(Keys.Str(kind), Keys.Sep));
			return true;
		}, cancellationToken);

	public ValueTask<IReadOnlyList<SharpFeed>> GetFeedsAsync(string kind, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<SharpFeed> feeds = Store.Read(tx => tx.Range(Tables.Feed, Keys.Concat(Keys.Str(kind), Keys.Sep))
			.Select(entry => ToFeed(Codec.Deserialize<FeedRecord>(entry.Value)))
			.ToList());
		return ValueTask.FromResult(feeds);
	}

	public ValueTask<IReadOnlyList<SharpFeedUsage>> GetFeedUsageAsync(CancellationToken cancellationToken = default)
	{
		// One pass over the feed rows, which carry their own counts; no line is read.
		IReadOnlyList<SharpFeedUsage> usage = Store.Read(tx => tx.Range(Tables.Feed, [])
			.Select(entry => Codec.Deserialize<FeedRecord>(entry.Value))
			.GroupBy(feed => feed.Kind, StringComparer.Ordinal)
			.Select(kind => new SharpFeedUsage(kind.Key, kind.Count(), kind.Sum(feed => (long)feed.Messages),
				kind.Sum(feed => feed.Bytes), kind.Sum(feed => feed.StoredBytes)))
			.ToList());
		return ValueTask.FromResult(usage);
	}

	public ValueTask<Found<SharpFeed>> GetFeedAsync(string kind, string key, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<SharpFeed>>(tx => tx.TryGet(Tables.Feed, FeedKey(kind, key), out var bytes)
			? ToFeed(Codec.Deserialize<FeedRecord>(bytes))
			: new NotFound()));

	public async ValueTask SetFeedAsync(SharpFeed feed, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			var current = ReadFeed(tx, feed.Kind, feed.Key);
			tx.Put(Tables.Feed, FeedKey(feed.Kind, feed.Key), Codec.Serialize(current with
			{
				Settings = ToRecord(feed.Settings),
				Locks = new Dictionary<string, string>(feed.Locks)
			}));
		}, cancellationToken);

	public async ValueTask<bool> DeleteFeedAsync(string kind, string key, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Feed, FeedKey(kind, key), out _)) return false;
			DeleteFeedRows(tx, kind, key);
			return true;
		}, cancellationToken);

	public async ValueTask<bool> RenameFeedAsync(string kind, string from, string to,
		CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (string.Equals(from, to, StringComparison.Ordinal)
				|| !tx.TryGet(Tables.Feed, FeedKey(kind, from), out var bytes))
			{
				return false;
			}

			var source = Codec.Deserialize<FeedRecord>(bytes);
			var target = tx.TryGet(Tables.Feed, FeedKey(kind, to), out var targetBytes)
				? Codec.Deserialize<FeedRecord>(targetBytes)
				: source with { Key = to, Messages = 0, Bytes = 0, StoredBytes = 0, LastId = 0 };

			// A line keeps its id, so the id index only points at its new key, and lines already under the new
			// key stay in id order with the moved ones.
			foreach (var (oldKey, value) in tx.Range(Tables.FeedMessage, FeedPrefix(kind, from)).ToList())
			{
				var id = Codec.Deserialize<FeedMessageRecord>(value).Id;
				var newKey = MessageKey(kind, to, id);
				tx.Delete(Tables.FeedMessage, oldKey);
				tx.Put(Tables.FeedMessage, newKey, value);
				tx.Put(Tables.FeedMessageId, Keys.Dbref(id), newKey);
			}

			// A member of both keeps the membership they had under the new key.
			foreach (var (memberKey, value) in tx.Range(Tables.FeedMember, FeedPrefix(kind, from)).ToList())
			{
				var number = Keys.ReadDbref(memberKey.AsSpan(memberKey.Length - 8));
				tx.Delete(Tables.FeedMember, memberKey);
				tx.Delete(Tables.FeedMemberOf, MemberOfKey(number, kind, from));
				if (tx.TryGet(Tables.FeedMember, MemberKey(kind, to, number), out _)) continue;
				tx.Put(Tables.FeedMember, MemberKey(kind, to, number), value);
				tx.Put(Tables.FeedMemberOf, MemberOfKey(number, kind, to), []);
			}

			tx.Delete(Tables.Feed, FeedKey(kind, from));
			tx.Put(Tables.Feed, FeedKey(kind, to), Codec.Serialize(target with
			{
				Messages = target.Messages + source.Messages,
				Bytes = target.Bytes + source.Bytes,
				StoredBytes = target.StoredBytes + source.StoredBytes,
				LastId = Math.Max(target.LastId, source.LastId)
			}));
			return true;
		}, cancellationToken);

	public ValueTask<IReadOnlyList<SharpFeedMember>> GetFeedMembersAsync(string kind, string key,
		CancellationToken cancellationToken = default)
	{
		IReadOnlyList<SharpFeedMember> members = Store.Read(tx => tx.Range(Tables.FeedMember, FeedPrefix(kind, key))
			.Select(entry => ToMember(Keys.ReadDbref(entry.Key.AsSpan(entry.Key.Length - 8)),
				Codec.Deserialize<FeedMemberRecord>(entry.Value)))
			.ToList());
		return ValueTask.FromResult(members);
	}

	public ValueTask<IReadOnlyList<(string Kind, string Key)>> GetMemberFeedsAsync(DBRef member, string? kind,
		CancellationToken cancellationToken = default)
	{
		var creation = MemberCreation(member);
		var prefix = kind is null
			? Keys.Dbref(member.Number)
			: Keys.Concat(Keys.Dbref(member.Number), Keys.Str(kind), Keys.Sep);
		IReadOnlyList<(string, string)> feeds = Store.Read(tx => tx.Range(Tables.FeedMemberOf, prefix)
			.Select(entry => SplitFeed(entry.Key.AsSpan(8)))
			.Where(feed => tx.TryGet(Tables.FeedMember, MemberKey(feed.Kind, feed.Key, member.Number), out var bytes)
				&& Codec.Deserialize<FeedMemberRecord>(bytes).CreationTime == creation)
			.ToList());
		return ValueTask.FromResult(feeds);
	}

	public async ValueTask SetFeedMemberAsync(string kind, string key, SharpFeedMember member,
		CancellationToken cancellationToken = default)
	{
		var creation = MemberCreation(member.Member);
		await Store.WriteAsync(tx =>
		{
			EnsureFeed(tx, kind, key);
			tx.Put(Tables.FeedMember, MemberKey(kind, key, member.Member.Number), Codec.Serialize(new FeedMemberRecord
			{
				CreationTime = creation,
				JoinedAt = member.JoinedAt,
				Gag = member.Gag,
				LastSeen = member.LastSeen
			}));
			tx.Put(Tables.FeedMemberOf, MemberOfKey(member.Member.Number, kind, key), []);
		}, cancellationToken);
	}

	public async ValueTask<bool> RemoveFeedMemberAsync(string kind, string key, DBRef member,
		CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.FeedMemberOf, MemberOfKey(member.Number, kind, key));
			return tx.Delete(Tables.FeedMember, MemberKey(kind, key, member.Number));
		}, cancellationToken);

	public async ValueTask AppendFeedMessageAsync(SharpFeedMessage message, FeedSettings limits,
		CancellationToken cancellationToken = default)
	{
		var text = MarkupTextSerializer.Serialize(message.Text);
		var record = new FeedMessageRecord
		{
			Id = message.Id,
			AtMs = message.At.ToUnixTimeMilliseconds(),
			Speaker = message.Speaker.ToString(),
			SpeakerName = message.SpeakerName,
			Executor = message.Executor.ToString(),
			ExecutorName = message.ExecutorName,
			Location = message.Location?.ToString(),
			LocationName = message.LocationName,
			Style = message.Style,
			Text = text,
			DisplayName = message.DisplayName,
			Line = message.Line is { } line ? MarkupTextSerializer.Serialize(line) : null,
			Audience = message.Audience,
			Bytes = Keys.Str(text).Length
		};

		var key = MessageKey(message.Kind, message.Key, message.Id);
		var idKey = Keys.Dbref(message.Id);
		record = record with { StoredBytes = Codec.Serialize(record).Length + key.Length + idKey.Length + key.Length };

		await Store.WriteAsync(tx =>
		{
			var feed = ReadFeed(tx, message.Kind, message.Key);
			tx.Put(Tables.FeedMessage, key, Codec.Serialize(record));
			tx.Put(Tables.FeedMessageId, idKey, key);
			feed = feed with
			{
				Messages = feed.Messages + 1,
				Bytes = feed.Bytes + record.Bytes,
				StoredBytes = feed.StoredBytes + record.StoredBytes,
				LastId = Math.Max(feed.LastId, message.Id)
			};

			// Oldest first, until the feed is inside every limit. The line just written is never dropped for
			// age, and only for size when it alone is over the limit, which leaves the feed empty.
			var maxMessages = limits.MaxMessages ?? 0;
			var maxBytes = limits.MaxBytes ?? 0;
			var cutoff = limits.MaxAge is { } age && age > TimeSpan.Zero ? record.AtMs - (long)age.TotalMilliseconds : long.MinValue;
			foreach (var (oldKey, oldValue) in tx.Range(Tables.FeedMessage, FeedPrefix(message.Kind, message.Key)).ToList())
			{
				var old = Codec.Deserialize<FeedMessageRecord>(oldValue);
				var over = (maxMessages > 0 && feed.Messages > maxMessages)
					|| (maxBytes > 0 && feed.Bytes > maxBytes)
					|| (old.Id != record.Id && old.AtMs < cutoff);
				if (!over) break;

				tx.Delete(Tables.FeedMessage, oldKey);
				tx.Delete(Tables.FeedMessageId, Keys.Dbref(old.Id));
				feed = Without(feed, old);
			}

			tx.Put(Tables.Feed, FeedKey(message.Kind, message.Key), Codec.Serialize(feed));
		}, cancellationToken);
	}

	public ValueTask<IReadOnlyList<SharpFeedMessage>> GetFeedMessagesAsync(string kind, string key, int count,
		long afterId, CancellationToken cancellationToken = default)
	{
		// Newest first, so only the lines asked for are read, however long the feed has run.
		var newest = Store.Read(tx => tx.RangeReverse(Tables.FeedMessage, FeedPrefix(kind, key))
			.Select(entry => Codec.Deserialize<FeedMessageRecord>(entry.Value))
			.TakeWhile(record => record.Id > afterId)
			.Take(count > 0 ? count : int.MaxValue)
			.Select(record => ToMessage(kind, key, record))
			.ToList());
		newest.Reverse();
		IReadOnlyList<SharpFeedMessage> messages = newest;
		return ValueTask.FromResult(messages);
	}

	public ValueTask<Found<SharpFeedMessage>> GetFeedMessageAsync(long id, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<SharpFeedMessage>>(tx =>
		{
			if (!tx.TryGet(Tables.FeedMessageId, Keys.Dbref(id), out var key)
				|| !tx.TryGet(Tables.FeedMessage, key, out var bytes))
			{
				return new NotFound();
			}

			var (kind, feedKey) = SplitFeed(key.AsSpan(0, key.Length - 9));
			return ToMessage(kind, feedKey, Codec.Deserialize<FeedMessageRecord>(bytes));
		}));

	public async ValueTask<int> PurgeFeedAsync(string kind, string key, DateTimeOffset? before,
		CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Feed, FeedKey(kind, key), out var bytes)) return 0;

			var feed = Codec.Deserialize<FeedRecord>(bytes);
			var cutoff = before?.ToUnixTimeMilliseconds() ?? long.MaxValue;
			var purged = 0;
			foreach (var (oldKey, oldValue) in tx.Range(Tables.FeedMessage, FeedPrefix(kind, key)).ToList())
			{
				var old = Codec.Deserialize<FeedMessageRecord>(oldValue);
				if (old.AtMs >= cutoff) continue;

				tx.Delete(Tables.FeedMessage, oldKey);
				tx.Delete(Tables.FeedMessageId, Keys.Dbref(old.Id));
				feed = Without(feed, old);
				purged++;
			}

			tx.Put(Tables.Feed, FeedKey(kind, key), Codec.Serialize(feed));
			return purged;
		}, cancellationToken);

	public ValueTask<IReadOnlyList<SharpFeedTap>> GetFeedTapsAsync(string? kind, CancellationToken cancellationToken = default)
	{
		var prefix = kind is null ? [] : Keys.Concat(Keys.Str(kind), Keys.Sep);
		IReadOnlyList<SharpFeedTap> taps = Store.Read(tx => tx.Range(Tables.FeedTap, prefix)
			.Select(entry => ToTap(entry.Key))
			.ToList());
		return ValueTask.FromResult(taps);
	}

	public async ValueTask AddFeedTapAsync(SharpFeedTap tap, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx => tx.Put(Tables.FeedTap, TapKey(tap), []), cancellationToken);

	public async ValueTask<bool> RemoveFeedTapAsync(SharpFeedTap tap, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.FeedTap, TapKey(tap)), cancellationToken);

	/// <summary>The cascade's part for a destroyed object: its memberships and the taps on its attributes.</summary>
	private static void DeleteFeedMemberships(ITx tx, long number)
	{
		foreach (var (key, _) in tx.Range(Tables.FeedMemberOf, Keys.Dbref(number)).ToList())
		{
			var (kind, feedKey) = SplitFeed(key.AsSpan(8));
			tx.Delete(Tables.FeedMember, MemberKey(kind, feedKey, number));
		}

		tx.DeletePrefix(Tables.FeedMemberOf, Keys.Dbref(number));

		var dbref = $"#{number}:";
		var taps = tx.Range(Tables.FeedTap, [])
			.Where(entry => ToTap(entry.Key).Object.ToString().StartsWith(dbref, StringComparison.Ordinal))
			.ToList();
		foreach (var (key, _) in taps)
		{
			tx.Delete(Tables.FeedTap, key);
		}
	}

	/// <summary>A feed's lines, members and row.</summary>
	private static void DeleteFeedRows(ITx tx, string kind, string key)
	{
		foreach (var (_, value) in tx.Range(Tables.FeedMessage, FeedPrefix(kind, key)).ToList())
		{
			tx.Delete(Tables.FeedMessageId, Keys.Dbref(Codec.Deserialize<FeedMessageRecord>(value).Id));
		}

		tx.DeletePrefix(Tables.FeedMessage, FeedPrefix(kind, key));
		foreach (var (memberKey, _) in tx.Range(Tables.FeedMember, FeedPrefix(kind, key)).ToList())
		{
			tx.Delete(Tables.FeedMemberOf, MemberOfKey(Keys.ReadDbref(memberKey.AsSpan(memberKey.Length - 8)), kind, key));
		}

		tx.DeletePrefix(Tables.FeedMember, FeedPrefix(kind, key));
		tx.Delete(Tables.Feed, FeedKey(kind, key));
	}

	/// <summary>A feed's counts with <paramref name="line"/> gone.</summary>
	private static FeedRecord Without(FeedRecord feed, FeedMessageRecord line) => feed with
	{
		Messages = feed.Messages - 1,
		Bytes = feed.Bytes - line.Bytes,
		StoredBytes = feed.StoredBytes - line.StoredBytes
	};

	private static FeedRecord ReadFeed(ITx tx, string kind, string key)
		=> tx.TryGet(Tables.Feed, FeedKey(kind, key), out var bytes)
			? Codec.Deserialize<FeedRecord>(bytes)
			: new FeedRecord { Kind = kind, Key = key };

	private static void EnsureFeed(ITx tx, string kind, string key)
	{
		if (!tx.TryGet(Tables.Feed, FeedKey(kind, key), out _))
		{
			tx.Put(Tables.Feed, FeedKey(kind, key), Codec.Serialize(new FeedRecord { Kind = kind, Key = key }));
		}
	}

	private static byte[] FeedKey(string kind, string key) => Keys.Concat(Keys.Str(kind), Keys.Sep, Keys.Str(key));

	/// <summary>What every line and member of a feed is keyed under: kind + 0x00 + key + 0x00.</summary>
	private static byte[] FeedPrefix(string kind, string key) => Keys.Concat(FeedKey(kind, key), Keys.Sep);

	private static byte[] MessageKey(string kind, string key, long id) => Keys.Concat(FeedPrefix(kind, key), Keys.Dbref(id));

	private static byte[] MemberKey(string kind, string key, long number) => Keys.Concat(FeedPrefix(kind, key), Keys.Dbref(number));

	private static byte[] MemberOfKey(long number, string kind, string key) => Keys.Concat(Keys.Dbref(number), FeedKey(kind, key));

	private static byte[] TapKey(SharpFeedTap tap)
		=> Keys.Concat(Keys.Str(tap.Kind), Keys.Sep, Keys.Str(tap.Object.ToString()), Keys.Sep, Keys.Str(tap.Attribute));

	/// <summary>kind + 0x00 + key, split.</summary>
	private static (string Kind, string Key) SplitFeed(ReadOnlySpan<byte> bytes)
	{
		var sep = bytes.IndexOf((byte)0);
		return (Keys.ReadStr(bytes[..sep]), Keys.ReadStr(bytes[(sep + 1)..]));
	}

	private static SharpFeedTap ToTap(byte[] key)
	{
		var parts = Keys.ReadStr(key).Split('\0');
		return new SharpFeedTap(parts[0], DBRef.Parse(parts[1]), parts[2]);
	}

	private static long MemberCreation(DBRef member) =>
		member.CreationMilliseconds
		?? throw new ArgumentException($"A feed member is named by objid; {member} has no creation time.", nameof(member));

	private static SharpFeedMember ToMember(long number, FeedMemberRecord record)
		=> new(new DBRef((int)number, record.CreationTime), record.JoinedAt, record.Gag, record.LastSeen);

	private static SharpFeedKind ToKind(FeedKindRecord record)
		=> new(record.Name, DBRef.Parse(record.Owner), record.Description, ToSettings(record.Settings), record.Locks);

	private static SharpFeed ToFeed(FeedRecord record)
		=> new(record.Kind, record.Key, ToSettings(record.Settings), record.Locks, record.Messages, record.Bytes,
			record.LastId, record.StoredBytes);

	private static SharpFeedMessage ToMessage(string kind, string key, FeedMessageRecord record)
		=> new(record.Id, kind, key, DateTimeOffset.FromUnixTimeMilliseconds(record.AtMs), DBRef.Parse(record.Speaker),
			record.SpeakerName, DBRef.Parse(record.Executor), record.ExecutorName,
			record.Location is null ? null : DBRef.Parse(record.Location), record.LocationName, record.Style, MarkupTextSerializer.Deserialize(record.Text), record.DisplayName,
			record.Line is null ? null : MarkupTextSerializer.Deserialize(record.Line), record.Audience);

	private static FeedSettings ToSettings(FeedSettingsRecord record)
		=> new(record.MaxMessages, record.MaxBytes, record.MaxLength,
			record.MaxAgeMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null, record.Logged, record.Style);

	private static FeedSettingsRecord ToRecord(FeedSettings settings) => new()
	{
		MaxMessages = settings.MaxMessages,
		MaxBytes = settings.MaxBytes,
		MaxLength = settings.MaxLength,
		MaxAgeMs = settings.MaxAge is { } age ? (long)age.TotalMilliseconds : null,
		Logged = settings.Logged,
		Style = settings.Style
	};
}
