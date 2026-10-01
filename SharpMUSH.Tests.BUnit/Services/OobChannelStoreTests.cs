using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

public class OobChannelStoreTests
{
	private const string TwoOccupants =
		"""{"v":2,"who":[{"dbref":"#5","objid":"#5:1","type":"player","name":"Bob","cmd":"look #5","you":true},{"dbref":"#6","name":"Ann","cmd":"look #6"}]}""";

	private const string OneOccupant = """{"v":2,"who":[{"dbref":"#7","name":"Cy","cmd":"look #7"}]}""";

	private const string Exits =
		"""{"v":2,"exits":[{"dbref":"#28","name":"north","aliases":["n"],"cmd":"goto #28","state":"open"}]}""";

	private const string Info =
		"""{"v":2,"dbref":"#35","objid":"#35:9","name":"Docks","scene":{"id":"42","title":"Salt Market","cast":3}}""";

	[Test]
	public async Task SetThenGetReturnsLatestAndRaisesEvent()
	{
		var store = new OobChannelStore();
		string? raised = null;
		store.ChannelUpdated += p => raised = p;

		store.Set("room.contents", "{\"who\":[]}");
		store.Set("room.contents", "{\"who\":[\"#5\"]}");

		await Assert.That(store.Get("room.contents")).IsEqualTo("{\"who\":[\"#5\"]}");
		await Assert.That(raised).IsEqualTo("room.contents");
		await Assert.That(store.Packages).Contains("room.contents");
	}

	[Test]
	public async Task EmptyPackageIsIgnored()
	{
		var store = new OobChannelStore();
		store.Set("", "{\"x\":1}");
		await Assert.That(store.Packages.Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_new_store_holds_an_empty_room()
	{
		var store = new OobChannelStore();

		await Assert.That(store.Room.Info).IsNull();
		await Assert.That(store.Room.Occupants).IsEmpty();
		await Assert.That(store.Room.Exits).IsEmpty();
	}

	[Test]
	public async Task Room_contents_are_kept_typed_and_announced()
	{
		var store = new OobChannelStore();
		var changes = 0;
		store.RoomChanged += () => changes++;

		store.Set(OobEntryParser.RoomContentsPackage, TwoOccupants);

		await Assert.That(changes).IsEqualTo(1);
		await Assert.That(store.Room.Occupants.Select(o => o.Name)).IsEquivalentTo(["Bob", "Ann"]);
		await Assert.That(store.Room.Occupants[0].ObjId).IsEqualTo("#5:1");
		await Assert.That(store.Room.Occupants[0].You).IsTrue();
	}

	[Test]
	public async Task Room_exits_and_info_are_kept_typed()
	{
		var store = new OobChannelStore();

		store.Set(OobEntryParser.RoomExitsPackage, Exits);
		store.Set(OobEntryParser.RoomInfoPackage, Info);

		await Assert.That(store.Room.Exits.Single().State).IsEqualTo(ExitState.Open);
		await Assert.That(store.Room.Exits.Single().Aliases).IsEquivalentTo(["n"]);
		await Assert.That(store.Room.Info!.ObjId).IsEqualTo("#35:9");
		await Assert.That(store.Room.Info.Scene).IsEqualTo(new RoomScene("42", "Salt Market", 3));
	}

	/// <summary>Every push is the whole list: the second replaces the first, it is not merged in.</summary>
	[Test]
	public async Task A_push_replaces_the_whole_list()
	{
		var store = new OobChannelStore();

		store.Set(OobEntryParser.RoomContentsPackage, TwoOccupants);
		store.Set(OobEntryParser.RoomContentsPackage, OneOccupant);

		await Assert.That(store.Room.Occupants.Select(o => o.Name)).IsEquivalentTo(["Cy"]);
	}

	[Test]
	public async Task A_push_leaves_the_other_room_parts_alone()
	{
		var store = new OobChannelStore();
		store.Set(OobEntryParser.RoomExitsPackage, Exits);
		store.Set(OobEntryParser.RoomInfoPackage, Info);
		var exits = store.Room.Exits;
		var info = store.Room.Info;

		store.Set(OobEntryParser.RoomContentsPackage, OneOccupant);

		await Assert.That(store.Room.Exits).IsSameReferenceAs(exits);
		await Assert.That(store.Room.Info).IsSameReferenceAs(info);
	}

	/// <summary>The latest push is the truth, so an unreadable one empties the list rather than keeping the last.</summary>
	[Test]
	public async Task An_unreadable_push_empties_that_part()
	{
		var store = new OobChannelStore();
		store.Set(OobEntryParser.RoomContentsPackage, TwoOccupants);
		store.Set(OobEntryParser.RoomInfoPackage, Info);

		store.Set(OobEntryParser.RoomContentsPackage, "not json");
		store.Set(OobEntryParser.RoomInfoPackage, "[]");

		await Assert.That(store.Room.Occupants).IsEmpty();
		await Assert.That(store.Room.Info).IsNull();
	}

	[Test]
	public async Task A_v1_push_is_kept_in_the_same_records()
	{
		var store = new OobChannelStore();

		store.Set(OobEntryParser.RoomContentsPackage, """{"who":[{"dbref":"#76","name":"Marble Bust","cmd":"look #76"}]}""");
		store.Set(OobEntryParser.RoomExitsPackage, """{"exits":[{"name":"east","cmd":"goto #80"}]}""");

		await Assert.That(store.Room.Occupants.Single().Cmd).IsEqualTo("look #76");
		await Assert.That(store.Room.Occupants.Single().ObjId).IsNull();
		await Assert.That(store.Room.Exits.Single().Cmd).IsEqualTo("goto #80");
		await Assert.That(store.Room.Exits.Single().State).IsNull();
	}

	[Test]
	public async Task Another_package_does_not_touch_the_room()
	{
		var store = new OobChannelStore();
		store.Set(OobEntryParser.RoomContentsPackage, TwoOccupants);
		var before = store.Room;
		var changes = 0;
		store.RoomChanged += () => changes++;

		store.Set("query.7", "\"42\"");

		await Assert.That(changes).IsEqualTo(0);
		await Assert.That(store.Room).IsSameReferenceAs(before);
	}

	/// <summary>A ChannelUpdated subscriber reading the typed room sees the payload that raised it.</summary>
	[Test]
	public async Task The_room_is_current_when_ChannelUpdated_fires()
	{
		var store = new OobChannelStore();
		IReadOnlyList<string>? seen = null;
		store.ChannelUpdated += _ => seen = [.. store.Room.Occupants.Select(o => o.Name)];

		store.Set(OobEntryParser.RoomContentsPackage, TwoOccupants);

		await Assert.That(seen).IsEquivalentTo(["Bob", "Ann"]);
	}

	[Test]
	public async Task Clear_empties_the_room_and_announces_it()
	{
		var store = new OobChannelStore();
		store.Set(OobEntryParser.RoomContentsPackage, TwoOccupants);
		store.Set(OobEntryParser.RoomExitsPackage, Exits);
		store.Set(OobEntryParser.RoomInfoPackage, Info);
		var changes = 0;
		store.RoomChanged += () => changes++;

		store.Clear();

		await Assert.That(changes).IsEqualTo(1);
		await Assert.That(store.Room.Info).IsNull();
		await Assert.That(store.Room.Occupants).IsEmpty();
		await Assert.That(store.Room.Exits).IsEmpty();
	}
}
