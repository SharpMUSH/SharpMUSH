using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
file sealed class FakeLoginHandler(IReadOnlyList<CharacterSummary> characters)
	: BindingAwareHandler(characters);

/// <summary>
/// Stands in for the server's half of the acting-character contract, because the client no longer has
/// a half of its own: the session token carries the binding, so the roster the server serves is the
/// only thing that says which character a tab acts as. This fake therefore tracks the binding the way
/// the real server does — the primary at login, the target after a switch — and marks it on every
/// roster it hands back. A fake that served an unmarked roster would be telling the client "you act as
/// nobody", which is exactly what it means now.
/// </summary>
file abstract class BindingAwareHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
{
	private readonly List<CharacterSummary> _roster = [.. characters];
	private int? _boundKey = characters.FirstOrDefault()?.DbrefNumber;

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath;

		if (request.Method == HttpMethod.Delete)
		{
			// Honour the unlink: the client re-reads the roster afterwards, and a fake that kept
			// serving the deleted character would hide whether the rebind picked a survivor.
			var deletedKey = int.Parse(path[(path.LastIndexOf('/') + 1)..]);
			_roster.RemoveAll(c => c.DbrefNumber == deletedKey);
			if (_boundKey == deletedKey) _boundKey = null;
			return new HttpResponseMessage(HttpStatusCode.OK);
		}

		if (path.EndsWith("api/auth/switch-character", StringComparison.Ordinal))
		{
			var body = await request.Content!.ReadAsStringAsync(cancellationToken);
			using var parsed = JsonDocument.Parse(body);
			_boundKey = parsed.RootElement.GetProperty("characterKey").GetInt32();

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new { ott = "ott-1", expiresIn = 60, accountSessionToken = $"bound-to-{_boundKey}" })
			};
		}

		var marked = _roster
			.Select(c => new { c.DbrefNumber, c.CreationTime, c.Name, c.Flags, isActing = c.DbrefNumber == _boundKey })
			.ToList();

		// The bare-array roster endpoint and the login envelope both carry the marker.
		if (path.EndsWith("api/account/characters", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(marked) };

		return new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new
			{
				accountId = "acct-1",
				username = "headwiz",
				characters = marked,
				accountSessionToken = "session-token-1",
				mustChangePassword = false,
				role = "God",
				permissions = new[] { "*" },
			})
		};
	}
}

/// <summary>
/// Adds nothing to <see cref="BindingAwareHandler"/> beyond a name the Unlink tests read better with;
/// the DELETE and switch routes it needs are already there.
/// </summary>
file sealed class FakeAccountHandler(IReadOnlyList<CharacterSummary> characters)
	: BindingAwareHandler(characters);

public class AccountAuthServiceActiveCharacterTests
{
	private static AccountAuthService MakeService() =>
		new(Substitute.For<IHttpClientFactory>(),
			Substitute.For<IJSRuntime>(),
			Substitute.For<ILogger<AccountAuthService>>(),
			Substitute.For<ITerminalService>(),
			Substitute.For<IPlayTerminalService>());

	private static HttpClient MakeLoginHttpClient(IReadOnlyList<CharacterSummary> characters) =>
		new(new FakeLoginHandler(characters)) { BaseAddress = new Uri("https://localhost:8081/") };

	/// <summary>
	/// Logs a fresh <see cref="AccountAuthService"/> in against a fixed roster and leaves it ready
	/// for <see cref="AccountAuthService.UnlinkCharacterAsync"/> calls against the same
	/// <paramref name="characters"/> list (unlink's DELETE response is empty; the service derives
	/// the post-unlink roster locally by filtering <see cref="AccountAuthService.Characters"/>).
	/// </summary>
	/// <remarks>
	/// Calls <see cref="AccountAuthService.InitAsync"/> BEFORE logging in, and deliberately does not
	/// configure the <see cref="IJSRuntime"/> mock's sessionStorage reads. <c>InitAsync</c> is
	/// single-flight (<c>_initTask ??= InitCoreAsync()</c>): running it once here up front consumes
	/// that single execution — with an unconfigured JS mock returning null "no session" — safely,
	/// under a plain, non-Bunit <c>NSubstitute.For&lt;IJSRuntime&gt;()</c>. Every LATER call this
	/// service makes to <c>InitAsync</c> internally (which
	/// <see cref="AccountAuthService.UnlinkCharacterAsync"/> does) just awaits the same already-
	/// completed task instead of re-running <c>InitCoreAsync</c>, so it can never clobber the real
	/// <c>AccountSessionToken</c> that <see cref="AccountAuthService.LoginAsync"/> sets afterward.
	/// </remarks>
	private static async Task<AccountAuthService> MakeUnlinkableServiceAsync(IReadOnlyList<CharacterSummary> characters)
	{
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		// Not disposed here on purpose: the returned service keeps calling through this same
		// HttpClient (e.g. for UnlinkCharacterAsync) after this helper returns.
		var http = new HttpClient(new FakeAccountHandler(characters)) { BaseAddress = new Uri("https://localhost:8081/") };
		httpClientFactory.CreateClient("api").Returns(http);

		var sut = new AccountAuthService(httpClientFactory, Substitute.For<IJSRuntime>(), Substitute.For<ILogger<AccountAuthService>>(), Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());
		await sut.InitAsync();
		await sut.LoginAsync("headwiz", "password-one");
		return sut;
	}

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
	public async Task IsLoggedIn_is_false_without_a_session_even_with_an_active_character()
	{
		var sut = MakeService();
		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		// AccountSessionToken was never set, so this tab has no session regardless of ActiveCharacter.
		await Assert.That(sut.IsLoggedIn).IsFalse();
		await Assert.That(sut.ActiveCharacter).IsNotNull();
	}

	[Test]
	public async Task Characters_is_empty_on_an_empty_roster()
	{
		var sut = MakeService();
		await Assert.That(sut.Characters.Count).IsEqualTo(0);
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

		var sut = new AccountAuthService(httpClientFactory, Substitute.For<IJSRuntime>(), Substitute.For<ILogger<AccountAuthService>>(), Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());

		var (success, _, characters) = await sut.LoginAsync("headwiz", "password-one");

		await Assert.That(success).IsTrue();
		await Assert.That(characters.Count).IsEqualTo(3);
		await Assert.That(sut.ActiveCharacter).IsNotNull();
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(first.DbrefNumber);
	}

	/// <summary>
	/// The active-character preservation guard on the roster default: once a character is active
	/// AND still present in a reloaded roster, a later roster load (a second
	/// <see cref="AccountAuthService.LoginAsync"/> round-trip, standing in for any of the public
	/// methods that route through <c>SetCharacters</c>) must NOT re-default
	/// <see cref="AccountAuthService.ActiveCharacter"/> back to the new roster's first entry, even
	/// though the active character is no longer first.
	/// </summary>
	/// <remarks>
	/// This replaces a weaker predecessor test that asserted the same "stays put" outcome across a
	/// completely DISJOINT roster reload (the active character absent from the new roster entirely).
	/// That was encoding an incomplete contract: the original guard was "only reseat when nothing is
	/// active," which happily left <see cref="AccountAuthService.ActiveCharacter"/> naming a
	/// character the account no longer owns whenever the roster changed out from under it — exactly
	/// the bug branch-review Finding 1 reproduced live via <c>UnlinkCharacterAsync</c> on the active
	/// character. The corrected invariant in <c>SetCharacters</c> re-validates membership on every
	/// assignment: present-but-reordered stays active (this test); absent gets reseated (covered by
	/// <see cref="UnlinkCharacterAsync_ActiveCharacter_ReseatsToFirstRemaining"/> and its siblings
	/// below, which exercise the disjoint case that the old test wrongly asserted the opposite of).
	/// </remarks>
	[Test]
	public async Task LoginAsync_RebindsToThePrimaryTheServerMarks()
	{
		var a = new CharacterSummary(1, 100L, "A", "");
		var b = new CharacterSummary(2, 200L, "B", "");
		var c = new CharacterSummary(3, 300L, "C", "");

		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		using var firstHttp = MakeLoginHttpClient([a, b]);
		httpClientFactory.CreateClient("api").Returns(firstHttp);

		var sut = new AccountAuthService(httpClientFactory, Substitute.For<IJSRuntime>(), Substitute.For<ILogger<AccountAuthService>>(), Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());
		await sut.LoginAsync("headwiz", "password-one");
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(a.DbrefNumber);

		// A local switch is not what makes a character acting any more — the token is, and this one is
		// still bound to A. Setting it here only moves what the UI shows until the server speaks again.
		sut.SetActiveCharacter(b);
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(b.DbrefNumber);

		// Log in afresh against a roster where C is primary. A login mints a token bound to the
		// primary, so the reloaded roster marks C — and the marker, not the previous local value, is
		// the answer. (That a real switch survives a reload is pinned by the switch tests, where the
		// token carries it.)
		using var secondHttp = MakeLoginHttpClient([c, b]);
		httpClientFactory.CreateClient("api").Returns(secondHttp);

		await sut.LoginAsync("headwiz", "password-one");

		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(c.DbrefNumber);
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

	/// <summary>
	/// Branch-review Finding 1 (a): unlinking the ACTIVE character must reseat
	/// <see cref="AccountAuthService.ActiveCharacter"/> to the post-unlink roster's first entry —
	/// not leave it naming a character the account no longer owns. This is the live bug: the panel's
	/// Account Management link (<c>Account.razor:273</c>) routes here.
	/// </summary>
	[Test]
	public async Task UnlinkCharacterAsync_ActiveCharacter_ReseatsToFirstRemaining()
	{
		var a = new CharacterSummary(1, 100L, "A", "");
		var b = new CharacterSummary(2, 200L, "B", "");
		var c = new CharacterSummary(3, 300L, "C", "");
		var sut = await MakeUnlinkableServiceAsync([a, b, c]);
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(a.DbrefNumber);

		var (success, error) = await sut.UnlinkCharacterAsync(a.DbrefNumber);

		await Assert.That(success).IsTrue();
		await Assert.That(error).IsNull();
		await Assert.That(sut.Characters.Select(x => x.DbrefNumber)).IsEquivalentTo([b.DbrefNumber, c.DbrefNumber]);
		await Assert.That(sut.ActiveCharacter).IsNotNull();
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(b.DbrefNumber);
	}

	/// <summary>
	/// Branch-review Finding 1 (b): unlinking the LAST character must leave
	/// <see cref="AccountAuthService.ActiveCharacter"/> null AND <see cref="AccountAuthService.Characters"/>
	/// empty — the spec's state table treats "no characters" and "an active character" as mutually
	/// exclusive, so this must never leave an empty roster while <c>ActiveCharacter</c> still names
	/// something.
	/// </summary>
	[Test]
	public async Task UnlinkCharacterAsync_LastCharacter_LeavesNoActiveCharacterAndEmptyRoster()
	{
		var a = new CharacterSummary(1, 100L, "A", "");
		var sut = await MakeUnlinkableServiceAsync([a]);
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(a.DbrefNumber);
		await Assert.That(sut.IsLoggedIn).IsTrue();

		var (success, error) = await sut.UnlinkCharacterAsync(a.DbrefNumber);

		await Assert.That(success).IsTrue();
		await Assert.That(error).IsNull();
		await Assert.That(sut.Characters).IsEmpty();
		await Assert.That(sut.ActiveCharacter).IsNull();
	}

	/// <summary>
	/// Branch-review Finding 1 (c): unlinking a NON-active character must leave
	/// <see cref="AccountAuthService.ActiveCharacter"/> completely untouched — only the roster
	/// shrinks.
	/// </summary>
	[Test]
	public async Task UnlinkCharacterAsync_NonActiveCharacter_LeavesActiveCharacterUntouched()
	{
		var a = new CharacterSummary(1, 100L, "A", "");
		var b = new CharacterSummary(2, 200L, "B", "");
		var c = new CharacterSummary(3, 300L, "C", "");
		var sut = await MakeUnlinkableServiceAsync([a, b, c]);
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(a.DbrefNumber);

		var (success, error) = await sut.UnlinkCharacterAsync(b.DbrefNumber);

		await Assert.That(success).IsTrue();
		await Assert.That(error).IsNull();
		await Assert.That(sut.Characters.Select(x => x.DbrefNumber)).IsEquivalentTo([a.DbrefNumber, c.DbrefNumber]);
		await Assert.That(sut.ActiveCharacter).IsNotNull();
		await Assert.That(sut.ActiveCharacter!.DbrefNumber).IsEqualTo(a.DbrefNumber);
	}
}
