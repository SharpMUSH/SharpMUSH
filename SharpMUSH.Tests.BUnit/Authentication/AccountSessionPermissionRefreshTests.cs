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
			Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());
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
			Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());
		await service.InitAsync();
		await Assert.That(service.Permissions).IsEmpty();
		await Assert.That(service.Role).IsNull();
		if (status == HttpStatusCode.Unauthorized) await Assert.That(service.IsLoggedIn).IsFalse();
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
			Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());
		await service.InitAsync();
		await service.InitAsync();
		await Assert.That(service.AccountSessionToken).IsEqualTo("valid-token");
		await Assert.That(service.Role).IsNull();
		await Assert.That(service.Permissions).IsEmpty();
	}

}
