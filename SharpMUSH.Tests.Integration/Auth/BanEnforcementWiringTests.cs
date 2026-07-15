using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// End-to-end proof, through the real DI container, that Task 12's two enforcement call sites —
/// <see cref="SharpMUSH.Library.Services.AccountService.DisableAccountAsync"/> and
/// <see cref="SitelockController.AddSitelockRule"/> — actually invoke <see cref="IBanEnforcer"/>.
/// <para>
/// Both tests observe a <see cref="HubConnectionRegistry"/> abort: that side effect is only ever
/// produced by <c>BanEnforcementService</c> (see <c>BanEnforcementServiceTests</c> in
/// <c>SharpMUSH.Tests</c> for its unit coverage), never by the account-disable/sitelock-persist code
/// on its own, so it cleanly proves the wiring exists. This mirrors <see cref="BanEnforcementTests"/>'
/// approach of asserting real, observable side effects instead of substituting <c>IMessageBus</c> in
/// this shared, session-wide test host (see that class's remarks for why that substitution would
/// leak into every other test that shares it).
/// </para>
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class BanEnforcementWiringTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record AccountRegisterRequest(string Username, string? Email, string Password);

	private const string Password = "Integration-Test-Pw-1!";

	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

	private async Task<string> RegisterAccountIdAsync()
	{
		var http = CreateClient();
		var response = await http.PostAsJsonAsync(
			"api/auth/account-register",
			new AccountRegisterRequest(UniqueName("acct"), null, Password));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(account).IsNotNull();
		return account!.AccountId;
	}

	[Test]
	public async Task DisableAccount_AbortsLiveSignalRConnectionForAccount()
	{
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		var registry = factory.Services.GetRequiredService<HubConnectionRegistry>();
		var accountId = await RegisterAccountIdAsync();

		// No enforcement fan-out (session revoke alone) ever touches HubConnectionRegistry, so an
		// abort firing here can only be explained by AccountService.DisableAccountAsync reaching
		// IBanEnforcer.EnforceAccountBanAsync.
		var aborted = false;
		registry.Add($"conn-{Guid.NewGuid()}", accountId, "203.0.113.5", () => aborted = true);

		var result = await accountService.DisableAccountAsync(accountId);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(aborted).IsTrue();
	}

	// Mutates the shared IOptionsWrapper<SharpMUSHOptions>-backed SitelockRules via
	// ConfigurationReloadService.SignalChange(), so it shares the "ConfigMutation" NotInParallel
	// group with the other suites that touch the same shared config (see LoginsConfigApiTests).
	[Test, NotInParallel("ConfigMutation")]
	public async Task AddSitelockRule_AbortsLiveSignalRConnectionForMatchingIp()
	{
		var database = factory.Services.GetRequiredService<ISharpDatabase>();
		var optionsWrapper = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var configReloadService = factory.Services.GetRequiredService<ConfigurationReloadService>();
		var banEnforcer = factory.Services.GetRequiredService<IBanEnforcer>();
		var logger = factory.Services.GetRequiredService<ILogger<SitelockController>>();
		var registry = factory.Services.GetRequiredService<HubConnectionRegistry>();

		// Constructed directly (bypassing HTTP/[Authorize]), same pattern as
		// SharpMUSH.Tests.Configuration.ConfigurationControllerTests: real DI-resolved
		// collaborators, calling the action method straight.
		var controller = new SitelockController(optionsWrapper, database, configReloadService, banEnforcer, logger);

		var hostPattern = $"203.0.113.{Random.Shared.Next(1, 254)}";
		var aborted = false;
		registry.Add($"conn-{Guid.NewGuid()}", "accounts/irrelevant", hostPattern, () => aborted = true);

		var result = await controller.AddSitelockRule(hostPattern, ["!connect"]);

		await Assert.That(result).IsNotNull();
		await Assert.That(aborted).IsTrue();
	}
}
