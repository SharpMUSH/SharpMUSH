using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public class RecentAccountProofStoreTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Test]
	public async Task ProofIsBoundToAccountSessionAndOperationAndConsumedExactlyOnce()
	{
		var store = new RecentAccountProofStore(new Clock());
		var proof = store.Issue("account", "session", AccountSecurityOperation.LinkIdentity,
			new AccountProofAuthority("password")).Expect<RecentAccountProof>();
		store.Consume(proof.Grant, "other", "session", AccountSecurityOperation.LinkIdentity).Expect<NotFound>();
		store.Consume(proof.Grant, "account", "other", AccountSecurityOperation.LinkIdentity).Expect<NotFound>();
		store.Consume(proof.Grant, "account", "session", AccountSecurityOperation.NativeLogin).Expect<NotFound>();
		var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
			store.Consume(proof.Grant, "account", "session", AccountSecurityOperation.LinkIdentity) is AccountProofAuthority)));
		await Assert.That(results.Count(success => success)).IsEqualTo(1);
	}

	[Test]
	public async Task ProofExpiresAtFiveMinutesWithoutSliding()
	{
		var clock = new Clock();
		var store = new RecentAccountProofStore(clock);
		var proof = store.Issue("account", "session", AccountSecurityOperation.NativeLogin,
			new AccountProofAuthority("passkey", "key-id")).Expect<RecentAccountProof>();
		await Assert.That(proof.ExpiresAt).IsEqualTo(clock.Now.AddMinutes(5));
		clock.Now = proof.ExpiresAt;
		store.Consume(proof.Grant, "account", "session", AccountSecurityOperation.NativeLogin).Expect<NotFound>();
	}

	[Test]
	public async Task RevocationInvalidatesOnlyTheAffectedAccountOrSession()
	{
		var store = new RecentAccountProofStore(new Clock());
		RecentAccountProof Issue(string account, string session) => store.Issue(account, session,
			AccountSecurityOperation.NativeLogin, new AccountProofAuthority("password")).Expect<RecentAccountProof>();
		var first = Issue("one", "first");
		var second = Issue("one", "second");
		var third = Issue("two", "third");
		store.RevokeSession("first");
		store.Consume(first.Grant, "one", "first", AccountSecurityOperation.NativeLogin).Expect<NotFound>();
		store.RevokeAccount("one");
		store.Consume(second.Grant, "one", "second", AccountSecurityOperation.NativeLogin).Expect<NotFound>();
		store.Consume(third.Grant, "two", "third", AccountSecurityOperation.NativeLogin).Expect<AccountProofAuthority>();
		await Assert.That(store.Count).IsEqualTo(0);
	}

	[Test]
	public async Task CapacityIsAtomicAndExpiredGrantsAreCleanedBeforeRefusing()
	{
		var clock = new Clock();
		var store = new RecentAccountProofStore(clock, capacity: 2);
		var attempts = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => store.Issue(
			"account", "session", AccountSecurityOperation.NativeLogin, new AccountProofAuthority("password")) is RecentAccountProof)));
		await Assert.That(attempts.Count(success => success)).IsEqualTo(2);
		clock.Now += TimeSpan.FromMinutes(5);
		store.Issue("account", "session", AccountSecurityOperation.NativeLogin, new AccountProofAuthority("password")).Expect<RecentAccountProof>();
		await Assert.That(store.Count).IsEqualTo(1);
	}
}
