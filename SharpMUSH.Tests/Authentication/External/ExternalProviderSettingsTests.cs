using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Server.Authentication.External;

namespace SharpMUSH.Tests.Authentication.External;

public class ExternalProviderSettingsTests
{
	[Test]
	public async Task FixedProvidersAreDisabledByDefaultAndNeverRequestEmailOrBotScopes()
	{
		await Assert.That(ExternalProviderDefinition.All.Select(p => p.Id)).IsEquivalentTo(["discord", "google", "microsoft", "grapevine", "github"]);
		foreach (var provider in ExternalProviderDefinition.All)
		{
			await Assert.That(new ExternalProviderSettings().Enabled).IsFalse();
			await Assert.That(provider.Scopes.Any(scope => scope is "email" or "bot" or "guilds" or "offline_access")).IsFalse();
			await Assert.That(provider.CallbackPath).IsEqualTo("/signin-external-" + provider.Id);
		}
		await Assert.That(ExternalProviderDefinition.Get("grapevine").RequiresClientSecret).IsFalse();
		await Assert.That(ExternalProviderDefinition.Get("grapevine").UserInformationEndpoint).IsEqualTo("https://grapevine.haus/users/me");
		// A GitHub token with no scope reads the public profile only, never email or repositories.
		await Assert.That(ExternalProviderDefinition.Get("github").Scopes).IsEmpty();
		await Assert.That(ExternalProviderDefinition.Get("github").RequiresClientSecret).IsTrue();
	}

	[Test]
	[Arguments("http://game.example")]
	[Arguments("https://user:password@game.example")]
	[Arguments("https://game.example/path")]
	[Arguments("https://game.example/?redirect=https://other.example")]
	[Arguments("https://game.example/#fragment")]
	[Arguments("//game.example")]
	public async Task EnabledProviderRefusesUntrustedProductionOrigin(string origin)
	{
		ExternalProviderValidation.Validate("google", new ExternalProviderSettings { Enabled = true, ClientId = "client", PublicOrigin = origin }, false).Expect<Error<string>>();
		await Task.CompletedTask;
	}

	[Test]
	public async Task HttpsOriginIsCanonicalAndLoopbackHttpIsDevelopmentOnly()
	{
		var settings = ExternalProviderValidation.Validate("google", new ExternalProviderSettings
		{ Enabled = true, ClientId = "client", PublicOrigin = "https://game.example/" }, false).Expect<ExternalProviderSettings>();
		await Assert.That(settings.PublicOrigin).IsEqualTo("https://game.example");
		ExternalProviderValidation.Validate("google", settings with { PublicOrigin = "http://localhost:8081" }, false).Expect<Error<string>>();
		ExternalProviderValidation.Validate("google", settings with { PublicOrigin = "http://localhost:8081" }, true).Expect<ExternalProviderSettings>();
	}

	[Test]
	public async Task MicrosoftAuthorityAndTenantRestrictionsAreConstrained()
	{
		var settings = new ExternalProviderSettings { Enabled = true, ClientId = "client", PublicOrigin = "https://game.example" };
		foreach (var audience in new[] { "common", "organizations", "consumers", "72f988bf-86f1-41af-91ab-2d7cd011db47" })
			ExternalProviderValidation.Validate("microsoft", settings with { MicrosoftAudience = audience }, false).Expect<ExternalProviderSettings>();
		ExternalProviderValidation.Validate("microsoft", settings with { MicrosoftAudience = "https://attacker.example" }, false).Expect<Error<string>>();
		ExternalProviderValidation.Validate("microsoft", settings with { AllowedTenants = ["not-a-tenant"] }, false).Expect<Error<string>>();
		await Task.CompletedTask;
	}
}
