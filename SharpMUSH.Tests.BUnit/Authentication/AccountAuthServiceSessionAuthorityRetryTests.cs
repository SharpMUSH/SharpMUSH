using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Authentication;

/// <summary>
/// Fakes <c>GET api/account/session</c> and <c>POST api/auth/switch-character</c>. The session read
/// answers 503 until <see cref="Recover"/> is called, then the account's authority — and records which
/// bearer token each read carried.
/// </summary>
internal sealed class SessionAuthorityHandler : HttpMessageHandler
{
	private readonly TaskCompletionSource _recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _sessionReads;

	public int SessionReads => Volatile.Read(ref _sessionReads);
	public string? LastSessionToken { get; private set; }

	/// <summary>From now on the session read succeeds.</summary>
	public void Recover() => _recovered.TrySetResult();

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath;
		if (path == "/api/auth/switch-character")
		{
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new { ott = "switch-ott", expiresIn = 60, accountSessionToken = "switched-session-token" })
			};
		}

		if (path != "/api/account/session") return new HttpResponseMessage(HttpStatusCode.NotFound);

		Interlocked.Increment(ref _sessionReads);
		LastSessionToken = request.Headers.Authorization?.Parameter;
		if (!_recovered.Task.IsCompleted) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

		await Task.Yield();
		return new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new { username = "headwiz", mustChangePassword = false, role = "Wizard", permissions = new[] { "layout.admin" } })
		};
	}
}

/// <summary>
/// A tab restoring a stored session asks the server for its current role and grants. When that request
/// failed — the server restarting, a dropped connection, the five-second timeout — the tab kept the token
/// with no role at all, and because <see cref="AccountAuthService.InitAsync"/> is cached for the life of
/// the tab it stayed a Guest until a reload. It now asks again in the background.
/// </summary>
public class AccountAuthServiceSessionAuthorityRetryTests : TrackingBunitContext
{
	private AccountAuthService Create(SessionAuthorityHandler handler)
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

	/// <summary>Completes on the first notification after which the tab holds a role.</summary>
	private static Task RoleArrives(AccountAuthService service)
	{
		var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		service.AuthStateChanged += () => { if (service.Role is not null) arrived.TrySetResult(); };
		return arrived.Task;
	}

	[Test]
	public async Task AFailedLoad_IsRetried_AndThePortalIsToldWhenTheRoleArrives()
	{
		var handler = new SessionAuthorityHandler();
		var service = Create(handler);
		var roleArrives = RoleArrives(service);

		await service.InitAsync();
		await Assert.That(service.Role).IsNull().Because("the server has not answered yet");

		handler.Recover();
		await roleArrives.WaitAsync(TimeSpan.FromSeconds(5));

		await Assert.That(service.Role).IsEqualTo("Wizard");
		await Assert.That(service.Permissions).Contains("layout.admin");
		await Assert.That(handler.SessionReads).IsGreaterThan(1);
	}

	[Test]
	public async Task ASuccessfulLoad_IsNotRepeated()
	{
		var handler = new SessionAuthorityHandler();
		handler.Recover();
		var service = Create(handler);

		await service.InitAsync();
		await Task.Delay(100);

		await Assert.That(service.Role).IsEqualTo("Wizard");
		await Assert.That(handler.SessionReads).IsEqualTo(1);
	}

	[Test]
	public async Task ACharacterSwitch_KeepsRecovering_WithTheNewToken()
	{
		// A switch adopts a new token for the same account without loading its authority. Recovery used
		// to key on the old token and give up here, leaving the account without its grants.
		var handler = new SessionAuthorityHandler();
		var service = Create(handler);
		var roleArrives = RoleArrives(service);

		await service.InitAsync();
		var ott = await service.SwitchCharacterAsync(new CharacterSummary(2, 2L, "Beta", ""));
		await Assert.That(ott.Expect<string>()).IsEqualTo("switch-ott");

		handler.Recover();
		await roleArrives.WaitAsync(TimeSpan.FromSeconds(5));

		await Assert.That(service.Role).IsEqualTo("Wizard");
		await Assert.That(handler.LastSessionToken).IsEqualTo("switched-session-token");
	}

	[Test]
	public async Task Retries_Stop_WhenTheTabSignsOut()
	{
		var handler = new SessionAuthorityHandler();
		var service = Create(handler);
		service.SessionAuthorityRetryDelays = [TimeSpan.FromMilliseconds(50)];

		await service.InitAsync();
		await service.LogoutAsync();
		var readsAtLogout = handler.SessionReads;
		await Task.Delay(300);

		await Assert.That(handler.SessionReads).IsEqualTo(readsAtLogout)
			.Because("there is no session left to load authority for");
	}
}
