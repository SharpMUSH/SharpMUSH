using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit cover for the two halves of the session-renewal contract: the write-coalescing that keeps a burst
/// of validations from writing at all (asserted by counting writes rather than by observing latency), and
/// the rule that a renewal may never insert — so a revocation landing between the read and the write is
/// not undone by it.
/// </summary>
public class DatabaseAccountSessionStoreTests
{
	[Test]
	public async Task Validate_RepeatedlyWithinTheThreshold_WritesNothing()
	{
		var spy = new SessionSpy();
		var store = new DatabaseAccountSessionStore(spy.Database);
		var token = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15), "203.0.113.1");
		await Assert.That(spy.Upserts).IsEqualTo(1); // minting the token

		for (var i = 0; i < 50; i++)
			await Assert.That((await store.ValidateAsync(token))?.AccountId).IsEqualTo("acct-1");

		await Assert.That(spy.Upserts).IsEqualTo(1)
			.Because("a burst of validations inside the threshold must not re-persist the same expiry");
		await Assert.That(spy.Touches).IsEqualTo(0);
	}

	[Test]
	public async Task Validate_AfterTheThresholdElapses_PersistsTheSlide()
	{
		var spy = new SessionSpy();
		var store = new DatabaseAccountSessionStore(spy.Database);
		var token = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15), "203.0.113.1");
		var expiryAtMint = spy.Stored!.ExpiryUnixMs;

		// The threshold is a fraction of TtlMs; a minute is comfortably past it for a 15-minute session.
		spy.AgeBy(TimeSpan.FromMinutes(1));

		await Assert.That((await store.ValidateAsync(token))?.AccountId).IsEqualTo("acct-1");
		await Assert.That(spy.Touches).IsEqualTo(1);
		await Assert.That(spy.Upserts).IsEqualTo(1)
			.Because("only minting a token may insert one; renewal is a conditional update");
		await Assert.That(spy.Stored!.ExpiryUnixMs).IsGreaterThanOrEqualTo(expiryAtMint);
	}

	[Test]
	public async Task Validate_WhenExpired_DeletesAndReturnsNull()
	{
		var spy = new SessionSpy();
		var store = new DatabaseAccountSessionStore(spy.Database);
		var token = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15), "203.0.113.1");

		spy.AgeBy(TimeSpan.FromMinutes(20));

		await Assert.That(await store.ValidateAsync(token)).IsNull();
		await Assert.That(spy.Deletes).IsEqualTo(1);
		await Assert.That(spy.Upserts).IsEqualTo(1)
			.Because("an expired session is removed, never slid");
		await Assert.That(spy.Touches).IsEqualTo(0);
	}

	/// <summary>
	/// The resurrection race. Renewal is read-then-write and the two are not atomic, so a logout committing
	/// in between used to be undone by the insert branch of the upsert that wrote the slid expiry back —
	/// leaving a token the user had explicitly given up alive for up to another full TTL.
	/// </summary>
	[Test]
	public async Task Validate_WhenALogoutCommitsBetweenTheReadAndTheRenewal_DoesNotResurrectTheSession()
	{
		var spy = new SessionSpy();
		var store = new DatabaseAccountSessionStore(spy.Database);
		var token = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15), "203.0.113.1");
		spy.AgeBy(TimeSpan.FromMinutes(1)); // past the coalescing threshold, so this validation really writes

		spy.AfterNextRead = async () => await store.RevokeAsync(token);

		var identity = await store.ValidateAsync(token);

		await Assert.That(spy.Stored).IsNull()
			.Because("a revocation that commits mid-validation must not be undone by the renewal write");
		await Assert.That(identity).IsNull()
			.Because("a request holding a token that has already been revoked must not authenticate");
	}
}
