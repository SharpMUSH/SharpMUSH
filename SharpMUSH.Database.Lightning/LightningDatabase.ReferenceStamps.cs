using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The creation times that keep a stored reference to an object from passing to whatever later holds its
/// number. A lock names each object as its objid (<c>#N:ms</c>), stamped from the object row in the same write
/// that stores the lock, so a lock naming a destroyed object fails rather than admitting the next holder of the
/// number, as PennMUSH's <c>purge_locks</c> makes it fail. Mail carries its sender's creation time, PennMUSH's
/// <c>from_ctime</c>.
/// <para>
/// <see cref="ReferenceStampMigrationId"/> stamps what was stored before: every object, channel and feed lock,
/// and every mail's sender. A sender whose object is already gone keeps no time, and reads as gone from then on;
/// a lock operand naming a number below the counter that holds nothing is stamped with a time no object has.
/// </para>
/// </summary>
public partial class LightningDatabase
{
	internal const string ReferenceStampMigrationId = "0009_reference_stamps";

	/// <summary>
	/// <paramref name="key"/> with each object it names by a bare <c>#N</c> written as that object's objid, or as
	/// the objid of the recover entry holding the number, so the lock admits it again once recovered. A
	/// number below the counter that holds nothing was an object once, so it is written with
	/// <see cref="GoneCreationTime"/> and fails for whatever holds the number next. A number the counter has not
	/// reached yet is kept as written: an import sets locks that name objects it is still to create.
	/// </summary>
	internal static string StampLockKey(ITx tx, string key)
		=> key.Contains('#')
			? LockKeyReferences.Rewrite(key, number => ReadObjectHeader(tx, number) switch
			{
				{ } header => $"#{number}:{header.CreationTime}",
				null when ReadRecover(tx, number) is { } held => $"#{number}:{held.Object.CreationTime}",
				null when number < ReadNextDbref(tx) => $"#{number}:{GoneCreationTime}",
				null => null
			})
			: key;

	/// <summary>The creation time a lock gives an object that is gone: no object is created at the epoch.</summary>
	internal const long GoneCreationTime = 0;

	/// <summary>A feed's or feed kind's locks, each stamped as <see cref="StampLockKey"/> stamps one.</summary>
	private static Dictionary<string, string> StampLockKeys(ITx tx, IEnumerable<KeyValuePair<string, string>> locks)
		=> locks.ToDictionary(pair => pair.Key, pair => StampLockKey(tx, pair.Value));

	private static void MigrateReferenceStamps(ITx tx)
	{
		var marker = Keys.Str("mig:" + ReferenceStampMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;

		foreach (var (key, value) in tx.Range(Tables.Obj, []).ToList())
		{
			var record = Codec.Deserialize<ObjectRecord>(value);
			var locks = LockNames.Fold(record.Locks);
			var changed = false;
			foreach (var (name, data) in locks.ToList())
			{
				var stamped = StampLockKey(tx, data.LockString);
				if (stamped == data.LockString) continue;
				locks[name] = data with { LockString = stamped };
				changed = true;
			}

			if (changed) tx.Put(Tables.Obj, key, Codec.Serialize(record with { Locks = locks }));
		}

		foreach (var (key, value) in tx.Range(Tables.Chan, []).ToList())
		{
			var record = Codec.Deserialize<ChannelRecord>(value);
			var stamped = record with
			{
				JoinLock = StampLockKey(tx, record.JoinLock),
				SpeakLock = StampLockKey(tx, record.SpeakLock),
				SeeLock = StampLockKey(tx, record.SeeLock),
				HideLock = StampLockKey(tx, record.HideLock),
				ModLock = StampLockKey(tx, record.ModLock)
			};
			if (stamped != record) tx.Put(Tables.Chan, key, Codec.Serialize(stamped));
		}

		foreach (var (key, value) in tx.Range(Tables.FeedKind, []).ToList())
		{
			var record = Codec.Deserialize<FeedKindRecord>(value);
			if (record.Locks.Count != 0)
				tx.Put(Tables.FeedKind, key, Codec.Serialize(record with { Locks = StampLockKeys(tx, record.Locks) }));
		}

		foreach (var (key, value) in tx.Range(Tables.Feed, []).ToList())
		{
			var record = Codec.Deserialize<FeedRecord>(value);
			if (record.Locks.Count != 0)
				tx.Put(Tables.Feed, key, Codec.Serialize(record with { Locks = StampLockKeys(tx, record.Locks) }));
		}

		foreach (var (key, value) in tx.Range(Tables.Mail, []).ToList())
		{
			var record = Codec.Deserialize<MailRecord>(value);
			if (record.SenderCreationTime is null && ReadObjectHeader(tx, record.Sender) is { } sender)
				tx.Put(Tables.Mail, key, Codec.Serialize(record with { SenderCreationTime = sender.CreationTime }));
		}

		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = ReferenceStampMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}
}
