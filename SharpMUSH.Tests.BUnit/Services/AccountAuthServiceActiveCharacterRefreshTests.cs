using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using NSubstitute;
using SharpMUSH.Client.Services;
using System.Net;
using System.Net.Http.Json;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// A real dictionary behind <c>sessionStorage.getItem/setItem/removeItem</c>. The point of these
/// tests is what survives a reload, so the store has to outlive the service that wrote to it —
/// a per-call stubbed JSInterop cannot express that.
/// </summary>
file sealed class FakeSessionStorage : IJSRuntime
{
	private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
		ValueTask.FromResult(Invoke<TValue>(identifier, args));

	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
		ValueTask.FromResult(Invoke<TValue>(identifier, args));

	private TValue Invoke<TValue>(string identifier, object?[]? args)
	{
		var key = args?.Length > 0 ? args[0]?.ToString() ?? string.Empty : string.Empty;

		switch (identifier)
		{
			case "sessionStorage.setItem" when args?.Length > 1:
				_items[key] = args[1]?.ToString() ?? string.Empty;
				break;
			case "sessionStorage.removeItem":
				_items.Remove(key);
				break;
			case "sessionStorage.getItem":
				return _items.TryGetValue(key, out var value) && value is TValue typed ? typed : default!;
		}

		return default!;
	}
}

/// <summary>Serves the login envelope and the character roster this account owns.</summary>
file sealed class FakeAccountApiHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath;

		// GetCharactersAsync reads a bare array; LoginAsync reads the session envelope.
		var content = path.EndsWith("api/account/characters", StringComparison.Ordinal)
			? JsonContent.Create(characters)
			: JsonContent.Create(new
			{
				accountId = "acct-1",
				username = "headwiz",
				characters,
				accountSessionToken = "session-token-1",
				mustChangePassword = false,
				role = "God",
				permissions = new[] { "*" },
			});

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
	}
}

/// <summary>
/// A switched-to character has to outlive a page reload. <c>ActingCharacterHeaderHandler</c> puts
/// <see cref="AccountAuthService.ActiveCharacter"/> on the wire as <c>X-Acting-Character</c>, and the
/// server routes per-character actions by that header — so if a refresh silently reseats the account
/// on its primary, uploads, mail and wiki edits get attributed to a character the player wasn't
/// acting as, with nothing on screen to say so.
/// </summary>
public class AccountAuthServiceActiveCharacterRefreshTests
{
	private static readonly CharacterSummary Alpha = new(10, 1000, "Alpha", "PLAYER");
	private static readonly CharacterSummary Beta = new(20, 2000, "Beta", "PLAYER");

	private static AccountAuthService MakeService(IJSRuntime js, IReadOnlyList<CharacterSummary> roster)
	{
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		// Not disposed: the returned service keeps calling through this client after the helper returns.
		var http = new HttpClient(new FakeAccountApiHandler(roster)) { BaseAddress = new Uri("https://localhost:8081/") };
		httpClientFactory.CreateClient("api").Returns(http);

		return new AccountAuthService(
			httpClientFactory,
			js,
			Substitute.For<ILogger<AccountAuthService>>(),
			Substitute.For<ITerminalService>(),
			Substitute.For<IPlayTerminalService>());
	}

	[Test]
	public async Task ActiveCharacter_SurvivesAPageRefresh()
	{
		var storage = new FakeSessionStorage();
		CharacterSummary[] roster = [Alpha, Beta];

		var beforeRefresh = MakeService(storage, roster);
		await beforeRefresh.InitAsync();
		await beforeRefresh.LoginAsync("headwiz", "password-one");
		beforeRefresh.SetActiveCharacter(Beta);

		// F5: a brand-new service (new DI container) over the same tab's sessionStorage.
		var afterRefresh = MakeService(storage, roster);
		await afterRefresh.InitAsync();
		await afterRefresh.GetCharactersAsync();

		await Assert.That(afterRefresh.ActiveCharacter?.DbrefNumber).IsEqualTo(Beta.DbrefNumber);
		await Assert.That(afterRefresh.ActiveCharacter?.CreationTime).IsEqualTo(Beta.CreationTime);
	}

	[Test]
	public async Task RestoredActiveCharacter_IsDroppedWhenTheAccountNoLongerOwnsIt()
	{
		var storage = new FakeSessionStorage();

		var beforeRefresh = MakeService(storage, [Alpha, Beta]);
		await beforeRefresh.InitAsync();
		await beforeRefresh.LoginAsync("headwiz", "password-one");
		beforeRefresh.SetActiveCharacter(Beta);

		// Beta was unlinked elsewhere; the reloaded tab must not keep acting as it.
		var afterRefresh = MakeService(storage, [Alpha]);
		await afterRefresh.InitAsync();
		await afterRefresh.GetCharactersAsync();

		await Assert.That(afterRefresh.ActiveCharacter?.DbrefNumber).IsEqualTo(Alpha.DbrefNumber);
	}

	[Test]
	public async Task NoSession_LeavesNoActiveCharacterBehind()
	{
		var storage = new FakeSessionStorage();

		var beforeRefresh = MakeService(storage, [Alpha, Beta]);
		await beforeRefresh.InitAsync();
		await beforeRefresh.LoginAsync("headwiz", "password-one");
		beforeRefresh.SetActiveCharacter(Beta);
		await beforeRefresh.LogoutAsync();

		// sessionStorage is tab-scoped and outlives the logout; a fresh service must come up anonymous.
		var afterRefresh = MakeService(storage, [Alpha, Beta]);
		await afterRefresh.InitAsync();

		await Assert.That(afterRefresh.ActiveCharacter).IsNull();
	}
}
