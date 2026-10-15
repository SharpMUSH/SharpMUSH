using SharpMUSH.Library.Plugins;
using SharpMUSH.Server.Authentication;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Whatever drops an account's claims (a role assigned, a character linked, a ban) tells every
/// <see cref="IAccountChangeListener"/>, which is how a plugin mirrors an account's roles elsewhere.
/// </summary>
public class AccountChangeListenerTests
{
	private sealed class Recording : IAccountChangeListener
	{
		public List<string> Heard { get; } = [];

		public ValueTask AccountChangedAsync(string accountId, CancellationToken cancellationToken)
		{
			Heard.Add(accountId);
			return ValueTask.CompletedTask;
		}
	}

	private sealed class Throwing : IAccountChangeListener
	{
		public ValueTask AccountChangedAsync(string accountId, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("listener failed");
	}

	private static FusionCache Cache()
		=> new(new Microsoft.Extensions.Options.OptionsWrapper<FusionCacheOptions>(new FusionCacheOptions()));

	[Test]
	public async Task InvalidatingAnAccount_TellsEachListener()
	{
		var first = new Recording();
		var second = new Recording();
		var invalidator = new AccountClaimsInvalidator(Cache(), new Lazy<IEnumerable<IAccountChangeListener>>(() => [first, second]));

		await invalidator.InvalidateAsync("account-1");

		await Assert.That(first.Heard).IsEquivalentTo(["account-1"]);
		await Assert.That(second.Heard).IsEquivalentTo(["account-1"]);
	}

	[Test]
	public async Task AListenerThatThrows_DoesNotKeepTheNextFromHearing()
	{
		var after = new Recording();
		var invalidator = new AccountClaimsInvalidator(Cache(), new Lazy<IEnumerable<IAccountChangeListener>>(() => [new Throwing(), after]));

		await invalidator.InvalidateAsync("account-2");

		await Assert.That(after.Heard).IsEquivalentTo(["account-2"]);
	}
}
