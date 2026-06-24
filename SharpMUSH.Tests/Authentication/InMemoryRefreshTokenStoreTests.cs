using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="InMemoryRefreshTokenStore"/> covering token lifecycle and revocation.
/// </summary>
public class InMemoryRefreshTokenStoreTests
{
	private static DBRef Ref(int n) => new(n, 0L);

	[Test]
	public async ValueTask CreateToken_Returns32HexChars()
	{
		var store = new InMemoryRefreshTokenStore();
		var token = await store.CreateTokenAsync("acc1", Ref(1), TimeSpan.FromMinutes(10));
		await Assert.That(token).Matches("^[0-9a-f]{32}$");
	}

	[Test]
	public async ValueTask CreateToken_TwoTokens_AreUnique()
	{
		var store = new InMemoryRefreshTokenStore();
		var t1 = await store.CreateTokenAsync("acc1", Ref(1), TimeSpan.FromMinutes(10));
		var t2 = await store.CreateTokenAsync("acc1", Ref(1), TimeSpan.FromMinutes(10));
		await Assert.That(t1).IsNotEqualTo(t2);
	}

	[Test]
	public async ValueTask ValidateToken_Valid_ReturnsPayload()
	{
		var store = new InMemoryRefreshTokenStore();
		var token = await store.CreateTokenAsync("acc1", Ref(5), TimeSpan.FromMinutes(10));

		var result = await store.ValidateAsync(token);

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Value.AccountId).IsEqualTo("acc1");
		await Assert.That(result!.Value.CharacterRef.Number).IsEqualTo(5);
	}

	[Test]
	public async ValueTask ValidateToken_UnknownToken_ReturnsNull()
	{
		var store = new InMemoryRefreshTokenStore();
		var result = await store.ValidateAsync("not-a-real-token");
		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask ValidateToken_AfterExpiry_ReturnsNull()
	{
		var store = new InMemoryRefreshTokenStore();
		var token = await store.CreateTokenAsync("acc1", Ref(5), TimeSpan.FromMilliseconds(1));

		await Task.Delay(50);

		var result = await store.ValidateAsync(token);
		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask ValidateToken_DoesNotAutoRevoke_SecondReadSucceeds()
	{
		// Refresh tokens do NOT slide; they are not auto-revoked on read.
		// The caller calls RevokeAsync manually after issuing new tokens.
		var store = new InMemoryRefreshTokenStore();
		var token = await store.CreateTokenAsync("acc1", Ref(5), TimeSpan.FromMinutes(10));

		var first = await store.ValidateAsync(token);
		var second = await store.ValidateAsync(token);

		await Assert.That(first).IsNotNull();
		await Assert.That(second).IsNotNull();
	}

	[Test]
	public async ValueTask RevokeToken_AfterRevoke_ReturnsNull()
	{
		var store = new InMemoryRefreshTokenStore();
		var token = await store.CreateTokenAsync("acc1", Ref(5), TimeSpan.FromMinutes(10));

		await store.RevokeAsync(token);

		var result = await store.ValidateAsync(token);
		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask RevokeToken_UnknownToken_DoesNotThrow()
	{
		var store = new InMemoryRefreshTokenStore();
		await store.RevokeAsync("ghost-token");
	}

	[Test]
	public async ValueTask RevokeToken_OneOfTwo_OtherRemainsValid()
	{
		var store = new InMemoryRefreshTokenStore();
		var t1 = await store.CreateTokenAsync("acc1", Ref(1), TimeSpan.FromMinutes(10));
		var t2 = await store.CreateTokenAsync("acc1", Ref(2), TimeSpan.FromMinutes(10));

		await store.RevokeAsync(t1);

		await Assert.That(await store.ValidateAsync(t1)).IsNull();
		await Assert.That(await store.ValidateAsync(t2)).IsNotNull();
	}

	[Test]
	public async ValueTask RevokeAllForAccount_RevokesOnlyTargetAccount()
	{
		var store = new InMemoryRefreshTokenStore();
		var tokenA1 = await store.CreateTokenAsync("acc1", Ref(1), TimeSpan.FromMinutes(10));
		var tokenA2 = await store.CreateTokenAsync("acc1", Ref(2), TimeSpan.FromMinutes(10));
		var tokenB = await store.CreateTokenAsync("acc2", Ref(3), TimeSpan.FromMinutes(10));

		await store.RevokeAllForAccountAsync("acc1");

		await Assert.That(await store.ValidateAsync(tokenA1)).IsNull();
		await Assert.That(await store.ValidateAsync(tokenA2)).IsNull();
		await Assert.That(await store.ValidateAsync(tokenB)).IsNotNull();
	}

	[Test]
	public async ValueTask MultipleTokensSameAccount_AllValid()
	{
		var store = new InMemoryRefreshTokenStore();
		var t1 = await store.CreateTokenAsync("acc1", Ref(1), TimeSpan.FromMinutes(10));
		var t2 = await store.CreateTokenAsync("acc1", Ref(2), TimeSpan.FromMinutes(10));

		await Assert.That(await store.ValidateAsync(t1)).IsNotNull();
		await Assert.That(await store.ValidateAsync(t2)).IsNotNull();
	}

	[Test]
	public async ValueTask Concurrent_CreateAndValidate_NoCrash()
	{
		var store = new InMemoryRefreshTokenStore();
		var tasks = Enumerable.Range(0, 50).Select(async i =>
		{
			var token = await store.CreateTokenAsync($"acc-{i}", Ref(i), TimeSpan.FromSeconds(30));
			var result = await store.ValidateAsync(token);
			await Assert.That(result).IsNotNull();
			await Assert.That(result!.Value.AccountId).IsEqualTo($"acc-{i}");
		});
		await Task.WhenAll(tasks);
	}
}
