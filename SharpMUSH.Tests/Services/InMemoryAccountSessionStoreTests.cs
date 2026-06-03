using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit tests for <see cref="InMemoryAccountSessionStore"/> covering token lifecycle,
/// rolling expiry, and revocation — no DI container needed.
/// </summary>
public class InMemoryAccountSessionStoreTests
{
	private static InMemoryAccountSessionStore CreateStore() => new();

	// ── CreateTokenAsync ──────────────────────────────────────────────────────

	[Test]
	public async ValueTask CreateToken_Returns32CharHexGuid()
	{
		var store = CreateStore();
		var token = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15));

		await Assert.That(token).IsNotNull();
		await Assert.That(token.Length).IsEqualTo(32); // Guid "N" format
		await Assert.That(token).Matches("^[0-9a-f]{32}$");
	}

	[Test]
	public async ValueTask CreateToken_TwoTokens_AreUnique()
	{
		var store = CreateStore();
		var t1 = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15));
		var t2 = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15));

		await Assert.That(t1).IsNotEqualTo(t2);
	}

	[Test]
	public async ValueTask CreateToken_DifferentAccountIds_BothStoredIndependently()
	{
		var store = CreateStore();
		var t1 = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15));
		var t2 = await store.CreateTokenAsync("acct-2", TimeSpan.FromMinutes(15));

		var id1 = await store.ValidateAsync(t1);
		var id2 = await store.ValidateAsync(t2);

		await Assert.That(id1).IsEqualTo("acct-1");
		await Assert.That(id2).IsEqualTo("acct-2");
	}

	// ── ValidateAsync — valid token ───────────────────────────────────────────

	[Test]
	public async ValueTask ValidateAsync_ValidToken_ReturnsAccountId()
	{
		var store = CreateStore();
		var token = await store.CreateTokenAsync("acct-42", TimeSpan.FromMinutes(15));

		var accountId = await store.ValidateAsync(token);

		await Assert.That(accountId).IsEqualTo("acct-42");
	}

	[Test]
	public async ValueTask ValidateAsync_UnknownToken_ReturnsNull()
	{
		var store = CreateStore();

		var result = await store.ValidateAsync("not-a-real-token");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask ValidateAsync_EmptyToken_ReturnsNull()
	{
		var store = CreateStore();

		var result = await store.ValidateAsync(string.Empty);

		await Assert.That(result).IsNull();
	}

	// ── ValidateAsync — expiry ────────────────────────────────────────────────

	[Test]
	public async ValueTask ValidateAsync_ExpiredToken_ReturnsNull()
	{
		var store = CreateStore();
		var token = await store.CreateTokenAsync("acct-1", TimeSpan.FromMilliseconds(1));

		// Wait for the token to expire
		await Task.Delay(50);

		var result = await store.ValidateAsync(token);
		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask ValidateAsync_ExpiredToken_IsRemovedFromStore()
	{
		var store = CreateStore();
		var token = await store.CreateTokenAsync("acct-1", TimeSpan.FromMilliseconds(1));

		await Task.Delay(50);
		_ = await store.ValidateAsync(token); // triggers removal

		// A second validation should also return null (not re-inserted)
		var second = await store.ValidateAsync(token);
		await Assert.That(second).IsNull();
	}

	// ── Sliding expiry ────────────────────────────────────────────────────────

	[Test]
	public async ValueTask ValidateAsync_SlidesExpiry_TokenRemainsValid()
	{
		var store = CreateStore();
		// TTL of 200 ms; we'll validate at ~100 ms and expect the expiry is extended
		var token = await store.CreateTokenAsync("acct-slide", TimeSpan.FromMilliseconds(200));

		await Task.Delay(100);
		var first = await store.ValidateAsync(token); // slides expiry to now+200 ms
		await Assert.That(first).IsEqualTo("acct-slide");

		// If expiry did NOT slide, this 150 ms delay would exceed the original 200 ms window.
		// Because it DID slide, the token should still be valid.
		await Task.Delay(150);
		var second = await store.ValidateAsync(token);
		await Assert.That(second).IsEqualTo("acct-slide");
	}

	[Test]
	public async ValueTask ValidateAsync_CanBeCalledMultipleTimes_ReturnsAccountIdEachTime()
	{
		var store = CreateStore();
		var token = await store.CreateTokenAsync("acct-multi", TimeSpan.FromMinutes(15));

		for (var i = 0; i < 5; i++)
		{
			var result = await store.ValidateAsync(token);
			await Assert.That(result).IsEqualTo("acct-multi");
		}
	}

	// ── RevokeAsync ───────────────────────────────────────────────────────────

	[Test]
	public async ValueTask RevokeAsync_ValidToken_SubsequentValidateReturnsNull()
	{
		var store = CreateStore();
		var token = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15));

		await store.RevokeAsync(token);
		var result = await store.ValidateAsync(token);

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask RevokeAsync_UnknownToken_DoesNotThrow()
	{
		var store = CreateStore();

		// Should complete without throwing
		await store.RevokeAsync("ghost-token");
	}

	[Test]
	public async ValueTask RevokeAsync_OneOf_TwoTokens_OtherRemainsValid()
	{
		var store = CreateStore();
		var t1 = await store.CreateTokenAsync("acct-1", TimeSpan.FromMinutes(15));
		var t2 = await store.CreateTokenAsync("acct-2", TimeSpan.FromMinutes(15));

		await store.RevokeAsync(t1);

		var r1 = await store.ValidateAsync(t1);
		var r2 = await store.ValidateAsync(t2);

		await Assert.That(r1).IsNull();
		await Assert.That(r2).IsEqualTo("acct-2");
	}

	// ── Thread-safety (smoke test) ────────────────────────────────────────────

	[Test]
	public async ValueTask CreateAndValidate_Concurrent_NoCrash()
	{
		var store = CreateStore();
		var tasks = Enumerable.Range(0, 50).Select(async i =>
		{
			var token = await store.CreateTokenAsync($"acct-{i}", TimeSpan.FromSeconds(30));
			var result = await store.ValidateAsync(token);
			await Assert.That(result).IsEqualTo($"acct-{i}");
		});

		await Task.WhenAll(tasks);
	}
}
