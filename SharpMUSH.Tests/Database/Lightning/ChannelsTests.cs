using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// <see cref="IChannelStore"/> against the Lightning provider directly — no NATS, no
/// <c>ServerWebAppFactory</c>. Complements <c>ChannelUniquenessTests</c> (which runs through the mediator
/// across every provider) by asserting the LMDB encoding itself: both index directions
/// (<see cref="Tables.ChanMember"/>/<see cref="Tables.RevChanMember"/>) after join/leave, a status update
/// round trip, and that delete removes every member row and reverse-index entry it created.
/// </summary>
public class ChannelsTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private string _path = null!;
	private LightningDatabase _db = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public void Cleanup()
	{
		_db.Store.Dispose();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
				// join by a few milliseconds under load. Leaving the temp directory behind costs
				// disk, not correctness — matches MigrationTests' own cleanup.
			}
		}
	}

	private async Task<SharpPlayer> God() => (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;

	private async Task<SharpPlayer> NewPlayer(string name)
		=> (await _db.GetObjectNodeAsync(await _db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 0))).Known.AsPlayer;

	[Test]
	public async Task CreateChannelAsyncStoresTheRecordAndSecondCreateOfTheSameNameIsRefused()
	{
		var god = await God();

		var first = await _db.CreateChannelAsync(MModule.single("Newbie"), ["Player"], god);
		var second = await _db.CreateChannelAsync(MModule.single("Newbie"), ["Player"], god);

		await Assert.That(first.IsSuccess).IsTrue();
		await Assert.That(second.IsNameTaken).IsTrue();

		var channel = await _db.GetChannelAsync("Newbie");
		await Assert.That(channel).IsNotNull();
		await Assert.That(channel!.Name.ToPlainText()).IsEqualTo("Newbie");
		await Assert.That(channel.Privs).Contains("Player");
		await Assert.That((await channel.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number).IsEqualTo(god.Object.DBRef.Number);

		// Only one row on disk despite the refused second create.
		var count = _db.Store.Read(tx => tx.Range(Tables.Chan, []).Count());
		await Assert.That(count).IsEqualTo(1);
	}

	[Test]
	public async Task JoinAndLeaveWriteAndRemoveBothIndexDirections()
	{
		var god = await God();
		var alice = await NewPlayer("Alice");
		await _db.CreateChannelAsync(MModule.single("JoinLeave"), ["Player"], god);
		var channel = (await _db.GetChannelAsync("JoinLeave"))!;

		await _db.AddUserToChannelAsync(channel, alice);

		var aliceKey = Keys.Dbref(alice.Object.DBRef.Number);
		var chanKey = Keys.Upper("JoinLeave");
		var memberKey = Keys.Composite("JOINLEAVE", (long)alice.Object.DBRef.Number);

		await Assert.That(_db.Store.Read(tx => tx.TryGet(Tables.ChanMember, memberKey, out _))).IsTrue();
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.RevChanMember, aliceKey).Any(v => v.AsSpan().SequenceEqual(chanKey)))).IsTrue();

		var members = await channel.Members.Value.Select(m => m.Member.Object().DBRef.Number).ToListAsync();
		await Assert.That(members).Contains(alice.Object.DBRef.Number);

		await _db.RemoveUserFromChannelAsync(channel, alice);

		await Assert.That(_db.Store.Read(tx => tx.TryGet(Tables.ChanMember, memberKey, out _))).IsFalse();
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.RevChanMember, aliceKey).Any(v => v.AsSpan().SequenceEqual(chanKey)))).IsFalse();
	}

	[Test]
	public async Task UpdateChannelUserStatusAsyncRoundTripsEveryField()
	{
		var god = await God();
		var alice = await NewPlayer("Bob");
		await _db.CreateChannelAsync(MModule.single("StatusChan"), ["Player"], god);
		var channel = (await _db.GetChannelAsync("StatusChan"))!;
		await _db.AddUserToChannelAsync(channel, alice);

		var title = MModule.single("<Bob>");
		await _db.UpdateChannelUserStatusAsync(channel, alice,
			new SharpChannelStatus(Combine: true, Gagged: true, Hide: true, Mute: true, Title: title));

		var refreshed = (await _db.GetChannelAsync("StatusChan"))!;
		var status = await refreshed.Members.Value.Where(m => m.Member.Object().DBRef.Number == alice.Object.DBRef.Number)
			.Select(m => m.Status).FirstAsync();

		await Assert.That(status.Combine).IsTrue();
		await Assert.That(status.Gagged).IsTrue();
		await Assert.That(status.Hide).IsTrue();
		await Assert.That(status.Mute).IsTrue();
		await Assert.That(status.Title!.ToPlainText()).IsEqualTo("<Bob>");

		// A partial update leaves the fields it did not name untouched.
		await _db.UpdateChannelUserStatusAsync(channel, alice, new SharpChannelStatus(null, false, null, null, null));
		var afterPartial = (await _db.GetChannelAsync("StatusChan"))!;
		var partialStatus = await afterPartial.Members.Value.Where(m => m.Member.Object().DBRef.Number == alice.Object.DBRef.Number)
			.Select(m => m.Status).FirstAsync();

		await Assert.That(partialStatus.Combine).IsTrue();
		await Assert.That(partialStatus.Gagged).IsFalse();
		await Assert.That(partialStatus.Hide).IsTrue();
		await Assert.That(partialStatus.Mute).IsTrue();
	}

	[Test]
	public async Task DeleteChannelAsyncRemovesTheRecordEveryMemberAndEveryReverseEntry()
	{
		var god = await God();
		var alice = await NewPlayer("Carol");
		await _db.CreateChannelAsync(MModule.single("DoomedChan"), ["Player"], god);
		var channel = (await _db.GetChannelAsync("DoomedChan"))!;
		await _db.AddUserToChannelAsync(channel, alice);

		await _db.DeleteChannelAsync(channel);

		await Assert.That(await _db.GetChannelAsync("DoomedChan")).IsNull();

		var chanKey = Keys.Upper("DoomedChan");
		var leftoverMembers = _db.Store.Read(tx => tx.Range(Tables.ChanMember, Keys.Concat(chanKey, Keys.Sep)).Count());
		await Assert.That(leftoverMembers).IsEqualTo(0);

		var godKey = Keys.Dbref(god.Object.DBRef.Number);
		var aliceKey = Keys.Dbref(alice.Object.DBRef.Number);
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.RevChanMember, godKey).Any(v => v.AsSpan().SequenceEqual(chanKey)))).IsFalse();
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.RevChanMember, aliceKey).Any(v => v.AsSpan().SequenceEqual(chanKey)))).IsFalse();
	}

	[Test]
	public async Task DeletingAMemberObjectRemovesItsChannelMembershipsToo()
	{
		var god = await God();
		var dbref = await _db.CreatePlayerAsync("Dave", "pw", new DBRef(0), new DBRef(0), 0);
		var dave = (await _db.GetObjectNodeAsync(dbref)).Known.AsPlayer;
		await _db.CreateChannelAsync(MModule.single("CascadeChan"), ["Player"], god);
		var channel = (await _db.GetChannelAsync("CascadeChan"))!;
		await _db.AddUserToChannelAsync(channel, dave);

		await _db.DeleteObjectAsync(dbref);

		var memberKey = Keys.Composite("CASCADECHAN", (long)dbref.Number);
		await Assert.That(_db.Store.Read(tx => tx.TryGet(Tables.ChanMember, memberKey, out _))).IsFalse();
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.RevChanMember, Keys.Dbref(dbref.Number)).Count())).IsEqualTo(0);

		var survivingMembers = await channel.Members.Value.Select(m => m.Member.Object().DBRef.Number).ToListAsync();
		await Assert.That(survivingMembers).DoesNotContain(dbref.Number);
	}

	[Test]
	public async Task RenameMovesTheChannelRowAndEveryMemberAndReverseEntryToTheNewKey()
	{
		var god = await God();
		var alice = await NewPlayer("Eve");
		await _db.CreateChannelAsync(MModule.single("OldName"), ["Player"], god);
		var channel = (await _db.GetChannelAsync("OldName"))!;
		await _db.AddUserToChannelAsync(channel, alice);

		await _db.UpdateChannelAsync(channel, MModule.single("NewName"), null, null, null, null, null, null, null, null, null);

		await Assert.That(await _db.GetChannelAsync("OldName")).IsNull();
		var renamed = await _db.GetChannelAsync("NewName");
		await Assert.That(renamed).IsNotNull();

		var oldKey = Keys.Upper("OldName");
		var newKey = Keys.Upper("NewName");
		var oldPrefix = Keys.Concat(oldKey, Keys.Sep);
		await Assert.That(_db.Store.Read(tx => tx.Range(Tables.ChanMember, oldPrefix).Count())).IsEqualTo(0);
		await Assert.That(_db.Store.Read(tx => tx.TryGet(Tables.ChanMember, Keys.Composite("NEWNAME", (long)alice.Object.DBRef.Number), out _))).IsTrue();

		var aliceKey = Keys.Dbref(alice.Object.DBRef.Number);
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.RevChanMember, aliceKey).Any(v => v.AsSpan().SequenceEqual(oldKey)))).IsFalse();
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.RevChanMember, aliceKey).Any(v => v.AsSpan().SequenceEqual(newKey)))).IsTrue();
	}

	[Test]
	public async Task GetChannelsOwnedByAndGetMemberChannelsFindWhatTheyShould()
	{
		var god = await God();
		var alice = await NewPlayer("Frank");
		await _db.CreateChannelAsync(MModule.single("OwnedByGod"), ["Player"], god);
		var channel = (await _db.GetChannelAsync("OwnedByGod"))!;
		await _db.AddUserToChannelAsync(channel, alice);

		var owned = await _db.GetChannelsOwnedByAsync(god.Object.DBRef).Select(c => c.Name.ToPlainText()).ToListAsync();
		await Assert.That(owned).Contains("OwnedByGod");

		var memberOf = await _db.GetMemberChannelsAsync(alice).Select(c => c.Name.ToPlainText()).ToListAsync();
		await Assert.That(memberOf).Contains("OwnedByGod");
	}
}
