using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Authentication;

public class AccountSessionPermissionRefreshTests : TrackingBunitContext
{
	private sealed class SessionHandler(string[] permissions, HttpStatusCode status) : HttpMessageHandler
	{
		public int Calls { get; private set; }
		public string? Bearer { get; private set; }
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Calls++;
			Bearer = request.Headers.Authorization?.Parameter;
			return Task.FromResult(new HttpResponseMessage(status)
			{
				Content = JsonContent.Create(new { username = "current", role = "Wizard", mustChangePassword = false, permissions })
			});
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HydrationUsesCurrentPermissionsInsteadOfStoredGrants(bool revoked)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("stored");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Wizard");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult(revoked ? "[\"jobs.manage.own\"]" : "[]");
		var expected = revoked ? Array.Empty<string>() : new[] { "jobs.manage.own" };
		var handler = new SessionHandler(expected, HttpStatusCode.OK);
		var factory = Substitute.For<IHttpClientFactory>();
		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			[]);
		using var http = new HttpClient(new AccountSessionBearerHandler(service) { InnerHandler = handler }) { BaseAddress = new Uri("https://localhost/") };
		factory.CreateClient("api").Returns(http);
		await Task.WhenAll(service.InitAsync(), service.InitAsync()).WaitAsync(TimeSpan.FromSeconds(3));
		await Assert.That(handler.Calls).IsEqualTo(1);
		await Assert.That(handler.Bearer).IsEqualTo("session");
		await Assert.That(service.Permissions).IsEquivalentTo(expected);
		await Assert.That(service.Username).IsEqualTo("current");
	}

	[Test]
	[Arguments(HttpStatusCode.Unauthorized)]
	[Arguments(HttpStatusCode.ServiceUnavailable)]
	public async Task FailedRefreshCannotRestoreStoredAuthority(HttpStatusCode status)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("expired");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("God");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		var factory = Substitute.For<IHttpClientFactory>();
		using var http = new HttpClient(new SessionHandler(["*"], status)) { BaseAddress = new Uri("https://localhost/") };
		factory.CreateClient("api").Returns(http);
		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			[]);
		await service.InitAsync();
		await Assert.That(service.Permissions).IsEmpty();
		await Assert.That(service.Role).IsNull();
		if (status == HttpStatusCode.Unauthorized) await Assert.That(service.IsLoggedIn).IsFalse();
	}
	/// <summary>A fresh account: a Guest with no grants until its first character exists.</summary>
	private sealed class FirstCharacterHandler : HttpMessageHandler
	{
		private bool _hasCharacter;

		/// <summary>The next api/account/session read answers 503, once.</summary>
		public bool FailNextSessionRead { get; set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath.TrimStart('/');
			if (FailNextSessionRead && path == "api/account/session")
			{
				FailNextSessionRead = false;
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
			}
			if (request.Method == HttpMethod.Post && path == "api/account/characters")
			{
				_hasCharacter = true;
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
				{
					Content = JsonContent.Create(new { dbrefNumber = 16, creationTime = 16L, name = "Ash" })
				});
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					username = "alice",
					role = _hasCharacter ? "Player" : "Guest",
					mustChangePassword = false,
					permissions = _hasCharacter ? new[] { "wiki.create", "softcode.use" } : []
				})
			});
		}
	}

	/// <summary>
	/// The role comes from the account's characters. Creating the first one has to bring its grants into
	/// this tab: the portal kept a new player a Guest (no build tools, no wiki editing, /softcode refused)
	/// until they signed out and in again.
	/// </summary>
	[Test]
	public async Task CreatingTheFirstCharacter_LoadsItsRoleAndGrants()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session");
		var factory = Substitute.For<IHttpClientFactory>();
		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		using var http = new HttpClient(new FirstCharacterHandler()) { BaseAddress = new Uri("https://localhost/") };
		factory.CreateClient("api").Returns(http);
		await service.InitAsync();
		await Assert.That(service.Role).IsEqualTo("Guest");

		var notified = 0;
		service.AuthStateChanged += () => notified++;
		var created = await service.CreateCharacterAsync("Ash", "pass");

		await Assert.That(created.Expect<AccountAuthService.CharacterSummary>().Name).IsEqualTo("Ash");
		await Assert.That(service.Role).IsEqualTo("Player");
		await Assert.That(service.Permissions).Contains("softcode.use");
		await Assert.That(notified).IsGreaterThanOrEqualTo(1).Because("gated controls re-check when the auth state changes");
	}

	/// <summary>
	/// A dropped session read right after the first character is created must not leave the tab a Guest:
	/// the reload falls back to the backing-off retry.
	/// </summary>
	[Test]
	public async Task CreatingTheFirstCharacter_RetriesTheRoleWhenTheFirstReadFails()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session");
		var factory = Substitute.For<IHttpClientFactory>();
		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, [])
		{
			SessionAuthorityRetryDelays = [TimeSpan.FromMilliseconds(10)]
		};
		var handler = new FirstCharacterHandler();
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
		factory.CreateClient("api").Returns(http);
		await service.InitAsync();

		handler.FailNextSessionRead = true;
		await service.CreateCharacterAsync("Ash", "pass");

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (service.Role != "Player" && DateTime.UtcNow < deadline) await Task.Delay(20);
		await Assert.That(service.Role).IsEqualTo("Player");
	}

	private sealed class FailedTransportHandler(bool timeout) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromException<HttpResponseMessage>(timeout ? new TaskCanceledException("timeout") : new HttpRequestException("offline"));
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task TransportFailureKeepsTheSessionWithoutCachedAuthority(bool timeout)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("valid-token");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("God");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		var factory = Substitute.For<IHttpClientFactory>();
		using var http = new HttpClient(new FailedTransportHandler(timeout)) { BaseAddress = new Uri("https://localhost/") };
		factory.CreateClient("api").Returns(http);
		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			[]);
		await service.InitAsync();
		await service.InitAsync();
		await Assert.That(service.AccountSessionToken).IsEqualTo("valid-token");
		await Assert.That(service.Role).IsNull();
		await Assert.That(service.Permissions).IsEmpty();
	}

}
