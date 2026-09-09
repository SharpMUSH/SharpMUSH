using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class WorldVisibilityTests
{
	internal static void Flags(SharpObject obj, params string[] names) => obj.Flags = new(() => names.Select(name =>
		new SharpObjectFlag { Name = name, Symbol = "", System = true, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = [] }).ToAsyncEnumerable());

	[Test]
	[Arguments(false, false, false, false, false, true)]
	[Arguments(false, false, true, false, false, false)]
	[Arguments(true, false, true, false, false, true)]
	[Arguments(true, false, true, false, true, false)]
	[Arguments(false, true, false, false, false, false)]
	[Arguments(false, true, false, true, false, true)]
	public async Task ExistingLookLightAndDarkRulesRemainConsistent(bool roomLight, bool roomDark,
		bool itemDark, bool itemLight, bool exit, bool expected)
	{
		var objects = new TestObjectFactory();
		var room = objects.CreateRoom(10, "Room");
		var viewer = objects.CreatePlayer(11, "Viewer", room);
		var item = exit ? objects.CreateExit(12, "Exit", [], room).AsContent : objects.CreateThing(12, "Thing", room).AsContent;
		Flags(room.Object, roomLight ? ["LIGHT"] : roomDark ? ["DARK"] : []);
		Flags(item.Object(), itemDark ? ["DARK"] : itemLight ? ["LIGHT"] : []);
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		await Assert.That(await WorldVisibility.CanSeeContentAsync(viewer, room, item, reality,
			Substitute.For<IConnectionService>())).IsEqualTo(expected);
	}

	[Test]
	public async Task RoomLightAndStaffPrivilegesCannotOverrideReality()
	{
		var objects = new TestObjectFactory();
		var room = objects.CreateRoom(10, "Room");
		var viewer = objects.CreatePlayer(1, "God", room);
		var item = objects.CreateThing(12, "Hidden", room).AsContent;
		Flags(room.Object, "LIGHT");
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(viewer.Object().DBRef, room.Object.DBRef, Arg.Any<CancellationToken>()).Returns(true);
		await Assert.That(await WorldVisibility.CanSeeContentAsync(viewer, room, item, reality,
			Substitute.For<IConnectionService>())).IsFalse();
	}
}
