using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public class NativeLoginSecretStoreTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private static NativeLoginAuthority Authority(string account = "account/1", int character = 10, string session = "session")
		=> new(account, new DBRef(character, 1000), AccountSessionDigest.Compute(session), 1, 2);

	[Test]
	public async Task AtomicManyConsumerRedemptionHasExactlyOneWinner()
	{
		using var store = new NativeLoginSecretStore();
		var minted = store.Issue(Authority(), "203.0.113.1").Expect<NativeLoginSecretMint>();
		var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() => store.Consume(minted.Secret, "203.0.113.2", i.ToString()))));
		await Assert.That(results.Count(x => x is NativeLoginSecretLease)).IsEqualTo(1);
		await Assert.That(store.List("account/1")).IsEmpty();
	}

	[Test]
	public async Task ExpiryIsFixedAndEqualityBoundaryIsExpired()
	{
		var clock = new Clock();
		using var store = new NativeLoginSecretStore(clock);
		var minted = store.Issue(Authority(), "203.0.113.1").Expect<NativeLoginSecretMint>();
		await Assert.That(minted.ExpiresAt).IsEqualTo(clock.Now.AddMinutes(2));
		clock.Now += TimeSpan.FromMinutes(2);
		await Assert.That(store.Consume(minted.Secret, "203.0.113.2", "connection") is NotFound).IsTrue();
	}

	[Test]
	public async Task PurposeSeparationAndRestartInvalidateAuthority()
	{
		using var store = new NativeLoginSecretStore();
		var minted = store.Issue(Authority(), "203.0.113.1").Expect<NativeLoginSecretMint>();
		await Assert.That(minted.Secret.StartsWith("nc1_", StringComparison.Ordinal)).IsTrue();
		await Assert.That(minted.Secret.Length).IsEqualTo(47);
		await Assert.That(store.Consume(minted.Secret[4..], "203.0.113.2", "a") is NotFound).IsTrue();
		await Assert.That(store.Consume("nc1_" + Guid.NewGuid().ToString("N"), "203.0.113.2", "b") is NotFound).IsTrue();
		using var restarted = new NativeLoginSecretStore();
		await Assert.That(restarted.Consume(minted.Secret, "203.0.113.2", "c") is NotFound).IsTrue();
		await Assert.That(store.Consume(minted.Secret, "203.0.113.2", "d") is NativeLoginSecretLease).IsTrue();
	}

	[Test]
	public async Task RevocationIsBoundToAccountSessionAndFullCharacterIdentity()
	{
		using var store = new NativeLoginSecretStore();
		var a = store.Issue(Authority(session: "first"), "203.0.113.1").Expect<NativeLoginSecretMint>();
		var b = store.Issue(Authority(character: 11, session: "second"), "203.0.113.1").Expect<NativeLoginSecretMint>();
		await Assert.That(store.Revoke("other-account", a.Id)).IsFalse();
		store.RevokeCharacter(new DBRef(10, 999));
		await Assert.That(store.List("account/1").Count).IsEqualTo(2);
		store.RevokeSession("first");
		await Assert.That(store.Consume(a.Secret, "203.0.113.2", "a") is NotFound).IsTrue();
		await Assert.That(store.List("account/1").Single().Id).IsEqualTo(b.Id);
		store.RevokeAccount("account/1");
		await Assert.That(store.Consume(b.Secret, "203.0.113.2", "b") is NotFound).IsTrue();
	}

	[Test]
	public async Task MintLimitsDoNotEvictOtherPendingClientsAndExpiredEntriesAreCleaned()
	{
		var clock = new Clock();
		using var store = new NativeLoginSecretStore(clock);
		for (var i = 0; i < 5; i++) store.Issue(Authority(), "203.0.113.1").Expect<NativeLoginSecretMint>();
		await Assert.That(store.Issue(Authority(), "203.0.113.1") is Error<string>).IsTrue();
		clock.Now += TimeSpan.FromMinutes(1);
		for (var i = 0; i < 5; i++) store.Issue(Authority(), "203.0.113.1").Expect<NativeLoginSecretMint>();
		await Assert.That(store.List("account/1").Count).IsEqualTo(10);
		await Assert.That(store.Issue(Authority(), "203.0.113.3") is Error<string>).IsTrue();
		clock.Now += TimeSpan.FromMinutes(1);
		await Assert.That(store.List("account/1").Count).IsEqualTo(5);
	}

	[Test]
	public async Task SharedIpAndFailedRedemptionLimitsAreBoundedWithoutAccountLockout()
	{
		using var store = new NativeLoginSecretStore();
		for (var i = 0; i < 10; i++) store.Issue(Authority("account/" + i), "203.0.113.1").Expect<NativeLoginSecretMint>();
		await Assert.That(store.Issue(Authority("new-account"), "203.0.113.1") is Error<string>).IsTrue();
		var minted = store.Issue(Authority("separate"), "203.0.113.3").Expect<NativeLoginSecretMint>();
		for (var i = 0; i < 10; i++) store.Consume("nc1_invalid", "203.0.113.2", "attacker");
		await Assert.That(store.Consume(minted.Secret, "203.0.113.2", "different-connection") is NotFound).IsTrue();
		await Assert.That(store.Consume(minted.Secret, "203.0.113.4", "legitimate") is NativeLoginSecretLease).IsTrue();
	}
}
