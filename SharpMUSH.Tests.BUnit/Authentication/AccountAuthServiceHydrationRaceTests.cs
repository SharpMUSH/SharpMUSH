using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Authentication;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Authentication;

/// <summary>
/// Counts how many times its client actually sent an HTTP request, without caring what's sent.
/// Used to prove a code path never reaches the network — the assertion is on the count, not on
/// any particular response, so it can't accidentally pass "for the wrong reason" the way an
/// unconfigured Substitute throwing (and being swallowed by a try/catch) could.
/// </summary>
file sealed class CountingHandler : HttpMessageHandler
{
	public int CallCount { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		CallCount++;
		return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
	}
}

/// <summary>
/// Regression coverage for the initialization race: <c>CascadingAuthenticationState</c>
/// (App.razor root) calls <c>DebugAuthStateProvider.GetAuthenticationStateAsync</c> — and through
/// it, <see cref="AccountAuthService.GetDebugOttAsync"/> — before any component has necessarily
/// called <see cref="AccountAuthService.InitAsync"/> (MainLayout used to be the only caller, and
/// on a page refresh there's no guarantee it runs first). Before the fix, every entry point that
/// consulted <see cref="AccountAuthService.ExplicitlyLoggedOut"/> read the un-hydrated default
/// `false`, silently re-authenticated, and <c>PersistSessionAsync</c> erased the persisted latch —
/// so a logout that survived one reload could be undone by the next one, depending on timing.
/// The fix makes every such entry point hydrate for itself first (idempotent, single-flight), so
/// the latch is observed correctly regardless of what has or hasn't run yet.
/// </summary>
public class AccountAuthServiceHydrationRaceTests : BunitContext
{
	[Test]
	public async Task GetDebugOttAsync_WithoutPriorInitAsync_LatchedSession_ReturnsNullWithoutCallingServer()
	{
		// sessionStorage already holds a latched logout AND a real stored session token from a
		// previous tab lifetime — exactly what a page refresh after logout looks like. Nobody has
		// called InitAsync yet.
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(bool.TrueString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("stored-session-token");

		var countingHandler = new CountingHandler();
		using var http = new HttpClient(countingHandler) { BaseAddress = new Uri("https://localhost:8081/") };
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(httpClientFactory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);

		// No call to service.InitAsync() here — this is the point of the test.
		var result = await service.GetDebugOttAsync();

		await Assert.That(result).IsNull();
		await Assert.That(countingHandler.CallCount).IsEqualTo(0);
		// And hydration did happen as a side effect, so the latch is now visible for anyone else.
		await Assert.That(service.ExplicitlyLoggedOut).IsTrue();
	}

	[Test]
	public async Task DebugAuthStateProvider_GetAuthenticationStateAsync_UnInitedLatchedService_ReturnsAnonymous()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(bool.TrueString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(null);

		var countingHandler = new CountingHandler();
		using var http = new HttpClient(countingHandler) { BaseAddress = new Uri("https://localhost:8081/") };
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(httpClientFactory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);
		var provider = new DebugAuthStateProvider(service);

		// Simulates CascadingAuthenticationState querying auth state on the very first render,
		// before MainLayout (or anything else) has called service.InitAsync().
		var state = await provider.GetAuthenticationStateAsync();

		await Assert.That(state.User.Identity?.IsAuthenticated ?? false).IsFalse();
		await Assert.That(state.User.Claims).IsEmpty();
		await Assert.That(countingHandler.CallCount).IsEqualTo(0);
	}

	[Test]
	public async Task InitAsync_CalledConcurrentlyTwice_RunsHydrationCoreOnlyOnce()
	{
		var loggedOutHandler = JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut")
			.SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(null);

		var service = new AccountAuthService(
			Substitute.For<IHttpClientFactory>(),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance);

		// Two callers racing before either has completed — the single-flight `_initTask ??= ...`
		// must hand the second caller the first caller's in-flight task rather than starting a
		// second read of storage.
		var first = service.InitAsync();
		var second = service.InitAsync();

		await Assert.That(ReferenceEquals(first, second)).IsTrue();

		await Task.WhenAll(first, second);

		await Assert.That(loggedOutHandler.Invocations.Count).IsEqualTo(1);
	}
}
