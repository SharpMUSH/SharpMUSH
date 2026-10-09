using DotNext.Threading;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The edge reads (contents, exits, entrances, homed-at, zone members, children, channel members, mailboxes)
/// read their set a page at a time. A set larger than any page has to come back whole, in the order one read of
/// the index gives it, with the rows whose object is gone left out exactly as before.
/// </summary>
public class EdgePagingTests : LightningDatabaseFixture
{
	/// <summary>More than the largest page, and not a multiple of any page size the reader steps through.</summary>
	private const int SetSize = 300 + LightningStore.FirstMapPageSize + 3;

	private async Task<SharpPlayer> God() => (await Db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<AnySharpObject> Node(DBRef dbref) => (await Db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();

	private List<long> IndexOrder(TableDef table, long key)
		=> Db.Store.Read(tx => tx.Dups(table, Keys.Dbref(key)).Select(v => Keys.ReadDbref(v)).ToList());

	private async Task<AnySharpContainer> NewRoom(string name)
		=> (await Node(await Db.CreateRoomAsync(name, await God()))).AsOptionalContainer.Expect<AnySharpContainer>();

	private async Task<List<DBRef>> NewThings(AnySharpContainer room, int count)
	{
		var god = await God();
		var created = new List<DBRef>();
		for (var i = 0; i < count; i++)
		{
			created.Add(await Db.CreateThingAsync($"Thing{i}", room, god, room));
		}

		return created;
	}

	[Test]
	public async Task ContentsLargerThanAPage_ComeBackWholeInIndexOrder()
	{
		var room = await NewRoom("BigRoom");
		var things = await NewThings(room, SetSize);

		var contents = await Db.GetContentRefsAsync(room.Object().DBRef).Select(c => (long)c.Number).ToListAsync();

		await Assert.That(contents).Count().IsEqualTo(SetSize);
		await Assert.That(contents).IsEquivalentTo(things.Select(t => (long)t.Number));
		await Assert.That(contents).IsEquivalentTo(IndexOrder(Tables.Location.Reverse, room.Object().Key), CollectionOrdering.Matching);
	}

	[Test]
	public async Task HomedAtLargerThanAPage_LeavesRoomsOut()
	{
		var room = await NewRoom("BigHome");
		await NewThings(room, SetSize);
		// A room's drop-to reuses the home edge; it is not homed there.
		var dropping = (await NewRoom("DropsHere")).Object();
		await Db.LinkRoomAsync((await Node(dropping.DBRef)).Expect<SharpRoom>(), room);

		var homed = await Db.GetHomedAtAsync(room.Object().DBRef).Select(c => (long)c.Object().Key).ToListAsync();

		await Assert.That(homed).Count().IsEqualTo(SetSize);
		await Assert.That(homed).DoesNotContain((long)dropping.Key);
		await Assert.That(homed).IsEquivalentTo(
			IndexOrder(Tables.Home.Reverse, room.Object().Key).Where(k => k != dropping.Key), CollectionOrdering.Matching);
	}

	[Test]
	public async Task ExitsAndEntrancesLargerThanAPage_ComeBackWholeInIndexOrder()
	{
		var god = await God();
		var source = await NewRoom("ExitSource");
		var destination = await NewRoom("ExitDestination");
		for (var i = 0; i < SetSize; i++)
		{
			var exit = (await Node(await Db.CreateExitAsync($"Exit{i}", [], source, god))).Expect<SharpExit>();
			await Db.LinkExitAsync(exit, destination);
		}

		var exits = await Db.GetExitsAsync(source).Select(e => (long)e.Object.Key).ToListAsync();
		var entrances = await Db.GetEntrancesAsync(destination.Object().DBRef).Select(e => (long)e.Object.Key).ToListAsync();

		await Assert.That(exits).Count().IsEqualTo(SetSize);
		await Assert.That(exits).IsEquivalentTo(IndexOrder(Tables.Exit.Forward, source.Object().Key), CollectionOrdering.Matching);
		await Assert.That(entrances).IsEquivalentTo(exits, CollectionOrdering.Matching);
	}

	[Test]
	public async Task ChildrenAndZoneMembersLargerThanAPage_ComeBackWholeInIndexOrder()
	{
		var room = await NewRoom("FamilyRoom");
		var parent = await Node((await NewThings(room, 1))[0]);
		foreach (var thing in await NewThings(room, SetSize))
		{
			var child = await Node(thing);
			await Db.SetObjectParent(child, parent);
			await Db.SetObjectZone(child, parent);
		}

		var children = await (await Node(parent.Object().DBRef)).Object().Children.Value!.Select(c => (long)c.Key).ToListAsync();
		var zoned = await Db.GetZoneMemberRefsAsync(parent.Object().DBRef).Select(c => (long)c.Number).ToListAsync();

		await Assert.That(children).Count().IsEqualTo(SetSize);
		await Assert.That(children).IsEquivalentTo(IndexOrder(Tables.Parent.Reverse, parent.Object().Key), CollectionOrdering.Matching);
		await Assert.That(zoned).IsEquivalentTo(children, CollectionOrdering.Matching);
	}

	/// <summary>
	/// A ref stamped for an object whose number now names another one reads as no set at all, not the
	/// newer object's contents or zone members.
	/// </summary>
	[Test]
	public async Task ContentsAndZoneMembersOfAStaleRef_AreEmpty()
	{
		var room = await NewRoom("RecycledRoom");
		var zone = await Node((await NewThings(room, 1))[0]);
		await Db.SetObjectZone(await Node((await NewThings(room, 1))[0]), zone);
		var staleRoom = room.Object().DBRef with { CreationMilliseconds = room.Object().DBRef.CreationMilliseconds - 1 };
		var staleZone = zone.Object().DBRef with { CreationMilliseconds = zone.Object().DBRef.CreationMilliseconds - 1 };

		await Assert.That(await Db.GetContentRefsAsync(room.Object().DBRef).CountAsync()).IsEqualTo(2);
		await Assert.That(await Db.GetZoneMemberRefsAsync(zone.Object().DBRef).CountAsync()).IsEqualTo(1);
		await Assert.That(await Db.GetContentRefsAsync(staleRoom).CountAsync()).IsEqualTo(0);
		await Assert.That(await Db.GetZoneMemberRefsAsync(staleZone).CountAsync()).IsEqualTo(0);
	}

	[Test]
	public async Task ChannelMembersLargerThanAPage_ComeBackWholeAndSkipGoneObjects()
	{
		var god = await God();
		await Db.CreateChannelAsync(MarkupText.Plain("PagedChannel"), [], god);
		var channel = (await Db.GetChannelAsync("PagedChannel"))!;
		var room = await NewRoom("ChannelRoom");
		foreach (var thing in await NewThings(room, SetSize))
		{
			await Db.AddUserToChannelAsync(channel, await Node(thing));
		}

		// A membership row naming an object that no longer exists is not a member.
		await Db.Store.WriteAsync(tx => tx.Put(Tables.ChanMember, Keys.Composite("PAGEDCHANNEL", 999_999L),
			Codec.Serialize(new SharpMUSH.Database.Lightning.Records.ChannelMemberRecord { Title = "" })));

		var members = await channel.Members.Value.Select(m => (long)m.Member.Object().Key).ToListAsync();

		await Assert.That(members).Count().IsEqualTo(SetSize + 1);
		await Assert.That(members).DoesNotContain(999_999L);
		await Assert.That(members).IsInOrder();
		await Assert.That(members[0]).IsEqualTo((long)god.Object.Key);
	}

	[Test]
	public async Task MailboxesLargerThanAPage_ComeBackWholeInIdOrder()
	{
		var sender = (await Node(await Db.CreatePlayerAsync("PagedSender", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		var recipient = (await Node(await Db.CreatePlayerAsync("PagedRecipient", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		for (var i = 0; i < SetSize; i++)
		{
			await Db.SendMailAsync(sender.Object, recipient, NewMail($"S{i:D4}", i % 2 == 0 ? "INBOX" : "OTHER"));
		}

		var all = await Db.GetAllIncomingMailsAsync(recipient).Select(m => m.Subject.ToPlainText()).ToListAsync();
		var inbox = await Db.GetIncomingMailsAsync(recipient, "INBOX").Select(m => m.Subject.ToPlainText()).ToListAsync();
		var sent = await Db.GetAllSentMailsAsync(sender.Object).Select(m => m.Subject.ToPlainText()).ToListAsync();
		var sentTo = await Db.GetSentMailsAsync(sender.Object, recipient).Select(m => m.Subject.ToPlainText()).ToListAsync();

		var expected = Enumerable.Range(0, SetSize).Select(i => $"S{i:D4}").ToList();
		await Assert.That(all).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(inbox).IsEquivalentTo(expected.Where((_, i) => i % 2 == 0), CollectionOrdering.Matching);
		await Assert.That(sent).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(sentTo).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	/// <summary>The sender is read when asked for, so a sender destroyed after the listing resolves to none.</summary>
	[Test]
	public async Task MailSender_IsResolvedWhenAskedFor()
	{
		var sender = (await Node(await Db.CreatePlayerAsync("LazySender", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		var recipient = (await Node(await Db.CreatePlayerAsync("LazyRecipient", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		await Db.SendMailAsync(sender.Object, recipient, NewMail("Lazy", "INBOX"));

		var mail = await Db.GetAllIncomingMailsAsync(recipient).SingleAsync();
		await Db.DeleteObjectAsync(sender.Object.DBRef);

		await Assert.That(await mail.From.WithCancellation(CancellationToken.None) is None).IsTrue();
	}

	private static SharpMail NewMail(string subject, string folder) => new()
	{
		DateSent = DateTimeOffset.UtcNow,
		Fresh = true,
		Read = false,
		Tagged = false,
		Urgent = false,
		Forwarded = false,
		Cleared = false,
		Folder = folder,
		Content = MarkupText.Plain(subject),
		Subject = MarkupText.Plain(subject),
		From = new AsyncLazy<AnyOptionalSharpObject>(_ => throw new InvalidOperationException("unused on send"))
	};
}
