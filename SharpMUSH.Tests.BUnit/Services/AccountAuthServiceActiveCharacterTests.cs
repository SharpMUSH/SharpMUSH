using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Fakes a successful api/auth/account-login round-trip carrying a caller-supplied character
/// roster, so tests can drive <see cref="AccountAuthService.LoginAsync"/> end-to-end instead of
/// calling the private roster-defaulting logic's only public entry point indirectly.
/// </summary>
file sealed class FakeLoginHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new
			{
				accountId = "acct-1",
				username = "headwiz",
				characters,
				accountSessionToken = "session-token-1",
				mustChangePassword = false,
				role = "God",
				permissions = new[] { "*" },
			})
		});
}

public class AccountAuthServiceActiveCharacterTests
{
	private static AccountAuthService MakeService() =>
		new(Substitute.For<IHttpClientFactory>(),
			Substitute.For<IJSRuntime>(),
			Substitute.For<ILogger<AccountAuthService>>());

	private static HttpClient MakeLoginHttpClient(IReadOnlyList<CharacterSummary> characters) =>
		new(new FakeLoginHandler(characters)) { BaseAddress = new Uri("https://localhost:8081/") };

	[Test]
	public async Task SetActiveCharacter_raises_ActiveCharacterChanged()
	{
		var sut = MakeService();
		var raised = 0;
		sut.ActiveCharacterChanged += () => raised++;

		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		await Assert.That(raised).IsEqualTo(1);
		await Assert.That(sut.ActiveCharacter!.Name).IsEqualTo("Wizard");
	}

	[Test]
	public async Task SetActiveCharacter_to_same_character_does_not_re_raise()
	{
		var sut = MakeService();
		var ch = new CharacterSummary(7, 1000L, "Wizard", "");
		sut.SetActiveCharacter(ch);
		var raised = 0;
		sut.ActiveCharacterChanged += () => raised++;

		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		await Assert.That(raised).IsEqualTo(0);
	}

	[Test]
	public async Task CanUseTerminal_is_false_without_a_session()
	{
		var sut = MakeService();
		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		// IsLoggedIn is false: AccountSessionToken was never set.
		await Assert.That(sut.CanUseTerminal).IsFalse();
	}

	[Test]
	public async Task HasCharacters_is_false_on_an_empty_roster()
	{
		var sut = MakeService();
		await Assert.That(sut.HasCharacters).IsFalse();
	}

	[Test]
	public async Task SwitchCharacterAsync_without_a_session_leaves_ActiveCharacter_untouched()
	{
		var sut = MakeService();
		var before = sut.ActiveCharacter;

		var ott = await sut.SwitchCharacterAsync(new CharacterSummary(7, 1000L, "Wizard", ""));

		await Assert.That(ott).IsNull();
		await Assert.That(sut.ActiveCharacter).IsEqualTo(before);
	}

	/// <summary>
	/// Exercises the roster-defaulting logic (the private <c>SetCharacters</c> helper) through its
	/// public entry point rather than by driving <see cref="AccountAuthService.SetActiveCharacter"/>
	/// directly: a multi-character roster hydrated via <see cref="AccountAuthService.LoginAsync"/>
	/// must leave <see cref="AccountAuthService.ActiveCharacter"/> on the roster's FIRST entry.
	/// </summary>
	[Test]
	public async Task LoginAsync_MultiCharacterRoster_DefaultsActiveCharacterToFirstEntry()
	{
		var first = new CharacterSummary(1, 100L, "First", "");
		var second = new CharacterSummary(2, 200L, "Second", "");
		var third = new CharacterSummary(3, 300L, "Third", "");

		using var http = MakeLoginHttpClient([first, second, third]);
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient("api").Returns(http);

		var sut = new AccountAuthService(httpClientFactory, Substitute.For<IJSRuntime>(), Substitute.For<ILogger<AccountAuthService>>());

		var (success, _, characters) = await sut.LoginAsync("headwiz", "password-one");

		await Assert.That(success).IsTrue();
		await Assert.That(characters.Count).IsEqualTo(3);
		await Assert.That(sut.ActiveCharacter).IsNotNull();
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(first.DbrefNumber);
	}

	/// <summary>
	/// The only-when-null guard on the roster default: once a character is active, a later roster
	/// load (a second <see cref="AccountAuthService.LoginAsync"/> round-trip, standing in for any of
	/// the seven public methods that route through <c>SetCharacters</c>) must NOT re-default
	/// <see cref="AccountAuthService.ActiveCharacter"/> back to the new roster's first entry.
	/// </summary>
	[Test]
	public async Task LoginAsync_ActiveCharacterAlreadySet_SubsequentRosterLoadDoesNotResetIt()
	{
		var a = new CharacterSummary(1, 100L, "A", "");
		var b = new CharacterSummary(2, 200L, "B", "");
		var c = new CharacterSummary(3, 300L, "C", "");
		var d = new CharacterSummary(4, 400L, "D", "");

		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		using var firstHttp = MakeLoginHttpClient([a, b]);
		httpClientFactory.CreateClient("api").Returns(firstHttp);

		var sut = new AccountAuthService(httpClientFactory, Substitute.For<IJSRuntime>(), Substitute.For<ILogger<AccountAuthService>>());
		await sut.LoginAsync("headwiz", "password-one");
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(a.DbrefNumber);

		// Simulate the user having switched to a different character before the roster reloads.
		sut.SetActiveCharacter(b);
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(b.DbrefNumber);

		using var secondHttp = MakeLoginHttpClient([c, d]);
		httpClientFactory.CreateClient("api").Returns(secondHttp);

		await sut.LoginAsync("headwiz", "password-one");

		// The new roster's first entry is C — if the only-when-null guard regressed, ActiveCharacter
		// would flip back to it. It must instead stay on the already-active B.
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(b.DbrefNumber);
	}

	/// <summary>
	/// Pins the idempotency check in <see cref="AccountAuthService.SetActiveCharacter"/> to AND, not
	/// OR. Every other test in this file only exercises fully-null-vs-fully-populated transitions,
	/// where AND and OR agree. A recycled dbref (same <c>DbrefNumber</c>, different
	/// <c>CreationTime</c>) is the case where they diverge: under a buggy OR, matching DbrefNumber
	/// alone would short-circuit the early return and <see cref="AccountAuthService.ActiveCharacterChanged"/>
	/// would never fire.
	/// </summary>
	[Test]
	public async Task SetActiveCharacter_same_dbref_different_creationTime_still_raises_changed()
	{
		var sut = MakeService();
		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Old", ""));
		var raised = 0;
		sut.ActiveCharacterChanged += () => raised++;

		// Recycled dbref: same DbrefNumber, different CreationTime — must be treated as a distinct character.
		sut.SetActiveCharacter(new CharacterSummary(7, 2000L, "New", ""));

		await Assert.That(raised).IsEqualTo(1);
		await Assert.That(sut.ActiveCharacter!.CreationTime).IsEqualTo(2000L);
	}
}
