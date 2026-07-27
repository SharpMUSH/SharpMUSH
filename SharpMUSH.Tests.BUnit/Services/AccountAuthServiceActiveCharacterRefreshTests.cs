using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
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

	/// <summary>When set, writes only apply after a real await — so a caller that leaves a write
	/// detached has not applied it by the time its own method returns.</summary>
	public bool AsyncWrites { get; set; }

	/// <summary>When set, writes park until the test releases them — so a caller that awaits a write
	/// cannot finish, while one that leaves it detached sails past.</summary>
	public TaskCompletionSource? GateWrites { get; set; }

	public void Seed(string key, string value) { lock (_items) { _items[key] = value; } }
	public bool Has(string key) { lock (_items) { return _items.ContainsKey(key); } }
	public string? Read(string key) { lock (_items) { return _items.TryGetValue(key, out var v) ? v : null; } }

	/// <summary>
	/// Holds the answer to a <c>getItem</c> for this key until the test releases it, modelling a real
	/// interop round-trip. The value is snapshotted when the call is made (as the JS side would), so
	/// writes that happen while the read is in flight do not change what it returns.
	/// </summary>
	public TaskCompletionSource? GateReadsOf { get; set; }
	public string? GatedKey { get; set; }

	/// <summary>Completed the moment the gated read is actually in flight.</summary>
	public TaskCompletionSource? ReadStarted { get; set; }

	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
	{
		var key = args?.Length > 0 ? args[0]?.ToString() : null;
		if (GateReadsOf is { } gate && identifier == "sessionStorage.getItem" && key == GatedKey)
		{
			var snapshot = Invoke<TValue>(identifier, args);
			ReadStarted?.TrySetResult();
			return new ValueTask<TValue>(gate.Task.ContinueWith(_ => snapshot, TaskScheduler.Default));
		}

		if (GateWrites is { } writeGate && identifier != "sessionStorage.getItem")
			return new ValueTask<TValue>(writeGate.Task.ContinueWith(_ => Invoke<TValue>(identifier, args), TaskScheduler.Default));

		if (AsyncWrites && identifier != "sessionStorage.getItem")
			return new ValueTask<TValue>(Task.Run(async () => { await Task.Yield(); return Invoke<TValue>(identifier, args); }));

		return ValueTask.FromResult(Invoke<TValue>(identifier, args));
	}

	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
		InvokeAsync<TValue>(identifier, args);

	private TValue Invoke<TValue>(string identifier, object?[]? args)
	{
		var key = args?.Length > 0 ? args[0]?.ToString() ?? string.Empty : string.Empty;

		lock (_items)
		{
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
	public async Task ALoginLandingDuringHydration_IsNotClobberedByTheStoredValue()
	{
		var storage = new FakeSessionStorage();
		CharacterSummary[] roster = [Alpha, Beta];

		var seed = MakeService(storage, roster);
		await seed.InitAsync();
		await seed.LoginAsync("headwiz", "password-one");
		seed.SetActiveCharacter(Alpha);

		// Reload. Hold the stored-character read open so a login can land mid-hydration.
		var afterRefresh = MakeService(storage, roster);
		storage.GatedKey = "sharpmush.account.activeCharacter";
		storage.GateReadsOf = new TaskCompletionSource();
		storage.ReadStarted = new TaskCompletionSource();

		var hydrating = afterRefresh.InitAsync();
		await storage.ReadStarted.Task;          // the stored-character read is now in flight
		afterRefresh.SetActiveCharacter(Beta);   // ...and a login lands right here
		storage.GateReadsOf.SetResult();
		await hydrating;

		// The live selection is newer than anything storage had to say.
		await Assert.That(afterRefresh.ActiveCharacter?.DbrefNumber).IsEqualTo(Beta.DbrefNumber);
	}

	[Test]
	public async Task ACorruptStoredCharacter_IsClearedSoItStopsRecurring()
	{
		var storage = new FakeSessionStorage();

		var seed = MakeService(storage, [Alpha, Beta]);
		await seed.InitAsync();
		await seed.LoginAsync("headwiz", "password-one");
		storage.Seed("sharpmush.account.activeCharacter", "{not json");

		// Park every write. Hydration must not be able to finish until the clear it issued completes —
		// a detached clear would let it sail straight past.
		storage.GateWrites = new TaskCompletionSource();

		var afterRefresh = MakeService(storage, [Alpha, Beta]);
		var hydrating = afterRefresh.InitAsync();
		var finishedWithoutTheClear = await Task.WhenAny(hydrating, Task.Delay(250)) == hydrating;

		storage.GateWrites.SetResult();
		await hydrating;
		storage.GateWrites = null;

		await Assert.That(finishedWithoutTheClear).IsFalse();

		// And the bad entry is gone, rather than re-logging on every reload.
		await Assert.That(storage.Has("sharpmush.account.activeCharacter")).IsFalse();

		// Hydration then falls back to the roster default, which re-seats storage with a valid value.
		await afterRefresh.GetCharactersAsync();
		await Assert.That(afterRefresh.ActiveCharacter?.DbrefNumber).IsEqualTo(Alpha.DbrefNumber);
		await Assert.That(storage.Read("sharpmush.account.activeCharacter")).Contains("\"Alpha\"");
	}

	[Test]
	public async Task Logout_LeavesNoStoredCharacterBehind()
	{
		var storage = new FakeSessionStorage();

		var svc = MakeService(storage, [Alpha, Beta]);
		await svc.InitAsync();
		await svc.LoginAsync("headwiz", "password-one");
		svc.SetActiveCharacter(Beta);

		// End-state cover only: in-process both the awaited removal and the detached one land before
		// LogoutAsync returns, so this cannot distinguish them. It guards the property that matters to
		// a user — no stale acting character survives a logout to be restored by the next login — while
		// the awaited removal in LogoutAsync is what makes that hold in a browser, where a navigation
		// right after logout can cut a detached continuation short.
		storage.AsyncWrites = true;
		await svc.LogoutAsync();

		await Assert.That(storage.Has("sharpmush.account.activeCharacter")).IsFalse();
	}

	[Test]
	public async Task ARenamedCharacter_IsReboundToTheRosterCopy()
	{
		var storage = new FakeSessionStorage();

		var seed = MakeService(storage, [Alpha, Beta]);
		await seed.InitAsync();
		await seed.LoginAsync("headwiz", "password-one");
		seed.SetActiveCharacter(Beta);

		// Beta was renamed server-side after the switch; the stored snapshot still says "Beta".
		var renamed = Beta with { Name = "BetaRenamed" };
		var afterRefresh = MakeService(storage, [Alpha, renamed]);
		await afterRefresh.InitAsync();
		await afterRefresh.GetCharactersAsync();

		await Assert.That(afterRefresh.ActiveCharacter?.DbrefNumber).IsEqualTo(Beta.DbrefNumber);
		await Assert.That(afterRefresh.ActiveCharacter?.Name).IsEqualTo("BetaRenamed");
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
