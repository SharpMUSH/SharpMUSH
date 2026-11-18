using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Substitutions;

namespace SharpMUSH.Tests.Internal;

public class TestHandleSetupCleanupTests
{
	[Test]
	[Arguments(false, false, false)]
	[Arguments(false, true, false)]
	[Arguments(true, false, false)]
	[Arguments(true, true, false)]
	[Arguments(false, false, true)]
	[Arguments(true, false, true)]
	[Arguments(false, true, true)]
	[Arguments(true, true, true)]
	public async Task FailedSetupReleasesTheHandleAndPreservesItsError(bool failBinding, bool failCleanup, bool cancelled)
	{
		var original = cancelled ? (Exception)new OperationCanceledException("setup cancelled") : new IOException("NATS did not respond");
		var cleanup = new IOException("NATS cleanup did not respond");
		var store = Substitute.For<IConnectionStateStore>();
		var connections = new ConnectionService(Substitute.For<IPublisher>(), store);
		var player = new DBRef(123);
		long handle = 0;
		store.SetConnectionAsync(Arg.Any<long>(), Arg.Any<ConnectionStateData>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				handle = call.ArgAt<long>(0);
				return failBinding ? Task.CompletedTask : Task.FromException(original);
			});
		store.SetPlayerBindingAsync(Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromException(original));
		if (failCleanup)
			store.RemoveConnectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(cleanup));

		Exception? observed = null;
		try { await TestIsolationHelpers.ConnectTestHandleAsync(connections, player); }
		catch (Exception error) { observed = error; }

		await Assert.That(observed).IsSameReferenceAs(original);
		await Assert.That(handle).IsGreaterThan(0);
		await Assert.That(connections.Get(handle)).IsNull();
		await Assert.That(await connections.Get(player).AnyAsync()).IsFalse();
		await Assert.That(await connections.GetAll().AnyAsync()).IsFalse();
		await store.Received(1).RemoveConnectionAsync(handle, Arg.Any<CancellationToken>());
		await Assert.That(original.Data["TestHandleCleanupException"]).IsSameReferenceAs(failCleanup ? cleanup : null);
	}

	[Test]
	public async Task SuccessfulSetupKeepsTheHandleUntilItsOwnerDisconnects()
	{
		var store = Substitute.For<IConnectionStateStore>();
		var connections = new ConnectionService(Substitute.For<IPublisher>(), store);
		var player = new DBRef(123);
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(connections, player);
		try
		{
			await Assert.That(connections.Get(handle)!.Ref).IsEqualTo(player);
			await store.DidNotReceive().RemoveConnectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
		}
		finally { await connections.Disconnect(handle); }
		await Assert.That(connections.Get(handle)).IsNull();
	}

	[Test]
	public async Task PrintedOutputTeardownDoesNothingWhenSetupDidNotCreateAnActor()
	{
		// No factory was initialized either: partial setup teardown must not resolve its services.
		var fixture = new PrintedOutputSubstitutionTests { Factory = null! };
		await fixture.DisconnectActor();
	}
}
