using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit tests for <see cref="BanEnforcementService"/>. Every dependency is an NSubstitute double
/// except <see cref="HubConnectionRegistry"/>, a real instance wired to substituted
/// collaborators — it isn't interface-shaped, so its real behavior is exercised directly rather
/// than mocked. Cache invalidation is verified against a real <see cref="FusionCache"/> instance
/// (tag-based removal isn't meaningfully observable through a bare substitute).
/// </summary>
public class BanEnforcementServiceTests
{
	private static IConnectionService.ConnectionData MakeConnection(
		long handle, string? accountId, string ip, string? host = null)
	{
		var metadata = new ConcurrentDictionary<string, string>();
		if (accountId is not null)
		{
			metadata["AccountId"] = accountId;
		}
		metadata["InternetProtocolAddress"] = ip;
		if (host is not null)
		{
			metadata["HostName"] = host;
		}

		return new IConnectionService.ConnectionData(
			handle,
			null,
			IConnectionService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			metadata);
	}

	private static (
		BanEnforcementService Service,
		IAccountSessionStore Sessions,
		IFusionCache Cache,
		IConnectionService Connections,
		IMessageBus Bus,
		HubConnectionRegistry Registry)
		Build(IEnumerable<IConnectionService.ConnectionData>? liveConnections = null)
	{
		var sessions = Substitute.For<IAccountSessionStore>();
		var cache = Substitute.For<IFusionCache>();
		var connections = Substitute.For<IConnectionService>();
		connections.GetAll().Returns((liveConnections ?? []).ToAsyncEnumerable());
		var bus = Substitute.For<IMessageBus>();
		var registry = new HubConnectionRegistry();

		var svc = new BanEnforcementService(sessions, cache, connections, bus, registry,
			NullLogger<BanEnforcementService>.Instance);

		return (svc, sessions, cache, connections, bus, registry);
	}

	[Test]
	public async Task EnforceAccountBanAsync_RevokesAllSessionsForAccount()
	{
		var (svc, sessions, _, _, _, _) = Build();

		await svc.EnforceAccountBanAsync("accounts/1");

		await sessions.Received(1).RevokeAllForAccountAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceAccountBanAsync_InvalidatesCachedClaims()
	{
		var (svc, _, cache, _, _, _) = Build();

		await svc.EnforceAccountBanAsync("accounts/1");

		// BanEnforcementService no longer depends on AccountClaimsService directly; it invalidates
		// the same cache tag AccountClaimsService's cached role/scope entries are tagged with, via
		// IFusionCache.RemoveByTagAsync — the shared AccountCacheTag() is the single source of truth
		// for that tag, so both classes always agree on it.
		await cache.Received(1).RemoveByTagAsync(AccountClaimsService.AccountCacheTag("accounts/1"));
	}

	[Test]
	public async Task EnforceAccountBanAsync_PublishesDisconnectForEachMatchingHandle_AndNotForOthers()
	{
		var matching1 = MakeConnection(101, "accounts/1", "1.1.1.1");
		var matching2 = MakeConnection(102, "accounts/1", "2.2.2.2");
		var other = MakeConnection(103, "accounts/2", "3.3.3.3");
		var (svc, _, _, _, bus, _) = Build([matching1, matching2, other]);

		await svc.EnforceAccountBanAsync("accounts/1");

		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 101), Arg.Any<CancellationToken>());
		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 102), Arg.Any<CancellationToken>());
		await bus.DidNotReceive().Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 103), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceAccountBanAsync_AbortsSignalRConnectionsForAccountOnly()
	{
		var (svc, _, _, _, _, registry) = Build();
		var abortedA = false;
		var abortedB = false;
		registry.Add("conn-a", "accounts/1", "9.9.9.9", () => abortedA = true);
		registry.Add("conn-b", "accounts/2", "9.9.9.9", () => abortedB = true);

		await svc.EnforceAccountBanAsync("accounts/1");

		await Assert.That(abortedA).IsTrue();
		await Assert.That(abortedB).IsFalse();
	}

	[Test]
	public async Task EnforceAccountBanAsync_OnePublishThrows_OtherHandleStillPublishedAndAbortAndRevokeStillRun()
	{
		var matching1 = MakeConnection(101, "accounts/1", "1.1.1.1");
		var matching2 = MakeConnection(102, "accounts/1", "2.2.2.2");
		var (svc, sessions, _, _, bus, registry) = Build([matching1, matching2]);
		var aborted = false;
		registry.Add("conn-a", "accounts/1", "9.9.9.9", () => aborted = true);

		bus.Publish(Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 101), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("simulated publish failure")));

		await svc.EnforceAccountBanAsync("accounts/1");

		// The handle whose publish throws still gets attempted...
		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 101), Arg.Any<CancellationToken>());
		// ...but the failure does not stop the other matched handle from being published.
		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 102), Arg.Any<CancellationToken>());
		// Nor does it stop the SignalR abort or the session revoke fan-outs.
		await Assert.That(aborted).IsTrue();
		await sessions.Received(1).RevokeAllForAccountAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceAccountBanAsync_RevokeSessionsThrows_PublishAndAbortStillRun()
	{
		var matching = MakeConnection(111, "accounts/1", "1.1.1.1");
		var (svc, sessions, _, _, bus, registry) = Build([matching]);
		var aborted = false;
		registry.Add("conn-a", "accounts/1", "9.9.9.9", () => aborted = true);

		sessions.RevokeAllForAccountAsync("accounts/1", Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("simulated session-store failure")));

		await svc.EnforceAccountBanAsync("accounts/1");

		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 111), Arg.Any<CancellationToken>());
		await Assert.That(aborted).IsTrue();
	}

	[Test]
	public async Task EnforceHostRuleAsync_ExactMatch_RevokesDisconnectsAndAborts()
	{
		var matching = MakeConnection(201, "accounts/1", "10.0.0.5");
		var other = MakeConnection(202, "accounts/2", "10.0.0.6");
		var (svc, sessions, _, _, bus, registry) = Build([matching, other]);
		var abortedMatching = false;
		var abortedOther = false;
		registry.Add("conn-match", "accounts/1", "10.0.0.5", () => abortedMatching = true);
		registry.Add("conn-other", "accounts/2", "10.0.0.6", () => abortedOther = true);

		await svc.EnforceHostRuleAsync("10.0.0.5");

		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 201), Arg.Any<CancellationToken>());
		await bus.DidNotReceive().Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 202), Arg.Any<CancellationToken>());
		await sessions.Received().RevokeAllForIpAsync("10.0.0.5", Arg.Any<CancellationToken>());
		await Assert.That(abortedMatching).IsTrue();
		await Assert.That(abortedOther).IsFalse();
	}

	[Test]
	public async Task EnforceHostRuleAsync_GlobPattern_MatchesOnlyPatternedIps()
	{
		var matching = MakeConnection(301, "accounts/1", "10.0.0.42");
		var nonMatching = MakeConnection(302, "accounts/2", "10.0.1.42");
		var (svc, sessions, _, _, bus, _) = Build([matching, nonMatching]);

		await svc.EnforceHostRuleAsync("10.0.0.*");

		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 301), Arg.Any<CancellationToken>());
		await bus.DidNotReceive().Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 302), Arg.Any<CancellationToken>());
		await sessions.Received().RevokeAllForIpAsync("10.0.0.42", Arg.Any<CancellationToken>());
		await sessions.DidNotReceive().RevokeAllForIpAsync("10.0.1.42", Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceHostRuleAsync_NeverMatchesUnknownOriginBucket()
	{
		var unknownConn = MakeConnection(401, "accounts/1", "UNKNOWN");
		var (svc, sessions, _, _, bus, registry) = Build([unknownConn]);
		var abortedUnknown = false;
		registry.Add("conn-unknown", "accounts/1", "unknown", () => abortedUnknown = true);

		// A maximally-broad glob rule must still never sweep up the "unknown" sentinel bucket.
		await svc.EnforceHostRuleAsync("*");

		await bus.DidNotReceive().Publish(Arg.Any<DisconnectConnectionMessage>(), Arg.Any<CancellationToken>());
		await sessions.DidNotReceive().RevokeAllForIpAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		await Assert.That(abortedUnknown).IsFalse();
	}

	[Test]
	public async Task EnforceHostRuleAsync_LiteralUnknownPattern_NeverRevokesOrAbortsTheUnknownBucket()
	{
		// A session legitimately carries origin IP "unknown" when the client's remote address
		// couldn't be resolved (see AuthController.ClientIp()); an admin literally typing "unknown"
		// as a host-rule pattern must not be able to sweep every such session/connection.
		var (svc, sessions, _, _, bus, registry) = Build();
		var abortedUnknown = false;
		registry.Add("conn-unknown", "accounts/1", "unknown", () => abortedUnknown = true);

		await svc.EnforceHostRuleAsync("unknown");

		await bus.DidNotReceive().Publish(Arg.Any<DisconnectConnectionMessage>(), Arg.Any<CancellationToken>());
		await sessions.DidNotReceive().RevokeAllForIpAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		await Assert.That(abortedUnknown).IsFalse();
	}

	[Test]
	public async Task EnforceHostRuleAsync_HostNameMatchesButIpIsUnresolved_NeverRevokesOrAbortsTheUnknownBucket()
	{
		// Edge case: a connection's HostName field matches the pattern, but its IP field is still
		// the "UNKNOWN" sentinel (unresolved). The service must not fall back to treating "UNKNOWN"
		// as a concrete matched IP for the session-revoke/registry-abort fan-outs.
		var connection = MakeConnection(501, "accounts/1", "UNKNOWN", host: "evil.example.com");
		var (svc, sessions, _, _, bus, registry) = Build([connection]);
		var abortedUnknown = false;
		registry.Add("conn-unknown", "accounts/1", "unknown", () => abortedUnknown = true);

		await svc.EnforceHostRuleAsync("evil.example.com");

		// The game handle itself is still disconnected (it matched by hostname)...
		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 501), Arg.Any<CancellationToken>());
		// ...but the "unknown" IP bucket is never touched by the session/registry fan-outs.
		await sessions.DidNotReceive().RevokeAllForIpAsync(
			Arg.Is<string>(ip => string.Equals(ip, "unknown", StringComparison.OrdinalIgnoreCase)),
			Arg.Any<CancellationToken>());
		await Assert.That(abortedUnknown).IsFalse();
	}
}
