using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Server.Authentication.External;

namespace SharpMUSH.Tests.Authentication.External;

public class ExternalLoginFlowStoreTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Test]
	public async Task RemoteTransactionAndHandoffAreOneUseAndBrowserBound()
	{
		var clock = new Clock();
		var store = new ExternalLoginFlowStore(clock);
		var start = store.Begin("google", "generation", "/account", false, ExternalFlowPurpose.Login).Expect<ExternalFlowStart>();
		store.TakeRemote(start.TransactionId, "wrong-browser").Expect<NotFound>();
		var transaction = store.TakeRemote(start.TransactionId, start.BrowserSecret).Expect<ExternalFlowTransaction>();
		store.TakeRemote(start.TransactionId, start.BrowserSecret).Expect<NotFound>();
		var handoff = store.HandOff(transaction, new ExternalIdentityKey("google", "https://accounts.google.com", "user"), "Display").Expect<string>();
		store.TakeHandoff(handoff, _ => "wrong-browser").Expect<NotFound>();
		store.TakeHandoff(handoff, _ => start.BrowserSecret).Expect<ExternalFlowHandoff>();
		store.TakeHandoff(handoff, _ => start.BrowserSecret).Expect<NotFound>();
		await Task.CompletedTask;
	}

	[Test]
	public async Task FixedExpiryBoundariesAndCapacityAreEnforced()
	{
		var clock = new Clock();
		var store = new ExternalLoginFlowStore(clock, capacity: 1);
		var start = store.Begin("google", "generation", "/account", false, ExternalFlowPurpose.Login).Expect<ExternalFlowStart>();
		store.Begin("google", "generation", "/account", false, ExternalFlowPurpose.Login).Expect<Error<string>>();
		clock.Now += TimeSpan.FromMinutes(5);
		store.TakeRemote(start.TransactionId, start.BrowserSecret).Expect<NotFound>();
		start = store.Begin("google", "generation", "/account", false, ExternalFlowPurpose.Login).Expect<ExternalFlowStart>();
		var transaction = store.TakeRemote(start.TransactionId, start.BrowserSecret).Expect<ExternalFlowTransaction>();
		var handoff = store.HandOff(transaction, new ExternalIdentityKey("google", "issuer", "user"), null).Expect<string>();
		clock.Now += TimeSpan.FromSeconds(60);
		store.TakeHandoff(handoff, _ => start.BrowserSecret).Expect<NotFound>();
		await Task.CompletedTask;
	}

	[Test]
	public async Task SignupCanRetryWithoutConsumingTheIdentityAndRevocationKillsBoundFlows()
	{
		var store = new ExternalLoginFlowStore(new Clock());
		var start = store.Begin("google", "generation", "/account", false, ExternalFlowPurpose.Link,
			"account", "session-digest", 4).Expect<ExternalFlowStart>();
		store.RevokeAccount("account");
		store.TakeRemote(start.TransactionId, start.BrowserSecret).Expect<NotFound>();
		start = store.Begin("google", "generation", "/account", false, ExternalFlowPurpose.Login).Expect<ExternalFlowStart>();
		var transaction = store.TakeRemote(start.TransactionId, start.BrowserSecret).Expect<ExternalFlowTransaction>();
		var pending = store.BeginSignup(new ExternalFlowHandoff(transaction, new ExternalIdentityKey("google", "issuer", "user"), null)).Expect<string>();
		store.ReadSignup(pending, _ => start.BrowserSecret).Expect<ExternalFlowHandoff>();
		store.ReadSignup(pending, _ => start.BrowserSecret).Expect<ExternalFlowHandoff>();
		store.FinishSignup(pending);
		store.ReadSignup(pending, _ => start.BrowserSecret).Expect<NotFound>();
		await Task.CompletedTask;
	}

	[Test]
	[Arguments("https://attacker.example")]
	[Arguments("//attacker.example")]
	[Arguments("/%2f%2fattacker.example")]
	[Arguments("/account\\evil")]
	[Arguments("/auth/external/complete")]
	[Arguments("/admin/../auth/external/complete")]
	[Arguments("/account\r\nLocation: evil")]
	public async Task ReturnPathsAreAnAllowlistOfLocalRoutes(string path)
	{
		await Assert.That(ExternalLoginFlowStore.AllowedReturnPath(path)).IsFalse();
		await Assert.That(ExternalLoginFlowStore.AllowedReturnPath("/account")).IsTrue();
		await Assert.That(ExternalLoginFlowStore.AllowedReturnPath("/admin/import")).IsTrue();
	}
}
