using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.Services;

public class SessionResumeConsumerTests
{
	private sealed class Harness
	{
		public readonly IConnectionStateStore Store = Substitute.For<IConnectionStateStore>();
		public readonly IConnectionService Connections = Substitute.For<IConnectionService>();
		public readonly IAccountService Accounts = Substitute.For<IAccountService>();
		public readonly IMessageBus Bus = Substitute.For<IMessageBus>();
		public readonly ConnectionStateData State = new()
		{
			Handle = 42, State = "AccountMode", IpAddress = "127.0.0.1", Hostname = "old",
			ConnectionType = "websocket", ConnectedAt = DateTimeOffset.UtcNow,
			Metadata = new() { ["SessionId"] = "session", ["AccountId"] = "account", ["SSL"] = "1" }
		};
		public readonly SessionResumeRequestMessage Request = new(Guid.NewGuid(), 42, "session", "127.0.0.2", "new", true);
		public readonly IConnectionService.ConnectionData Current;
		public readonly SessionResumeConsumer Consumer;

		public Harness(Microsoft.Extensions.Hosting.IHostApplicationLifetime? lifetime = null)
		{
			Current = new(42, null, IConnectionService.ConnectionState.AccountMode,
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
				new ConcurrentDictionary<string, string>(State.Metadata) { ["ConnectionType"] = "websocket" });
			Store.GetConnectionAsync(42, Arg.Any<CancellationToken>()).Returns(State);
			Connections.Get(42).Returns(Current);
			Store.TryUpdateTransportAsync(42, "session", null, "AccountMode", "127.0.0.2", "new", true, Arg.Any<CancellationToken>()).Returns(true);
			Accounts.GetByIdAsync("account", Arg.Any<CancellationToken>()).Returns(new SharpAccount
			{
				Id = "account", Username = "user", PasswordHash = "unused"
			});
			var config = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			var baseline = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");
			config.CurrentValue.Returns(baseline with { Net = baseline.Net with { Logins = true } });
			Consumer = new(Store, Connections, Accounts, Substitute.For<IMediator>(), config, Bus,
				NullLogger<SessionResumeConsumer>.Instance, lifetime);
		}

		public Task AssertResponse(bool accepted) => Bus.Received(1).Publish(
			Arg.Is<SessionResumeResponseMessage>(m => m.Accepted == accepted && m.RequestId == Request.RequestId
				&& m.Handle == 42 && m.SessionId == "session"), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task StartupReturnsRetryableWithoutInspectingUnreconciledBindings()
	{
		var lifetime = Substitute.For<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
		var h = new Harness(lifetime);
		h.Store.ClearReceivedCalls();
		await h.Consumer.HandleAsync(h.Request);
		await h.Store.DidNotReceive().GetConnectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
		await h.Bus.Received(1).Publish(Arg.Is<SessionResumeResponseMessage>(response =>
			!response.Accepted && response.Retryable), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task InfrastructureFailureReturnsRetryableDenial()
	{
		var h = new Harness();
		h.Store.GetConnectionAsync(42, Arg.Any<CancellationToken>())
			.Returns(Task.FromException<ConnectionStateData?>(new IOException("broker unavailable")));
		await h.Consumer.HandleAsync(h.Request);
		await h.Bus.Received(1).Publish(Arg.Is<SessionResumeResponseMessage>(response =>
			!response.Accepted && response.Retryable && response.RequestId == h.Request.RequestId),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task AcceptedAccountResumePersistsTransportBeforeAcknowledgingWithoutRebinding()
	{
		var h = new Harness();
		var persisted = false;
		h.Store.TryUpdateTransportAsync(42, "session", null, "AccountMode", "127.0.0.2", "new", true, Arg.Any<CancellationToken>())
			.Returns(call => { persisted = true; return Task.FromResult(true); });
		h.Bus.Publish(Arg.Any<SessionResumeResponseMessage>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				if (call.Arg<SessionResumeResponseMessage>().Accepted && !persisted)
					throw new InvalidOperationException("Acknowledged before persistence");
				return Task.CompletedTask;
			});
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(true);
		await Assert.That(h.Current.InternetProtocolAddress).IsEqualTo("127.0.0.2");
		await h.Connections.DidNotReceive().BindAccount(Arg.Any<long>(), Arg.Any<string>());
		await h.Connections.DidNotReceive().Bind(Arg.Any<long>(), Arg.Any<DBRef>(), Arg.Any<bool>());
	}

	[Test]
	[Arguments("SessionId", "recycled")]
	[Arguments("ResumeExpiresAt", "1")]
	[Arguments("ResumeExpiresAt", "-1")]
	[Arguments("ResumeExpiresAt", "invalid")]
	[Arguments("AccountId", "different-account")]
	[Arguments("ResumeRevoked", "1")]
	public async Task StaleOrMismatchedPersistedSessionIsRejected(string key, string value)
	{
		var h = new Harness();
		h.State.Metadata[key] = value;
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(false);
	}

	[Test]
	public async Task InMemoryRevocationRejectsResumeEvenBeforePersistenceCompletes()
	{
		var h = new Harness();
		h.Current.Metadata["ResumeRevoked"] = "1";
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(false);
	}

	[Test]
	public async Task RevocationWhilePersistingTransportRejectsAcknowledgment()
	{
		var h = new Harness();
		h.Store.TryUpdateTransportAsync(42, "session", null, "AccountMode", "127.0.0.2", "new", true, Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				h.Current.Metadata["ResumeRevoked"] = "1";
				return Task.FromResult(true);
			});
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(false);
	}

	[Test]
	public async Task ActiveSessionWithoutDetachedDeadlineCanResume()
	{
		var h = new Harness();
		h.State.Metadata["ResumeExpiresAt"] = "0";
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(true);
	}

	[Test]
	public async Task SecureSessionCannotResumeOverPlaintext()
	{
		var h = new Harness();
		await h.Consumer.HandleAsync(h.Request with { IsSecure = false });
		await h.AssertResponse(false);
	}

	[Test]
	public async Task DisabledAccountCannotResume()
	{
		var h = new Harness();
		h.Accounts.GetByIdAsync("account", Arg.Any<CancellationToken>()).Returns((SharpAccount?)null);
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(false);
	}

	[Test]
	public async Task MissingDurableSessionCannotResumeFromMemoryAlone()
	{
		var h = new Harness();
		h.Store.GetConnectionAsync(42, Arg.Any<CancellationToken>()).Returns((ConnectionStateData?)null);
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(false);
	}

	[Test]
	[Arguments("#12")]
	[Arguments("#12:999")]
	public async Task BareOrRecycledPlayerReferenceCannotResume(string persistedReference)
	{
		var h = new Harness();
		h.State.State = "LoggedIn";
		h.State.PlayerObjid = persistedReference;
		h.Connections.Get(42).Returns(h.Current with
		{
			State = IConnectionService.ConnectionState.LoggedIn,
			Ref = new DBRef(12, 1000)
		});
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(false);
	}

	[Test]
	public async Task RevocationBeforeConditionalWriteRejectsResume()
	{
		var h = new Harness();
		h.Store.TryUpdateTransportAsync(42, "session", null, "AccountMode", "127.0.0.2", "new", true, Arg.Any<CancellationToken>())
			.Returns(false);
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(false);
		await Assert.That(h.Current.InternetProtocolAddress).IsEqualTo("UNKNOWN");
	}

	[Test]
	public async Task InMemorySessionReplacementBeforeAcknowledgmentRejectsResume()
	{
		var h = new Harness();
		h.Store.TryUpdateTransportAsync(42, "session", null, "AccountMode", "127.0.0.2", "new", true, Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				h.Current.Metadata["SessionId"] = "replacement";
				return Task.FromResult(true);
			});
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(false);
	}

	[Test]
	public async Task PersistenceFailureFailsClosed()
	{
		var h = new Harness();
		h.Store.TryUpdateTransportAsync(42, "session", null, "AccountMode", "127.0.0.2", "new", true, Arg.Any<CancellationToken>())
			.Returns(Task.FromException<bool>(new IOException("store unavailable")));
		await h.Consumer.HandleAsync(h.Request);
		await h.AssertResponse(false);
	}
}
