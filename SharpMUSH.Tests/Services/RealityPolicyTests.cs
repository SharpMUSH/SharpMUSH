using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;

namespace SharpMUSH.Tests.Services;

public class RealityPolicyTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MalformedProfilesDenyGameplayPerceptionButRemainAdministrativeErrors(bool malformedReceiver)
	{
		var factory = new TestObjectFactory();
		var receiver = factory.CreatePlayer(50, "receiver");
		var target = factory.CreatePlayer(51, "target");
		receiver.Object().Id = "receiver";
		target.Object().Id = "target";
		var objects = Substitute.For<IObjectStore>();
		objects.GetObjectNodeAsync(receiver.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(receiver.AsPlayer));
		objects.GetObjectNodeAsync(target.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(target.AsPlayer));
		var store = Substitute.For<IExpandedDataStore>();
		store.GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, Arg.Any<CancellationToken>())
			.Returns(new RealityConfiguration(1, true, ["normal"]));
		var malformed = malformedReceiver ? receiver : target;
		store.GetExpandedObjectData<ObjectReality>(malformed.Object().Id!, RealityPolicy.ObjectKey, Arg.Any<CancellationToken>())
			.Returns(ObjectReality.Default(malformed.Object().DBRef) with { Version = 2 });
		var policy = new RealityPolicy(store, objects);
		await Assert.ThrowsAsync<InvalidDataException>(async () => await policy.ReadObjectAsync(malformed.Object().DBRef));
		await Assert.That(await policy.CanPerceiveAsync(receiver.Object().DBRef, target.Object().DBRef)).IsFalse();
		var scan = await policy.ObserveAsync(receiver.Object().DBRef);
		await Assert.That(await scan(target.Object().DBRef, default)).IsFalse();
		await Assert.That(await policy.DescriptionAttributeAsync(receiver.Object().DBRef, target.Object().DBRef)).IsNull();
	}

	[Test]
	public async Task EnabledPolicyDoesNotTreatMissingObjectsAsVisible()
	{
		var store = Substitute.For<IExpandedDataStore>();
		store.GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, Arg.Any<CancellationToken>())
			.Returns(new RealityConfiguration(1, true, ["normal", "ghost"]));
		var policy = new RealityPolicy(store, Substitute.For<IObjectStore>());
		await Assert.That(await policy.CanPerceiveAsync(new DBRef(20, 1), new DBRef(21, 1))).IsFalse();
	}

	[Test]
	public async Task MissingConfigurationPreservesDisabledPennBehavior()
	{
		var store = Substitute.For<IExpandedDataStore>();
		var policy = new RealityPolicy(store, Substitute.For<IObjectStore>());
		await Assert.That(await policy.IsEnabledAsync()).IsFalse();
		await Assert.That(await policy.CanPerceiveAsync(new DBRef(20, 1), new DBRef(21, 1))).IsTrue();
	}

	[Test]
	[Arguments("normal", "normal", true)]
	[Arguments("normal", "ghost", false)]
	[Arguments("normal,ghost", "ghost", true)]
	[Arguments("", "normal", false)]
	[Arguments("normal", "", false)]
	public async Task ReceiverAndPresenceSetsDetermineDirectionalVisibility(string receive, string transmit, bool expected)
	{
		var factory = new TestObjectFactory();
		var receiver = factory.CreatePlayer(20, "receiver");
		var target = factory.CreatePlayer(21, "target");
		receiver.Object().Id = "receiver";
		target.Object().Id = "target";
		var objects = Substitute.For<IObjectStore>();
		objects.GetObjectNodeAsync(receiver.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(receiver.AsPlayer));
		objects.GetObjectNodeAsync(target.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(target.AsPlayer));
		var store = Substitute.For<IExpandedDataStore>();
		store.GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, Arg.Any<CancellationToken>())
			.Returns(new RealityConfiguration(1, true, ["normal", "ghost"]));
		store.GetExpandedObjectData<ObjectReality>("receiver", RealityPolicy.ObjectKey, Arg.Any<CancellationToken>())
			.Returns(new ObjectReality(1, receiver.Object().DBRef, receive.Split(',', StringSplitOptions.RemoveEmptyEntries), ["normal"], []));
		store.GetExpandedObjectData<ObjectReality>("target", RealityPolicy.ObjectKey, Arg.Any<CancellationToken>())
			.Returns(new ObjectReality(1, target.Object().DBRef, ["normal"], transmit.Split(',', StringSplitOptions.RemoveEmptyEntries), []));
		var policy = new RealityPolicy(store, objects);
		await Assert.That(await policy.CanPerceiveAsync(receiver.Object().DBRef, target.Object().DBRef)).IsEqualTo(expected);
		await Assert.That(await policy.CanPerceiveAsync(receiver.Object().DBRef, receiver.Object().DBRef)).IsTrue();
		await Assert.That(await policy.CanPerceiveAsync(target.Object().DBRef, receiver.Object().DBRef)).IsTrue();
	}


	[Test]
	[Arguments(false, false, true, "GHOSTDESC")]
	[Arguments(true, false, false, null)]
	[Arguments(false, true, false, null)]
	public async Task DescriptionsRespectConfiguredLayersAndObjectIncarnation(bool removed, bool recycled, bool visible, string? description)
	{
		var factory = new TestObjectFactory();
		var receiver = factory.CreatePlayer(30, "seer");
		var target = factory.CreatePlayer(31, "ghost");
		receiver.Object().Id = "seer";
		target.Object().Id = "ghost";
		var objects = Substitute.For<IObjectStore>();
		objects.GetObjectNodeAsync(receiver.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(receiver.AsPlayer));
		objects.GetObjectNodeAsync(target.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(target.AsPlayer));
		var store = Substitute.For<IExpandedDataStore>();
		store.GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, Arg.Any<CancellationToken>())
			.Returns(new RealityConfiguration(1, true, removed ? ["normal"] : ["normal", "ghost"]));
		store.GetExpandedObjectData<ObjectReality>("seer", RealityPolicy.ObjectKey, Arg.Any<CancellationToken>())
			.Returns(new ObjectReality(1, receiver.Object().DBRef, ["ghost"], ["normal"], []));
		store.GetExpandedObjectData<ObjectReality>("ghost", RealityPolicy.ObjectKey, Arg.Any<CancellationToken>())
			.Returns(new ObjectReality(1, recycled ? new DBRef(31, 99) : target.Object().DBRef, ["normal"], ["ghost"], new() { ["ghost"] = "GHOSTDESC" }));
		var policy = new RealityPolicy(store, objects);
		await Assert.That(await policy.CanPerceiveAsync(receiver.Object().DBRef, target.Object().DBRef)).IsEqualTo(visible);
		await Assert.That(await policy.DescriptionAttributeAsync(receiver.Object().DBRef, target.Object().DBRef)).IsEqualTo(description);
	}

}
