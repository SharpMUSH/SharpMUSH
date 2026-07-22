using System.Net;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

file sealed class FakeActiveCharacterState : IAccountAuthState
{
	public bool IsLoggedIn => AccountSessionToken is not null;
	public string? AccountSessionToken { get; set; } = "tok";
	public string? Username { get; set; }
	public string? Role { get; set; }
	public IReadOnlyList<string> Permissions { get; set; } = [];
	public bool ExplicitlyLoggedOut { get; set; }
	public event Action? AuthStateChanged;
	public AccountAuthService.CharacterSummary? ActiveCharacter { get; set; }
	public event Action? ActiveCharacterChanged;
	public Task InitAsync() => Task.CompletedTask;
	public Task<AccountAuthService.DebugOttResponse?> GetDebugOttAsync() =>
		Task.FromResult<AccountAuthService.DebugOttResponse?>(null);

	public void Touch()
	{
		AuthStateChanged?.Invoke();
		ActiveCharacterChanged?.Invoke();
	}
}

file sealed class CapturingInner : HttpMessageHandler
{
	public HttpRequestMessage? LastRequest { get; private set; }
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		LastRequest = request;
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
	}
}

/// <summary>
/// Pins that portal REST calls advertise which character the tab is acting as, so the server routes
/// per-character actions (mail, scenes, gallery, live commands) to the switched-to character rather
/// than always the primary. The server validates the hint against the account's own characters.
/// </summary>
public class ActingCharacterHeaderHandlerTests
{
	private static async Task<HttpRequestMessage?> SendAsync(IAccountAuthState auth)
	{
		var inner = new CapturingInner();
		using var handler = new ActingCharacterHeaderHandler(auth) { InnerHandler = inner };
		using var invoker = new HttpMessageInvoker(handler);
		await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/mail"), CancellationToken.None);
		return inner.LastRequest;
	}

	[Test]
	public async Task SendAsync_WithActiveCharacter_AddsActingCharacterHeader()
	{
		var auth = new FakeActiveCharacterState
		{
			ActiveCharacter = new AccountAuthService.CharacterSummary(7, 777L, "Bob", "")
		};

		var sent = await SendAsync(auth);

		await Assert.That(sent!.Headers.TryGetValues("X-Acting-Character", out var values)).IsTrue();
		await Assert.That(values!.Single()).IsEqualTo("#7");
	}

	[Test]
	public async Task SendAsync_NoActiveCharacter_AddsNoHeader()
	{
		var auth = new FakeActiveCharacterState { ActiveCharacter = null };

		var sent = await SendAsync(auth);

		await Assert.That(sent!.Headers.Contains("X-Acting-Character")).IsFalse();
	}
}
