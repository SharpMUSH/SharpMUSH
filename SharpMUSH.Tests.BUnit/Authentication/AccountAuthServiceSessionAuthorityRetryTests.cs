using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Authentication;

/// <summary>Answers api/account/session from a script, one response per call; counts the calls.</summary>
internal sealed class ScriptedSessionHandler(params Func<HttpResponseMessage>[] answers) : HttpMessageHandler
{
	private int _calls;
	public int Calls => Volatile.Read(ref _calls);

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var call = Interlocked.Increment(ref _calls);
		return Task.FromResult(answers[Math.Min(call, answers.Length) - 1]());
	}
}

/// <summary>
/// A tab restoring a stored session asks the server for its current role and grants. When that request
/// fails — the server restarting, a dropped connection, the five-second timeout — the tab kept the token
/// with no role at all, and because <see cref="AccountAuthService.InitAsync"/> is cached for the life of
/// the tab it stayed a Guest until a reload. It now asks again in the background.
/// </summary>
public class AccountAuthServiceSessionAuthorityRetryTests : TrackingBunitContext
{
	private static HttpResponseMessage Unavailable() => new(HttpStatusCode.ServiceUnavailable);

	private static HttpResponseMessage Session() => new(HttpStatusCode.OK)
	{
		Content = JsonContent.Create(new { username = "headwiz", mustChangePassword = false, role = "Wizard", permissions = new[] { "layout.admin" } })
	};

	private AccountAuthService Create(ScriptedSessionHandler handler)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("stored-session-token");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");

		var http = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		return new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, [])
		{
			SessionAuthorityRetryDelays = [TimeSpan.FromMilliseconds(10)],
		};
	}

	[Test]
	public async Task AFailedRefresh_IsRetried_AndThePortalIsToldWhenTheRoleArrives()
	{
		var handler = new ScriptedSessionHandler(Unavailable, Unavailable, Session);
		var service = Create(handler);
		var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await service.InitAsync();
		await Assert.That(service.Role).IsNull().Because("the first refresh failed");
		service.AuthStateChanged += () => { if (service.Role is not null) changed.TrySetResult(); };

		await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

		await Assert.That(service.Role).IsEqualTo("Wizard");
		await Assert.That(service.Permissions).Contains("layout.admin");
		await Assert.That(handler.Calls).IsEqualTo(3);
	}

	[Test]
	public async Task ASuccessfulRefresh_IsNotRepeated()
	{
		var handler = new ScriptedSessionHandler(Session);
		var service = Create(handler);

		await service.InitAsync();
		await Task.Delay(100);

		await Assert.That(service.Role).IsEqualTo("Wizard");
		await Assert.That(handler.Calls).IsEqualTo(1);
	}

	[Test]
	public async Task Retries_Stop_WhenTheTabLeavesThatSession()
	{
		var handler = new ScriptedSessionHandler(Unavailable);
		var service = Create(handler);
		service.SessionAuthorityRetryDelays = [TimeSpan.FromMilliseconds(50)];

		await service.InitAsync();
		await service.LogoutAsync();
		var callsAtLogout = handler.Calls;
		await Task.Delay(300);

		await Assert.That(handler.Calls).IsEqualTo(callsAtLogout)
			.Because("a refresh for a session the tab no longer holds has nothing to update");
	}
}
