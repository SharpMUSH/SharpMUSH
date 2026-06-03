using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ConnectionService.BindAccount"/> — validates that the
/// AccountMode state transition is handled correctly: metadata is set, state changes,
/// and listeners/notifications are fired.
/// </summary>
public class ConnectionServiceAccountModeTests
{
	private static ConnectionService BuildService(out IPublisher publisher)
	{
		publisher = Substitute.For<IPublisher>();
		return new ConnectionService(publisher);
	}

	private static ConcurrentDictionary<string, string> DefaultMetadata() =>
		new(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = "websocket"
		});

	private static async Task<(ConnectionService Svc, IPublisher Publisher)> MakeRegistered(long handle)
	{
		var publisher = Substitute.For<IPublisher>();
		var svc = new ConnectionService(publisher);
		await svc.Register(handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			DefaultMetadata());
		return (svc, publisher);
	}

	// ── BindAccount ───────────────────────────────────────────────────────────

	[Test]
	public async ValueTask BindAccount_TransitionsToAccountMode()
	{
		var (svc, _) = await MakeRegistered(1);

		await svc.BindAccount(1, "accounts/42");

		var data = svc.Get(1);
		await Assert.That(data).IsNotNull();
		await Assert.That(data!.State).IsEqualTo(IConnectionService.ConnectionState.AccountMode);
	}

	[Test]
	public async ValueTask BindAccount_StoresAccountIdInMetadata()
	{
		var (svc, _) = await MakeRegistered(1);

		await svc.BindAccount(1, "accounts/42");

		var data = svc.Get(1);
		await Assert.That(data!.Metadata.TryGetValue("AccountId", out var id)).IsTrue();
		await Assert.That(id).IsEqualTo("accounts/42");
	}

	[Test]
	public async ValueTask BindAccount_PublishesConnectionStateChangeNotification()
	{
		var (svc, publisher) = await MakeRegistered(1);

		await svc.BindAccount(1, "accounts/42");

		// IPublisher.Publish is a generic method; use NSubstitute.ReceivedCalls() extension
		var calls = publisher.ReceivedCalls()
			.Where(c => c.GetMethodInfo().Name == "Publish")
			.ToList();
		await Assert.That(calls.Count).IsGreaterThan(0);
	}

	[Test]
	public async ValueTask BindAccount_FiresStateChangeListeners()
	{
		var (svc, _) = await MakeRegistered(1);

		IConnectionService.ConnectionState? capturedNewState = null;
		svc.ListenState(evt => capturedNewState = evt.Item4);

		await svc.BindAccount(1, "accounts/42");

		await Assert.That(capturedNewState).IsEqualTo(IConnectionService.ConnectionState.AccountMode);
	}

	[Test]
	public async ValueTask BindAccount_ListenerReceivesOldAndNewState()
	{
		var (svc, _) = await MakeRegistered(1);

		IConnectionService.ConnectionState? capturedOld = null;
		IConnectionService.ConnectionState? capturedNew = null;

		svc.ListenState(evt =>
		{
			capturedOld = evt.Item3;
			capturedNew = evt.Item4;
		});

		await svc.BindAccount(1, "accounts/42");

		await Assert.That(capturedOld).IsEqualTo(IConnectionService.ConnectionState.Connected);
		await Assert.That(capturedNew).IsEqualTo(IConnectionService.ConnectionState.AccountMode);
	}

	[Test]
	public async ValueTask BindAccount_UnknownHandle_DoesNotThrow()
	{
		var publisher = Substitute.For<IPublisher>();
		var svc = new ConnectionService(publisher);

		// Handle 999 was never registered — BindAccount should silently no-op
		await svc.BindAccount(999, "accounts/42");
	}

	[Test]
	public async ValueTask BindAccount_DoesNotSetRef_RefRemainsNull()
	{
		var (svc, _) = await MakeRegistered(1);

		await svc.BindAccount(1, "accounts/42");

		var data = svc.Get(1);
		await Assert.That(data!.Ref).IsNull();
	}

	// ── Idle/Connected properties in AccountMode ──────────────────────────────

	[Test]
	public async ValueTask ConnectionData_InAccountMode_IdleIsNotNull()
	{
		var (svc, _) = await MakeRegistered(1);

		await svc.BindAccount(1, "accounts/42");
		var data = svc.Get(1);

		await Assert.That(data!.Idle).IsNotNull();
	}

	[Test]
	public async ValueTask ConnectionData_InAccountMode_ConnectedIsNotNull()
	{
		var (svc, _) = await MakeRegistered(1);

		await svc.BindAccount(1, "accounts/42");
		var data = svc.Get(1);

		await Assert.That(data!.Connected).IsNotNull();
	}

	// ── BindAccount then Bind (character login) ───────────────────────────────

	[Test]
	public async ValueTask BindAccount_ThenBind_TransitionsToLoggedIn()
	{
		var (svc, _) = await MakeRegistered(1);
		var playerRef = new DBRef(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

		await svc.BindAccount(1, "accounts/42");
		await svc.Bind(1, playerRef);

		var data = svc.Get(1);
		await Assert.That(data!.State).IsEqualTo(IConnectionService.ConnectionState.LoggedIn);
		await Assert.That(data.Ref).IsEqualTo(playerRef);
	}

	[Test]
	public async ValueTask BindAccount_ThenBind_AccountIdRemainsInMetadata()
	{
		var (svc, _) = await MakeRegistered(1);
		var playerRef = new DBRef(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

		await svc.BindAccount(1, "accounts/42");
		await svc.Bind(1, playerRef);

		// AccountId set during account mode should persist after character login
		var data = svc.Get(1);
		await Assert.That(data!.Metadata.TryGetValue("AccountId", out var id)).IsTrue();
		await Assert.That(id).IsEqualTo("accounts/42");
	}
}
