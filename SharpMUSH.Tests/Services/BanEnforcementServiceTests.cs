using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit tests for <see cref="BanEnforcementService"/>. Every dependency is an NSubstitute double
/// except <see cref="HubConnectionRegistry"/>, a real instance wired to substituted
/// collaborators — it isn't interface-shaped, so its real behavior is exercised directly rather
/// than mocked. Cache invalidation is verified through the <see cref="IAccountClaimsInvalidator"/>
/// seam the service now calls, rather than against the FusionCache tag API directly.
/// </summary>
public class BanEnforcementServiceTests
{
	private static IConnectionService.ConnectionData MakeConnection(
		long handle, string? accountId, string ip, string? host = null, DBRef? characterRef = null)
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
			characterRef,
			IConnectionService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			metadata);
	}

	private static (
		BanEnforcementService Service,
		IAccountSessionStore Sessions,
		IAccountClaimsInvalidator ClaimsInvalidator,
		IConnectionService Connections,
		IMessageBus Bus,
		HubConnectionRegistry Registry,
		ISharpDatabase Database)
		Build(IEnumerable<IConnectionService.ConnectionData>? liveConnections = null,
			IReadOnlyList<SharpPlayer>? linkedCharacters = null,
			IAccountSessionStore? sessionStore = null)
	{
		var sessions = sessionStore ?? Substitute.For<IAccountSessionStore>();
		if (sessionStore is null)
		{
			// A store with no sessions in it, so tests that say nothing about stored origins keep
			// exercising only the live-connection path.
			sessions.GetKnownOriginIpsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string[]>([]));
		}
		var claimsInvalidator = Substitute.For<IAccountClaimsInvalidator>();
		var connections = Substitute.For<IConnectionService>();
		connections.GetAll().Returns((liveConnections ?? []).ToAsyncEnumerable());
		var bus = Substitute.For<IMessageBus>();
		var registry = new HubConnectionRegistry();
		var database = Substitute.For<ISharpDatabase>();
		database.GetCharactersForAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(linkedCharacters ?? (IReadOnlyList<SharpPlayer>)[]));

		var svc = new BanEnforcementService(sessions, claimsInvalidator, connections, bus, registry, database,
			NullLogger<BanEnforcementService>.Instance);

		return (svc, sessions, claimsInvalidator, connections, bus, registry, database);
	}

	[Test]
	public async Task EnforceAccountBanAsync_RevokesAllSessionsForAccount()
	{
		var (svc, sessions, _, _, _, _, _) = Build();

		await svc.EnforceAccountBanAsync("accounts/1");

		await sessions.Received(1).RevokeAllForAccountAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceAccountBanAsync_InvalidatesCachedClaims()
	{
		var (svc, _, claimsInvalidator, _, _, _, _) = Build();

		await svc.EnforceAccountBanAsync("accounts/1");

		// Ban enforcement and character link/unlink drop the same cached claims, so they share one
		// invalidator rather than each reaching for the cache tag themselves.
		await claimsInvalidator.Received(1).InvalidateAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceAccountBanAsync_PublishesDisconnectForEachMatchingHandle_AndNotForOthers()
	{
		var matching1 = MakeConnection(101, "accounts/1", "1.1.1.1");
		var matching2 = MakeConnection(102, "accounts/1", "2.2.2.2");
		var other = MakeConnection(103, "accounts/2", "3.3.3.3");
		var (svc, _, _, _, bus, _, _) = Build([matching1, matching2, other]);

		await svc.EnforceAccountBanAsync("accounts/1");

		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 101), Arg.Any<CancellationToken>());
		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 102), Arg.Any<CancellationToken>());
		await bus.DidNotReceive().Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 103), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceAccountBanAsync_PublishesDisconnectForCharacterBoundConnection_NoAccountIdMetadataNeeded()
	{
		// The real game connection case (telnet `connect <char> <pw>`, the web OTT websocket
		// terminal): ConnectionService.Bind sets connection.Ref to the character's DBRef and never
		// touches Metadata["AccountId"] (that metadata is only ever set by the telnet account-mode
		// LOGIN/REGISTER commands' ConnectionService.BindAccount, which today is unreachable). So a
		// banned account's live character connection must still be disconnected purely by resolving
		// the account's linked characters and matching on connection.Ref.
		var factory = new TestObjectFactory();
		var character = factory.CreatePlayer(555, "BannedAccountsChar").AsPlayer;
		var characterBoundConnection = MakeConnection(
			701, accountId: null, ip: "4.4.4.4", characterRef: new DBRef(character.Object.Key, character.Object.CreationTime));
		var unrelatedConnection = MakeConnection(702, accountId: null, ip: "5.5.5.5", characterRef: new DBRef(999));

		var (svc, _, _, _, bus, _, _) = Build(
			[characterBoundConnection, unrelatedConnection],
			linkedCharacters: [character]);

		await svc.EnforceAccountBanAsync("accounts/1");

		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 701), Arg.Any<CancellationToken>());
		await bus.DidNotReceive().Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 702), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceAccountBanAsync_AbortsSignalRConnectionsForAccountOnly()
	{
		var (svc, _, _, _, _, registry, _) = Build();
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
		var (svc, sessions, _, _, bus, registry, _) = Build([matching1, matching2]);
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
		var (svc, sessions, _, _, bus, registry, _) = Build([matching]);
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
		var (svc, sessions, _, _, bus, registry, _) = Build([matching, other]);
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
		var (svc, sessions, _, _, bus, _, _) = Build([matching, nonMatching]);

		await svc.EnforceHostRuleAsync("10.0.0.*");

		await bus.Received(1).Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 301), Arg.Any<CancellationToken>());
		await bus.DidNotReceive().Publish(
			Arg.Is<DisconnectConnectionMessage>(m => m.Handle == 302), Arg.Any<CancellationToken>());
		await sessions.Received().RevokeAllForIpAsync("10.0.0.42", Arg.Any<CancellationToken>());
		await sessions.DidNotReceive().RevokeAllForIpAsync("10.0.1.42", Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// The case ban enforcement could not reach: a session that exists with nobody connected on it.
	/// Reading the live connection list finds nothing, so before stored origins were consulted a CIDR
	/// rule revoked nothing at all and the credential outlived the ban.
	/// </summary>
	[Test]
	public async Task EnforceHostRuleAsync_CidrPattern_RevokesStoredSessionsWithNoLiveConnection()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		sessionStore.GetKnownOriginIpsAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string[]>(["10.0.0.5", "10.255.255.254", "198.51.100.7"]));
		var (svc, sessions, _, _, _, _, _) = Build(sessionStore: sessionStore);

		await svc.EnforceHostRuleAsync("10.0.0.0/8");

		await sessions.Received().RevokeAllForIpAsync("10.0.0.5", Arg.Any<CancellationToken>());
		await sessions.Received().RevokeAllForIpAsync("10.255.255.254", Arg.Any<CancellationToken>());
		await sessions.DidNotReceive().RevokeAllForIpAsync("198.51.100.7", Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceHostRuleAsync_GlobPattern_RevokesStoredSessionsWithNoLiveConnection()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		sessionStore.GetKnownOriginIpsAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string[]>(["10.0.0.42", "10.0.1.42"]));
		var (svc, sessions, _, _, _, _, _) = Build(sessionStore: sessionStore);

		await svc.EnforceHostRuleAsync("10.0.0.*");

		await sessions.Received().RevokeAllForIpAsync("10.0.0.42", Arg.Any<CancellationToken>());
		await sessions.DidNotReceive().RevokeAllForIpAsync("10.0.1.42", Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// The stored-origin sweep must respect the same "unknown" sentinel rule the live-connection sweep
	/// does: a session whose origin could not be resolved is not what an admin means by <c>*</c>.
	/// </summary>
	[Test]
	public async Task EnforceHostRuleAsync_StoredOrigins_NeverIncludeTheUnknownBucket()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		sessionStore.GetKnownOriginIpsAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string[]>(["unknown"]));
		var (svc, sessions, _, _, _, _, _) = Build(sessionStore: sessionStore);

		await svc.EnforceHostRuleAsync("*");

		await sessions.DidNotReceive().RevokeAllForIpAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task EnforceHostRuleAsync_NeverMatchesUnknownOriginBucket()
	{
		var unknownConn = MakeConnection(401, "accounts/1", "UNKNOWN");
		var (svc, sessions, _, _, bus, registry, _) = Build([unknownConn]);
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
		var (svc, sessions, _, _, bus, registry, _) = Build();
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
		var (svc, sessions, _, _, bus, registry, _) = Build([connection]);
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

	/// <summary>
	/// The ban-evasion window this whole conditional-renewal change exists to close. A ban revokes an
	/// account's sessions, but session renewal is read-then-write and the two halves are not atomic: a ban
	/// landing between them used to be undone by the insert branch of the upsert that wrote the slid expiry
	/// back, and the banned account kept a working token for up to another full TTL.
	/// </summary>
	/// <remarks>
	/// Run against a real <see cref="DatabaseAccountSessionStore"/> rather than the substituted session
	/// store the rest of this class uses, because the defect lives in how the store sequences its two
	/// database calls — a substitute would assert nothing about it.
	/// </remarks>
	[Test]
	public async Task EnforceAccountBanAsync_CommittingBetweenARenewalsReadAndItsWrite_LeavesTheSessionRevoked()
	{
		var spy = new SessionSpy();
		var store = new DatabaseAccountSessionStore(spy.Database);
		var (svc, _, _, _, _, _, _) = Build(sessionStore: store);

		var token = await store.CreateTokenAsync("accounts/1", TimeSpan.FromMinutes(15), "203.0.113.66");
		spy.AgeBy(TimeSpan.FromMinutes(1)); // past the coalescing threshold, so this validation really writes

		spy.AfterNextRead = async () => await svc.EnforceAccountBanAsync("accounts/1");

		var identity = await store.ValidateAsync(token);

		await Assert.That(spy.Stored).IsNull()
			.Because("a ban that commits mid-validation must not be undone by the renewal write");
		await Assert.That(identity).IsNull()
			.Because("a banned account's token must not authenticate the request that was already holding it");
	}
}
