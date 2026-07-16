using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Authentication;

/// <summary>Counts requests and always returns a successful debug-OTT payload.</summary>
file sealed class SingleSuccessHandler : HttpMessageHandler
{
	public int CallCount { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		CallCount++;
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new
			{
				token = $"debug-ott-{CallCount}",
				expiresIn = 900,
				playerName = "God",
				accountId = (string?)null,
				accountUsername = (string?)null,
				accountSessionToken = (string?)null,
				accountMustChangePassword = false,
			})
		});
	}
}

/// <summary>First call 500s, every call after that succeeds — proves a failed fetch doesn't get stuck cached.</summary>
file sealed class FailThenSucceedHandler : HttpMessageHandler
{
	public int CallCount { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		CallCount++;
		if (CallCount == 1)
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new
			{
				token = "debug-ott-retry",
				expiresIn = 900,
				playerName = "God",
				accountId = (string?)null,
				accountUsername = (string?)null,
				accountSessionToken = (string?)null,
				accountMustChangePassword = false,
			})
		});
	}
}

/// <summary>Always 500s; used where the assertion is that it must never be called at all.</summary>
file sealed class NeverExpectedHandler : HttpMessageHandler
{
	public int CallCount { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		CallCount++;
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
	}
}

/// <summary>
/// Regression coverage for the "4x debug OTT issued at boot" bug: MainLayout, GlobalTerminal,
/// Account.razor, and DebugAuthStateProvider all call <see cref="AccountAuthService.GetDebugOttAsync"/>
/// concurrently at startup, and before this fix each call minted its own separate one-time God
/// token — only one of which was ever redeemed. <see cref="AccountAuthService.GetDebugOttAsync"/>
/// now single-flights following the exact <c>_initTask ??= ...</c> pattern already used by
/// <see cref="AccountAuthService.InitAsync"/>.
/// </summary>
public class AccountAuthServiceDebugOttTests : BunitContext
{
	private static IHttpClientFactory FactoryFor(HttpMessageHandler handler)
	{
		var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		return factory;
	}

	/// <summary>Two concurrent callers must share one in-flight fetch and one HTTP request.</summary>
	[Test]
	public async Task GetDebugOttAsync_TwoConcurrentCalls_MakesExactlyOneHttpRequest_SameResponse()
	{
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(null);

		var handler = new SingleSuccessHandler();
		var service = new AccountAuthService(FactoryFor(handler), JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);

		// Two callers racing before either has completed — same as the boot-time race between
		// MainLayout, GlobalTerminal, Account.razor, and DebugAuthStateProvider.
		var first = service.GetDebugOttAsync();
		var second = service.GetDebugOttAsync();

		await Assert.That(ReferenceEquals(first, second)).IsTrue();

		var (result1, result2) = (await first, await second);

		await Assert.That(handler.CallCount).IsEqualTo(1);
		await Assert.That(result1).IsNotNull();
		await Assert.That(ReferenceEquals(result1, result2)).IsTrue();
	}

	/// <summary>A failed fetch (server unreachable / non-success) must not be cached — the next call retries.</summary>
	[Test]
	public async Task GetDebugOttAsync_FirstCallFails_ReturnsNull_SubsequentCallRetriesAndSucceeds()
	{
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(null);

		var handler = new FailThenSucceedHandler();
		var service = new AccountAuthService(FactoryFor(handler), JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);

		var firstResult = await service.GetDebugOttAsync();
		await Assert.That(firstResult).IsNull();
		await Assert.That(handler.CallCount).IsEqualTo(1);

		var secondResult = await service.GetDebugOttAsync();
		await Assert.That(secondResult).IsNotNull();
		await Assert.That(handler.CallCount).IsEqualTo(2);
	}

	/// <summary>
	/// After an explicit logout, the logout latch must short-circuit before any HTTP call, and
	/// LogoutAsync must have cleared the cached debug-OTT task so a resurrected pre-logout
	/// response can never leak back out.
	/// </summary>
	[Test]
	public async Task GetDebugOttAsync_AfterLogout_ReturnsNull_NoHttpRequest()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(null);

		var handler = new NeverExpectedHandler();
		var service = new AccountAuthService(FactoryFor(handler), JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);

		await service.LogoutAsync();
		await Assert.That(service.ExplicitlyLoggedOut).IsTrue();

		var result = await service.GetDebugOttAsync();

		await Assert.That(result).IsNull();
		await Assert.That(handler.CallCount).IsEqualTo(0);
	}
}
