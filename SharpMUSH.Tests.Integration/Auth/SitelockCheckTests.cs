using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// Task 15: verifies sitelock rules gate the auth surfaces (game connect/login, registration,
/// guest logins) while anonymous browsing (any non-auth page/endpoint) stays open. Covers both
/// the REST surfaces (<c>AuthController</c>, <c>SetupController</c>) and the telnet surface
/// (<c>SocketCommands.Connect</c>/<c>HandleGuestLogin</c>).
///
/// The REST tests re-stub the shared <see cref="IOptionsWrapper{SharpMUSHOptions}"/> substitute's
/// <c>CurrentValue</c> around the call under test and restore it in a finally block —
/// <c>[NotInParallel("ConfigMutation")]</c> keeps them from racing other suites that do the same
/// (see <c>LoginsConfigApiTests</c>).
///
/// The sitelock rule in each blocking test targets the literal client IP the TestServer's
/// in-process HttpClient resolves to (discovered per-test via the dev-only
/// <c>GET api/debug/client-ip</c> endpoint from Task 14 — see <c>ForwardedHeadersTests</c>) rather
/// than a guessed loopback literal, so the test is agnostic to whatever TestServer happens to
/// report ("::1", "127.0.0.1", or otherwise). The telnet tests instead register a connection
/// handle directly with an arbitrary RFC 5737 documentation-range IP, since telnet connections
/// carry their origin IP as connection metadata rather than an HttpContext.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class SitelockCheckTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);
	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record AccountLoginRequest(string UsernameOrEmail, string Password);
	private record CreateCharacterRequest(string Name, string Password);
	private record MushTokenRequest(string? PlayerName, string? Password, string? AccountSessionToken, int? CharacterKey, long? CharacterCreationTime);
	private record SetupCompleteRequest(string Username, string Password);

	private const string Password = "Integration-Test-Pw-1!";

	/// <summary>A host pattern guaranteed never to match the TestServer's real client IP (TEST-NET-3, RFC 5737).</summary>
	private const string NonMatchingHost = "203.0.113.5";

	private IOptionsWrapper<SharpMUSHOptions> Options => factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
	private IMUSHCodeParser Parser => factory.CommandParser;
	private IConnectionService ConnectionService => factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();
	private INotifyService NotifyService => factory.Services.GetRequiredService<INotifyService>();

	private static long _nextTelnetHandle = 90_000;

	/// <summary>
	/// Test client pinned to the https base address. The server uses UseHttpsRedirection;
	/// following the 307 from http→https makes HttpClient drop the Authorization header,
	/// which breaks every bearer-authenticated endpoint in these tests.
	/// </summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

	/// <summary>
	/// Discovers the client IP this HttpClient resolves to server-side (Task 14's debug endpoint),
	/// mapped through the exact same fallback <c>AuthController.ClientIp()</c>/<c>SetupController.Complete</c>
	/// apply in production. Empirically, this in-process TestServer's <c>HttpContext.Connection.RemoteIpAddress</c>
	/// is null (the debug endpoint's raw echo comes back empty) — production code's own
	/// <c>?? "unknown"</c> fallback is what actually reaches the sitelock guard in that case, so the
	/// rule under test must target that literal, not the raw (empty) echo.
	/// </summary>
	private static async Task<string> GetClientIpAsync(HttpClient http)
	{
		using var response = await http.GetAsync("api/debug/client-ip");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var raw = await response.Content.ReadAsStringAsync();
		return string.IsNullOrEmpty(raw) ? "unknown" : raw;
	}

	private async Task<(HttpClient Http, AccountLoginResponse Account)> RegisterAccountAsync()
	{
		var http = CreateClient();
		var response = await http.PostAsJsonAsync(
			"api/auth/account-register",
			new AccountRegisterRequest(UniqueName("acct"), null, Password));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(account).IsNotNull();
		return (http, account!);
	}

	private static async Task<CreatedCharacterResponse> CreateCharacterAsync(
		HttpClient http, string sessionToken, string name, string password)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(name, password)),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!;
	}

	/// <summary>Re-stubs the shared options substitute's SitelockRules; caller restores in a finally block.</summary>
	private (IOptionsWrapper<SharpMUSHOptions> Options, SharpMUSHOptions Original) StubSitelockRules(
		Dictionary<string, string[]> rules)
	{
		var options = Options;
		var original = options.CurrentValue;
		options.CurrentValue.Returns(original with { SitelockRules = new SitelockRulesOptions(rules) });
		return (options, original);
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task AccountLogin_FromSitelockedIp_Returns403()
	{
		var (http, account) = await RegisterAccountAsync();
		await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Pleb"), Password);

		var clientIp = await GetClientIpAsync(http);
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [clientIp] = ["!connect"] });
		try
		{
			using var response = await http.PostAsJsonAsync("api/auth/account-login",
				new AccountLoginRequest(account.Username, Password));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	/// <summary>
	/// Anonymous browsing must never be gated by sitelock rules, even a maximally broad one. This
	/// hits <c>GET /health</c> — an unauthenticated, dependency-free public endpoint (Program.cs
	/// <c>MapGet("/health", ...)</c>) — rather than <c>GET /</c>, since the SPA's <c>index.html</c>
	/// is not guaranteed to exist in the test build's output; <c>/health</c> is a reliable stand-in
	/// for "any non-auth page GET" that proves the sitelock guard was never even consulted.
	/// </summary>
	[Test, NotInParallel("ConfigMutation")]
	public async Task AnonymousHealthCheck_StillReturns200_WhenBroadSitelockRuleConfigured()
	{
		var http = CreateClient();
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { ["*"] = ["!connect", "!create", "!guest"] });
		try
		{
			using var response = await http.GetAsync("health");
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("healthy");
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task AccountRegister_FromCreateSitelockedIp_Returns403()
	{
		var http = CreateClient();
		var clientIp = await GetClientIpAsync(http);
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [clientIp] = ["!create"] });
		try
		{
			using var response = await http.PostAsJsonAsync("api/auth/account-register",
				new AccountRegisterRequest(UniqueName("blocked"), null, Password));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task AccountLogin_FromNonMatchingIp_LogsInFine()
	{
		var (http, account) = await RegisterAccountAsync();
		await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Pleb"), Password);

		// The rule targets a documentation-range IP (RFC 5737) that can never be the TestServer's
		// real client IP, so this login must succeed exactly as if no sitelock rule existed.
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [NonMatchingHost] = ["!connect"] });
		try
		{
			using var response = await http.PostAsJsonAsync("api/auth/account-login",
				new AccountLoginRequest(account.Username, Password));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task GetMushToken_FromSitelockedIp_Returns403()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Ott"), Password);

		var clientIp = await GetClientIpAsync(http);
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [clientIp] = ["!connect"] });
		try
		{
			using var response = await http.PostAsJsonAsync("api/auth/mush-token",
				new MushTokenRequest(null, null, account.AccountSessionToken, character.DbrefNumber, character.CreationTime));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	/// <summary>
	/// Shares <c>SetupFlowTests</c>' <c>"SetupFlow"</c> serialization domain (it flips the same
	/// game-wide <c>ServerState.SetupCompleted</c> flag) as well as <c>"ConfigMutation"</c> (the
	/// sitelock rule re-stub) — both keys are required so this test never races either family.
	/// Uses a high explicit Order so it always runs after every other <c>"SetupFlow"</c> test
	/// (max existing Order is 6), leaving setup completed again afterward for the rest of the suite.
	/// </summary>
	[Test, NotInParallel(["SetupFlow", "ConfigMutation"], Order = 50)]
	public async Task SetupComplete_FromCreateSitelockedIp_Returns403()
	{
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		await db.SetServerSetupCompletedAsync(false);

		var http = CreateClient();
		var clientIp = await GetClientIpAsync(http);
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [clientIp] = ["!create"] });
		try
		{
			using var response = await http.PostAsJsonAsync("api/setup/complete",
				new SetupCompleteRequest(UniqueName("sitelocked"), "claimed-password-1"));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
			await db.SetServerSetupCompletedAsync(true);
		}
	}

	// ── Telnet surface (SocketCommands.Connect / HandleGuestLogin) ──────────────────────────────

	/// <summary>Registers a fresh, not-yet-connected telnet-style handle carrying a specific origin IP.</summary>
	private async Task<long> RegisterTelnetHandleAsync(string ip)
	{
		var handle = Interlocked.Increment(ref _nextTelnetHandle);
		var metadata = new ConcurrentDictionary<string, string>(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["InternetProtocolAddress"] = ip,
			["HostName"] = ip,
			["ConnectionType"] = "telnet",
		});
		await ConnectionService.Register(handle, ip, ip, "telnet",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			metadata);
		return handle;
	}

	private async Task<string> CreateTelnetPlayerAsync(string namePrefix, string password)
	{
		var defaultHome = new DBRef((int)Options.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Options.CurrentValue.Limit.StartingQuota;
		var playerName = UniqueName(namePrefix);
		await Mediator.Send(new CreatePlayerCommand(playerName, password, defaultHome, defaultHome, startingQuota));
		return playerName;
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task TelnetConnect_FromSitelockedIp_Refused()
	{
		var playerName = await CreateTelnetPlayerAsync("SLPleb", "pleb-password-1");

		const string blockedIp = "198.51.100.7"; // RFC 5737 TEST-NET-2, never a real connecting IP
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [blockedIp] = ["!connect"] });
		try
		{
			var handle = await RegisterTelnetHandleAsync(blockedIp);
			await Parser.CommandParse(handle, ConnectionService, MModule.single($"connect {playerName} pleb-password-1"));

			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == handle),
				Arg.Is<OneOf<MString, string>>(s => SharpMUSH.Tests.TestHelpers.MessagePlainTextEquals(s, "Access from your location is restricted.")),
				null, INotifyService.NotificationType.Announce);

			// Never bound to the player: the connection must still be anonymous.
			await Assert.That(ConnectionService.Get(handle)?.Ref).IsNull();
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task TelnetConnect_FromNonMatchingIp_LogsInFine()
	{
		var playerName = await CreateTelnetPlayerAsync("SLOk", "pleb-password-1");

		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [NonMatchingHost] = ["!connect"] });
		try
		{
			var handle = await RegisterTelnetHandleAsync("192.0.2.9"); // RFC 5737 TEST-NET-1, does not match NonMatchingHost
			await Parser.CommandParse(handle, ConnectionService, MModule.single($"connect {playerName} pleb-password-1"));

			await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task TelnetGuestLogin_FromGuestSitelockedIp_Refused()
	{
		const string blockedIp = "203.0.113.77"; // RFC 5737 TEST-NET-3
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [blockedIp] = ["!guest"] });
		try
		{
			var handle = await RegisterTelnetHandleAsync(blockedIp);
			await Parser.CommandParse(handle, ConnectionService, MModule.single("connect guest"));

			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == handle),
				Arg.Is<OneOf<MString, string>>(s => SharpMUSH.Tests.TestHelpers.MessagePlainTextEquals(s, "Access from your location is restricted.")),
				null, INotifyService.NotificationType.Announce);

			await Assert.That(ConnectionService.Get(handle)?.Ref).IsNull();
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	/// <summary>
	/// The telnet account-mode REGISTER surface is gated on <c>!create</c> (its sibling LOGIN/PLAY
	/// on <c>!connect</c>, MAKE on <c>!create</c>). The gate is the first statement in the command,
	/// so it fires through <c>CommandParse</c> even though these commands' multi-word args cannot be
	/// split into separate positional slots (a pre-existing NoParse single-arg-grammar limitation,
	/// unrelated to this gate — see PlayerCreationConfigTests, which likewise reaches REGISTER/MAKE's
	/// pre-arg checks via CommandParse). We assert the block message fires and no account is created.
	/// </summary>
	[Test, NotInParallel("ConfigMutation")]
	public async Task TelnetRegister_FromCreateSitelockedIp_Refused()
	{
		const string blockedIp = "198.51.100.33"; // RFC 5737 TEST-NET-2
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [blockedIp] = ["!create"] });
		try
		{
			var handle = await RegisterTelnetHandleAsync(blockedIp);
			await Parser.CommandParse(handle, ConnectionService, MModule.single("register SitelockedAcct somepassword"));

			await NotifyService.Received(1).Notify(
				Arg.Is<long>(h => h == handle),
				Arg.Is<OneOf<MString, string>>(s => SharpMUSH.Tests.TestHelpers.MessagePlainTextEquals(s, "Access from your location is restricted.")),
				null, INotifyService.NotificationType.Announce);

			// The gate returned before any account mutation: the connection never entered AccountMode.
			await Assert.That(ConnectionService.Get(handle)?.State).IsNotEqualTo(IConnectionService.ConnectionState.AccountMode);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}
}
