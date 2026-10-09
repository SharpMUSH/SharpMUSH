using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The store's counts and point reads — child count, objects by type, highest dbref, one channel membership,
/// a channel's population — answer what walking the full sets answered.
/// </summary>
public class ProjectionTests : LightningDatabaseFixture
{
	private async Task<SharpPlayer> God() => (await Db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<AnySharpObject> Node(DBRef dbref) => (await Db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();

	private async Task<AnySharpContainer> Room() => (await Node(new DBRef(2))).AsOptionalContainer.Expect<AnySharpContainer>();

	private async Task<DBRef> Thing(string name, SharpPlayer? owner = null)
	{
		var room = await Room();
		return await Db.CreateThingAsync(name, room, owner ?? await God(), room);
	}

	[Test]
	public async Task ChildCount_MatchesTheChildren()
	{
		var parent = await Node(await Thing("Parent"));
		await Assert.That(await Db.GetChildCountAsync(parent.Object().DBRef)).IsEqualTo(0);

		for (var i = 0; i < 5; i++)
		{
			await Db.SetObjectParent(await Node(await Thing($"Child{i}")), parent);
		}

		var children = await (await Node(parent.Object().DBRef)).Object().Children.Value!.CountAsync();
		await Assert.That(await Db.GetChildCountAsync(parent.Object().DBRef)).IsEqualTo(5);
		await Assert.That(children).IsEqualTo(5);
	}

	[Test]
	public async Task TypeCounts_MatchAScanOfTheObjects()
	{
		var god = await God();
		var owner = (await Node(await Db.CreatePlayerAsync("CountOwner", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		await Thing("Owned1", owner);
		await Thing("Owned2", owner);
		await Db.CreateRoomAsync("OwnedRoom", owner);
		await Db.CreateExitAsync("OwnedExit", [], await Room(), owner);
		await Thing("NotOwned", god);

		var all = await Db.GetAllObjectsAsync().ToListAsync();
		var everyone = await Db.GetObjectTypeCountsAsync(null);
		await Assert.That(everyone).IsEqualTo(new ObjectTypeCounts(
			all.Count(o => o.Type == "ROOM"), all.Count(o => o.Type == "EXIT"),
			all.Count(o => o.Type == "THING"), all.Count(o => o.Type == "PLAYER")));
		await Assert.That(everyone.Total).IsEqualTo(all.Count);

		// A player owns itself, so it counts among its own objects.
		await Assert.That(await Db.GetObjectTypeCountsAsync(owner.Object.DBRef)).IsEqualTo(new ObjectTypeCounts(1, 1, 2, 1));
	}

	[Test]
	public async Task HighestDbref_FollowsTheLastObject()
	{
		var highest = all(await Db.GetAllObjectsAsync().Select(o => o.Key).ToListAsync());
		await Assert.That(await Db.GetHighestDbrefAsync() is int before && before == highest).IsTrue();

		var created = await Thing("Topmost");
		await Assert.That(await Db.GetHighestDbrefAsync() is int top && top == created.Number).IsTrue();

		await Db.DeleteObjectAsync(created);
		await Assert.That(await Db.GetHighestDbrefAsync() is int after && after == highest).IsTrue();

		static int all(List<int> keys) => keys.Max();
	}

	[Test]
	public async Task HighestDbref_IsNotFoundForAnEmptyTable()
	{
		await Db.Store.WriteAsync(tx => tx.DeletePrefix(Tables.Obj, []));

		await Assert.That(await Db.GetHighestDbrefAsync() is NotFound).IsTrue();
	}

	[Test]
	public async Task ChannelMemberStatusAndCount_AnswerAsTheMemberListDoes()
	{
		var god = await God();
		await Db.CreateChannelAsync(MarkupText.Plain("PointChannel"), [], god);
		var channel = (await Db.GetChannelAsync("PointChannel"))!;
		var member = await Node(await Thing("Member"));
		var stranger = await Node(await Thing("Stranger"));
		await Db.AddUserToChannelAsync(channel, member);
		await Db.UpdateChannelUserStatusAsync(channel, member,
			new SharpChannelStatus(Combine: null, Gagged: true, Hide: null, Mute: null, Title: MarkupText.Plain("Sir")));
		// A membership row whose object is gone is not a member, and is not counted.
		await Db.Store.WriteAsync(tx => tx.Put(Tables.ChanMember, Keys.Composite("POINTCHANNEL", 999_999L),
			Codec.Serialize(new ChannelMemberRecord { Title = "" })));

		var status = await Db.GetChannelMemberStatusAsync(channel, member.Object().DBRef);
		await Assert.That(status is SharpChannelStatus { Gagged: true } found && found.Title!.ToPlainText() == "Sir").IsTrue();
		await Assert.That(await Db.GetChannelMemberStatusAsync(channel, new DBRef(member.Object().Key)) is SharpChannelStatus).IsTrue();
		await Assert.That(await Db.GetChannelMemberStatusAsync(channel, stranger.Object().DBRef) is NotFound).IsTrue();
		await Assert.That(await Db.GetChannelMemberStatusAsync(channel, new DBRef(999_999)) is NotFound).IsTrue();
		// An objid that is no longer the object's is not the member asked about.
		await Assert.That(await Db.GetChannelMemberStatusAsync(channel, new DBRef(member.Object().Key, 1)) is NotFound).IsTrue();

		var listed = await channel.Members.Value.CountAsync();
		await Assert.That(await Db.GetChannelMemberCountAsync(channel)).IsEqualTo(listed);
		await Assert.That(listed).IsEqualTo(2);
	}
}
