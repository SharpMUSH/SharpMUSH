using NSubstitute;
using System.Runtime.CompilerServices;
using SharpMUSH.Library;
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
	[Arguments("room-light")]
	[Arguments("room-dark")]
	[Arguments("viewer-power")]
	[Arguments("item-dark")]
	[Arguments("item-light")]
	public async Task ExplicitScanCancellationReachesVisibilityStreams(string stage)
	{
		var objects = new TestObjectFactory();
		var room = objects.CreateRoom(10, "Room");
		var viewer = objects.CreatePlayer(11, "Viewer", room);
		var item = objects.CreateThing(12, "Thing", room).AsContent;
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		using var cancellation = new CancellationTokenSource();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var count = 0;
		var skip = stage is "room-dark" or "item-light" ? 1 : 0;
		async IAsyncEnumerable<T> Block<T>([EnumeratorCancellation] CancellationToken token = default)
		{
			if (count++ < skip) yield break;
			entered.TrySetResult(token);
			await release.Task.WaitAsync(token);
			yield break;
		}
		if (stage.StartsWith("room")) room.Object.Flags = new(() => Block<SharpObjectFlag>());
		else if (stage == "viewer-power") viewer.Object().Powers = new(() => Block<SharpPower>());
		else item.Object().Flags = new(() => Block<SharpObjectFlag>());
		async Task Run()
		{
			var scan = await WorldVisibility.CreateScanAsync(viewer, room, reality, Substitute.For<IConnectionService>(), cancellation.Token);
			await scan(item, cancellation.Token);
		}
		var pending = Run();
		try
		{
			var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(token.CanBeCanceled).IsTrue();
			cancellation.Cancel();
			await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5))).Throws<OperationCanceledException>();
		}
		finally { release.TrySetResult(); }
	}

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
	public async Task AContentsScanReadsReceiverAndContainerOnceAndRefreshesForTheNextScan()
	{
		var factory = new TestObjectFactory();
		var room = factory.CreateRoom(10, "Room");
		var viewer = factory.CreatePlayer(11, "Viewer", room);
		var first = factory.CreateThing(12, "First", room);
		var second = factory.CreateThing(13, "Second", room);
		var objects = Substitute.For<IObjectStore>();
		foreach (var obj in new AnySharpObject[] { room, viewer, first, second })
		{
			obj.Object().Id = obj.Object().DBRef.ToString();
			objects.GetObjectNodeAsync(obj.Object().DBRef, Arg.Any<CancellationToken>()).Returns(obj.WithNoneOption());
		}
		var store = Substitute.For<IExpandedDataStore>();
		store.GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, Arg.Any<CancellationToken>())
			.Returns(new RealityConfiguration(1, true, ["normal", "ghost"]));
		var policy = new RealityPolicy(store, objects);
		var connections = Substitute.For<IConnectionService>();
		var scan = await WorldVisibility.CreateScanAsync(viewer, room, policy, connections);
		await Assert.That(await scan(viewer.AsContent, default)).IsFalse(); // Offline players remain omitted.
		await Assert.That(await scan(first.AsContent, default)).IsTrue();
		await Assert.That(await scan(second.AsContent, default)).IsTrue();
		await store.Received(1).GetExpandedObjectData<ObjectReality>(viewer.Object().Id!, RealityPolicy.ObjectKey, Arg.Any<CancellationToken>());
		await store.Received(1).GetExpandedObjectData<ObjectReality>(room.Object.Id!, RealityPolicy.ObjectKey, Arg.Any<CancellationToken>());
		store.GetExpandedObjectData<ObjectReality>(second.Object().Id!, RealityPolicy.ObjectKey, Arg.Any<CancellationToken>())
			.Returns(ObjectReality.Default(second.Object().DBRef) with { Transmit = ["ghost"] });
		await Assert.That(await scan(second.AsContent, default)).IsFalse();
		store.GetExpandedObjectData<ObjectReality>(viewer.Object().Id!, RealityPolicy.ObjectKey, Arg.Any<CancellationToken>())
			.Returns(ObjectReality.Default(viewer.Object().DBRef) with { Receive = ["ghost"] });
		var next = await WorldVisibility.CreateScanAsync(viewer, room, policy, Substitute.For<IConnectionService>());
		await Assert.That(await next(first.AsContent, default)).IsFalse();
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
