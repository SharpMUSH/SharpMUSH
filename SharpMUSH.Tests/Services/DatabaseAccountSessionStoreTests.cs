using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit cover for the write-coalescing half of the session-concurrency fix. The exclusive transaction in
/// the ArangoDB provider stops concurrent slides from failing; this is what stops them from happening at
/// all, so it is asserted by counting writes rather than by observing latency.
/// </summary>
public class DatabaseAccountSessionStoreTests
{
	/// <summary>
	/// A substituted <see cref="ISharpDatabase"/> holding exactly one session, counting the writes the
	/// store issues against it.
	/// </summary>
	private sealed class SessionSpy
	{
		public ISharpDatabase Database { get; } = Substitute.For<ISharpDatabase>();
		public SharpSession? Stored { get; private set; }
		public int Upserts { get; private set; }
		public int Deletes { get; private set; }

		public SessionSpy()
		{
			Database.GetSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
				.Returns(_ => ValueTask.FromResult(Stored));
			Database.When(x => x.UpsertSessionAsync(Arg.Any<SharpSession>(), Arg.Any<CancellationToken>()))
				.Do(call =>
				{
					Upserts++;
					var s = call.Arg<SharpSession>();
					// Copy: the store mutates the instance it read back, so keeping the reference would
					// make the "stored" expiry follow un-persisted slides.
					Stored = new SharpSession
					{
						Token = s.Token, AccountId = s.AccountId, OriginIp = s.OriginIp,
						ExpiryUnixMs = s.ExpiryUnixMs, TtlMs = s.TtlMs,
						CharacterKey = s.CharacterKey, CharacterCreationTime = s.CharacterCreationTime
					};
				});
			Database.When(x => x.DeleteSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
				.Do(_ =>
				{
					Deletes++;
					Stored = null;
				});
		}

		/// <summary>Backdates the persisted expiry, standing in for time passing since the last write.</summary>
		public void AgeBy(TimeSpan elapsed) => Stored!.ExpiryUnixMs -= (long)elapsed.TotalMilliseconds;
	}

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
		await Assert.That(spy.Upserts).IsEqualTo(2);
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
	}
}
