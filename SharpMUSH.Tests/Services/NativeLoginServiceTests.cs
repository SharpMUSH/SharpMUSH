using Mediator;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class NativeLoginServiceTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class Harness : IDisposable
	{
		public readonly Clock Time = new();
		public readonly IAccountSessionStore Sessions = Substitute.For<IAccountSessionStore>();
		public readonly IMediator Mediator = Substitute.For<IMediator>();
		public readonly INativeLoginPolicy Policy = Substitute.For<INativeLoginPolicy>();
		public readonly AccountCredentialGate Gate = new();
		public readonly RecentAccountProofStore Proofs;
		public readonly NativeLoginSecretStore Store;
		public readonly NativeLoginService Service;
		public NativeCharacterAuthority Authority = new("account", new DBRef(10, 1000), 7, 3);
		public bool Live = true;

		public Harness()
		{
			Proofs = new(Time);
			Store = new(Time);
			Sessions.InspectAsync("session", Arg.Any<CancellationToken>()).Returns(_ =>
				Task.FromResult<Found<IAccountSessionStore.SessionIdentity>>(Live ? new IAccountSessionStore.SessionIdentity("account", null, null) : new NotFound()));
			Sessions.InspectDigestAsync(AccountSessionDigest.Compute("session"), Arg.Any<CancellationToken>()).Returns(_ =>
				Task.FromResult<Found<IAccountSessionStore.SessionIdentity>>(Live ? new IAccountSessionStore.SessionIdentity("account", null, null) : new NotFound()));
			Mediator.Send(Arg.Any<GetNativeCharacterAuthorityQuery>(), Arg.Any<CancellationToken>())
				.Returns(_ => new ValueTask<Found<NativeCharacterAuthority>>(Authority));
			Policy.CanConnectAsync(Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
			Service = new(Store, Sessions, Proofs, Mediator, Gate, Policy, Substitute.For<IPasswordService>(), Substitute.For<IValidateService>(), Time);
		}

		public string Grant(long epoch = 7) => Proofs.Issue("account", "session", AccountSecurityOperation.NativeLogin,
			new AccountProofAuthority("password", null, epoch)).Expect<RecentAccountProof>().Grant;
		public async Task<NativeLoginSecretMint> Mint() => (await Service.MintAsync("account", "session", new DBRef(10, 1000), "203.0.113.1", Grant())).Expect<NativeLoginSecretMint>();
		public void Dispose() => Store.Dispose();
	}

	[Test]
	public async Task MintRequiresLiveMatchingSessionDedicatedProofAndCurrentCredentialEpoch()
	{
		using var h = new Harness();
		await Assert.That(await h.Service.MintAsync("account", "wrong-session", new DBRef(10, 1000), "ip", h.Grant()) is Error<string>).IsTrue();
		await Assert.That(await h.Service.MintAsync("account", "session", new DBRef(10, 1000), "ip", h.Grant(6)) is Error<string>).IsTrue();
		await Assert.That(await h.Service.MintAsync("account", "session", new DBRef(10, 1000), "ip", null) is Error<string>).IsTrue();
		var grant = h.Grant();
		(await h.Service.MintAsync("account", "session", new DBRef(10, 1000), "ip", grant)).Expect<NativeLoginSecretMint>();
		await Assert.That(await h.Service.MintAsync("account", "session", new DBRef(10, 1000), "ip", grant) is Error<string>).IsTrue();
	}

	[Test]
	public async Task PolicyTimeExpiryIsCheckedAgainBeforeBindAndConsumedSecretNeverReplays()
	{
		using var h = new Harness();
		var mint = await h.Mint();
		var bound = false;
		var result = await h.Service.RedeemAsync(mint.Secret, "different-ip", "connection",
			_ => { h.Time.Now += TimeSpan.FromMinutes(2); return Task.FromResult(true); },
			(_, _) => { bound = true; return Task.FromResult("bound"); });
		await Assert.That(result is NotFound).IsTrue();
		await Assert.That(bound).IsFalse();
		await Assert.That(await h.Service.RedeemAsync(mint.Secret, "ip", "connection", _ => Task.FromResult(true), (_, _) => Task.FromResult("bound")) is NotFound).IsTrue();
	}

	[Test]
	public async Task SessionRevocationAndCredentialEpochChangesDuringPolicyPreventBinding()
	{
		using var h = new Harness();
		var first = await h.Mint();
		var bound = false;
		await h.Service.RedeemAsync(first.Secret, "ip", "first", _ => { h.Live = false; return Task.FromResult(true); }, (_, _) => { bound = true; return Task.FromResult("bound"); });
		await Assert.That(bound).IsFalse();
		h.Live = true;
		var second = await h.Mint();
		await h.Service.RedeemAsync(second.Secret, "ip", "second", _ => { h.Authority = h.Authority with { PasswordCredentialEpoch = 4 }; return Task.FromResult(true); }, (_, _) => { bound = true; return Task.FromResult("bound"); });
		await Assert.That(bound).IsFalse();
	}

	[Test]
	public async Task IndependentClientsBindOnceFromDifferentSourceAddresses()
	{
		using var h = new Harness();
		var first = await h.Mint();
		var second = await h.Mint();
		await Assert.That(first.Secret).IsNotEqualTo(second.Secret);
		(await h.Service.RedeemAsync(first.Secret, "new-source-ip", "1", _ => Task.FromResult(true), (character, _) => Task.FromResult(character))).Expect<DBRef>();
		(await h.Service.RedeemAsync(second.Secret, "another-source-ip", "2", _ => Task.FromResult(true), (character, _) => Task.FromResult(character))).Expect<DBRef>();
		await Assert.That(h.Store.List("account")).IsEmpty();
	}
	[Test]
	public async Task RecognizedTokensRejectedByAuthorityCountTowardConnectionAndIpFailureLimits()
	{
		using var h = new Harness();
		h.Live = false;
		for (var i = 0; i < 10; i++)
		{
			var stale = h.Store.Issue(new("stale-" + i, new DBRef(10, 1000), AccountSessionDigest.Compute("session"), 7, 3), "mint-" + i).Expect<NativeLoginSecretMint>();
			await Assert.That(await h.Service.RedeemAsync(stale.Secret, "failure-source", "failure-connection", _ => Task.FromResult(true), (_, _) => Task.FromResult(true)) is NotFound).IsTrue();
		}
		h.Live = true;
		var valid = await h.Mint();
		await Assert.That(await h.Service.RedeemAsync(valid.Secret, "failure-source", "failure-connection", _ => Task.FromResult(true), (_, _) => Task.FromResult(true)) is NotFound).IsTrue();
		h.Time.Now += TimeSpan.FromSeconds(61);
		await Assert.That((await h.Service.RedeemAsync(valid.Secret, "failure-source", "failure-connection", _ => Task.FromResult(true), (_, _) => Task.FromResult(true))).Expect<bool>()).IsTrue();
	}

	[Test]
	public async Task LoginPolicyClosingWhilePublicationIsBlockedPreventsTheActualNativeBinding()
	{
		using var h = new Harness();
		var mint = await h.Mint();
		var connections = new ConnectionService(Substitute.For<IPublisher>());
		await connections.Register(1, "203.0.113.9", "client.example.test", "telnet", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8);
		var publication = connections.Get(1)!.Binding.PublicationGate;
		await publication.WaitAsync();
		var bindingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<Found<bool>>? redeeming = null;
		try
		{
			redeeming = h.Service.RedeemAsync(mint.Secret, "203.0.113.9", "1", _ => Task.FromResult(true), async (character, finalAuthority) =>
			{
				bindingEntered.SetResult();
				return await connections.BindAuthorizedAsync(1, character, finalAuthority);
			});
			await bindingEntered.Task;
			h.Policy.CanConnectAsync(Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
		}
		finally { publication.Release(); }
		await Assert.That((await redeeming!).Expect<bool>()).IsFalse();
		await Assert.That(connections.Get(1)!.Ref).IsNull();
	}

}
