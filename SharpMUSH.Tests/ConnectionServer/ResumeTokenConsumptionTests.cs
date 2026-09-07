using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class ResumeTokenConsumptionTests
{
	[Test]
	public async Task ConcurrentConsumersHaveExactlyOneWinner()
	{
		var store = new ResumeTokenService();
		var token = await store.MintAsync(42, "session");
		var results = await Task.WhenAll(Enumerable.Range(0, 32)
			.Select(_ => Task.Run(async () => await store.TryConsumeAsync(token))));
		await Assert.That(results.Count(result => result.Found)).IsEqualTo(1);
		await Assert.That(results.Single(result => result.Found).Handle).IsEqualTo(42L);
		await Assert.That((await store.TryResolveAsync(token)).Found).IsFalse();
		await Assert.That(token.Length).IsEqualTo(64);
	}

	[Test]
	public async Task ExpiredTokenCannotBeConsumedAtTheExpiryBoundary()
	{
		var now = DateTimeOffset.UtcNow;
		var store = new ResumeTokenService(() => now);
		var token = await store.MintAsync(42, "session");
		now += TimeSpan.FromSeconds(30);
		await Assert.That((await store.TryConsumeAsync(token)).Found).IsFalse();
	}

	[Test]
	public async Task RevocationRejectsEveryTokenForThatSession()
	{
		var store = new ResumeTokenService();
		var first = await store.MintAsync(42, "session");
		var second = await store.MintAsync(42, "session");
		var other = await store.MintAsync(43, "other");
		await store.RevokeSessionAsync("session");
		await Assert.That((await store.TryResolveAsync(first)).Found).IsFalse();
		await Assert.That((await store.TryConsumeAsync(second)).Found).IsFalse();
		await Assert.That((await store.TryConsumeAsync(other)).Found).IsTrue();
	}

	[Test]
	public async Task InspectionDoesNotSpendTokenButInvalidationDoes()
	{
		var store = new ResumeTokenService();
		var token = await store.MintAsync(42, "session");
		await Assert.That((await store.TryResolveAsync(token)).Found).IsTrue();
		await store.InvalidateAsync(token);
		await Assert.That((await store.TryConsumeAsync(token)).Found).IsFalse();
	}
}
