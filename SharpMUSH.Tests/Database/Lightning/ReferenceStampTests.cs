using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Stored references to objects carry the creation time of the object they named, so a destroyed object's
/// number taken by a new object does not inherit them: locks hold objids, mail holds its sender's creation
/// time, and a freed object's activity goes with it.
/// </summary>
public class ReferenceStampTests : LightningDatabaseFixture
{
	private async Task<SharpPlayer> God() => (await Db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<AnySharpObject> Node(DBRef dbref) => (await Db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();

	private async Task<AnySharpContainer> Room() => (await Node(new DBRef(2))).AsOptionalContainer.Expect<AnySharpContainer>();

	private async Task<SharpObject> Thing(string name, int? number = null, long? created = null)
	{
		var room = await Room();
		return (await Node(await Db.CreateThingAsync(name, room, await God(), room, created, requestedDbref: number))).Object();
	}

	private string StoredLock(SharpObject target, string name) => Db.Store.Read(tx =>
		Codec.Deserialize<ObjectRecord>(tx.TryGet(Tables.Obj, Keys.Dbref(target.DBRef.Number), out var bytes)
			? bytes : throw new InvalidOperationException("no such object")).Locks[name].LockString);

	[Test]
	public async Task ALockIsStoredWithTheObjidOfEachObjectItNames()
	{
		var target = await Thing("StampLocked");
		var key = await Thing("StampKey");

		await Db.SetLockAsync(target, "Basic", new SharpLockData { LockString = $"=#{key.DBRef.Number} | +#{key.DBRef.Number} | FOO:#{key.DBRef.Number} | #999999" });

		await Assert.That(StoredLock(target, "Basic"))
			.IsEqualTo($"={key.DBRef} | +{key.DBRef} | FOO:#{key.DBRef.Number} | #999999")
			.Because("only object operands are stamped; an attribute value and a number nothing holds keep their text");
	}

	[Test]
	public async Task ALockNamingAFreedNumberCannotPassToItsNextHolder()
	{
		var target = await Thing("StampHoleLocked");
		var gone = await Thing("StampHoleGone");
		await Db.DeleteObjectAsync(gone.DBRef);

		await Db.SetLockAsync(target, "Basic", new SharpLockData { LockString = $"=#{gone.DBRef.Number}" });

		await Assert.That(StoredLock(target, "Basic")).IsEqualTo($"=#{gone.DBRef.Number}:{LightningDatabase.GoneCreationTime}");

		var successor = await Thing("StampHoleSuccessor", gone.DBRef.Number, LightningDatabase.GoneCreationTime);
		await Assert.That(successor.CreationTime).IsNotEqualTo(LightningDatabase.GoneCreationTime)
			.Because("no object is created at the time a gone lock operand carries");
	}

	[Test]
	public async Task ALockNamingARecoverEntryNamesTheObjectItHolds()
	{
		var target = await Thing("StampHeldLocked");
		var held = await Thing("StampHeld");
		await Db.RetireObjectAsync(held.DBRef, null);

		await Db.SetLockAsync(target, "Basic", new SharpLockData { LockString = $"=#{held.DBRef.Number}" });

		await Assert.That(StoredLock(target, "Basic")).IsEqualTo($"={held.DBRef}");
	}

	[Test]
	public async Task AStampedLockIsWrittenAgainUnchanged()
	{
		var target = await Thing("StampTwice");
		var key = await Thing("StampTwiceKey");

		await Db.SetLockAsync(target, "Basic", new SharpLockData { LockString = $"@{key.DBRef}/Use" });

		await Assert.That(StoredLock(target, "Basic")).IsEqualTo($"@{key.DBRef}/Use");
	}

	[Test]
	public async Task TheMigrationStampsLocksAndMailWrittenBeforeIt()
	{
		var target = await Thing("MigratedLock");
		var key = await Thing("MigratedKey");
		var recipient = (await Node(await Db.CreatePlayerAsync("MigratedRecipient", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		await Db.SendMailAsync(key, recipient, Mail("Before stamps"));

		await Db.Store.WriteAsync(tx =>
		{
			var record = Codec.Deserialize<ObjectRecord>(tx.TryGet(Tables.Obj, Keys.Dbref(target.DBRef.Number), out var bytes) ? bytes : []);
			tx.Put(Tables.Obj, Keys.Dbref(target.DBRef.Number), Codec.Serialize(record with
			{
				Locks = new Dictionary<string, LockRecord> { ["Basic"] = new() { LockString = $"=#{key.DBRef.Number}" } }
			}));
			foreach (var (mailKey, value) in tx.Range(Tables.Mail, []).ToList())
			{
				tx.Put(Tables.Mail, mailKey, Codec.Serialize(Codec.Deserialize<MailRecord>(value) with { SenderCreationTime = null }));
			}

			tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.ReferenceStampMigrationId));
		});

		await ReopenAsync();

		await Assert.That(StoredLock(target, "Basic")).IsEqualTo($"={key.DBRef}");
		var mail = await Db.GetIncomingMailsAsync(recipient, "INBOX").SingleAsync();
		var from = (await mail.From.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(from.Object().DBRef).IsEqualTo(key.DBRef);
	}

	[Test]
	public async Task MailFromAFreedSenderHasNoSenderOnceItsNumberIsTaken()
	{
		var recipient = (await Node(await Db.CreatePlayerAsync("ReusedRecipient", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		var sender = await Thing("ReusedSender");
		await Db.SendMailAsync(sender, recipient, Mail("From the first holder"));

		await Db.DeleteObjectAsync(sender.DBRef);
		await Thing("ReusedSenderSuccessor", sender.DBRef.Number, sender.CreationTime + 1000);

		var mail = await Db.GetIncomingMailsAsync(recipient, "INBOX").SingleAsync();
		await Assert.That(await mail.From.WithCancellation(CancellationToken.None) is None).IsTrue();
		var summary = await Db.GetIncomingMailSummariesAsync(recipient, "INBOX").SingleAsync();
		await Assert.That(summary.SenderName).IsNull();
	}

	[Test]
	public async Task AFreedObjectsActivityGoesWithIt()
	{
		var day = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
		var player = await Thing("ActivePoser");
		var number = player.DBRef.Number;
		await Db.MergeActivityAsync(new ActivityBatch([new ActivityPlayerDay(day, number, 3, 1)],
			[new ActivityRoomDay(day, number, 3, 1, [number])], new Dictionary<long, long> { [number] = 100 }));

		await Db.DeleteObjectAsync(player.DBRef);

		var window = await Db.GetActivityAsync(day, day);
		await Assert.That(window.Players.Any(p => p.Player == number)).IsFalse();
		await Assert.That(window.Rooms.Any(r => r.Room == number)).IsFalse();
		await Assert.That(window.LastPoses.ContainsKey(number)).IsFalse();
	}

	[Test]
	public async Task AFreedRoomsSceneCountGoesWithItEvenWithoutAPose()
	{
		var day = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
		var room = await Thing("SceneOnlyRoom");
		var number = room.DBRef.Number;
		await Db.MergeActivityAsync(new ActivityBatch([], [new ActivityRoomDay(day, number, 0, 1, [])], new Dictionary<long, long>()));

		await Db.DeleteObjectAsync(room.DBRef);

		await Assert.That((await Db.GetActivityAsync(day, day)).Rooms.Any(r => r.Room == number)).IsFalse();
	}

	private static SharpMail Mail(string subject) => new()
	{
		DateSent = DateTimeOffset.UtcNow,
		Fresh = true,
		Read = false,
		Tagged = false,
		Urgent = false,
		Cleared = false,
		Forwarded = false,
		Folder = "INBOX",
		Content = MarkupText.Plain(subject),
		Subject = MarkupText.Plain(subject),
		From = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(_ => throw new InvalidOperationException("unused on send"))
	};
}
