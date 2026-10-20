using System.Security.Claims;
using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Server.Authentication.External;
namespace SharpMUSH.Tests.Authentication.External;

public class ExternalIdentityAdapterTests
{
	private static EffectiveExternalProvider Provider(string id) => new(ExternalProviderDefinition.Get(id), new() { RegistrationId = "registration", MicrosoftAudience = "common" }, "generation", null);
	private static ClaimsPrincipal Claims(string subject, string issuer, string? tenant = null) => new(new ClaimsIdentity(new[] { new Claim("sub", subject), new Claim("iss", issuer) }.Concat(tenant is null ? [] : new[] { new Claim("tid", tenant) })));
	[Test]
	[Arguments("discord", "id")]
	[Arguments("grapevine", "uid")]
	public async Task OAuthUsesOnlyFixedIssuerAndStableSubject(string provider, string field)
	{
		using var json = JsonDocument.Parse("{\"" + field + "\":\"stable-subject\",\"username\":\"display\",\"email\":\"ignored@example.org\"}");
		var proof = ExternalIdentityAdapter.OAuth(Provider(provider), json.RootElement).Expect<ExternalIdentityProof>();
		await Assert.That(proof.Key).IsEqualTo(new ExternalIdentityKey("registration", Provider(provider).Definition.Issuer, "stable-subject"));
		await Assert.That(proof.Label).IsEqualTo("display");
		ExternalIdentityAdapter.OpenId(Provider(provider), Claims("stable-subject", "https://attacker.example")).Expect<Error<string>>();
	}
	[Test]
	public async Task GitHubUsesItsNumericIdAndLogin()
	{
		using var json = JsonDocument.Parse("{\"id\":583231,\"login\":\"octocat\",\"name\":\"The Octocat\"}");
		var proof = ExternalIdentityAdapter.OAuth(Provider("github"), json.RootElement).Expect<ExternalIdentityProof>();
		await Assert.That(proof.Key).IsEqualTo(new ExternalIdentityKey("registration", "https://github.com", "583231"));
		await Assert.That(proof.Label).IsEqualTo("octocat");
	}
	[Test]
	[Arguments("{\"id\":\"583231\"}")]
	[Arguments("{\"id\":0}")]
	[Arguments("{\"id\":1.5}")]
	[Arguments("{\"login\":\"octocat\"}")]
	public async Task InvalidGitHubIdentifiersAreRefused(string profile)
	{
		using var json = JsonDocument.Parse(profile);
		ExternalIdentityAdapter.OAuth(Provider("github"), json.RootElement).Expect<Error<string>>();
		await Task.CompletedTask;
	}
	[Test]
	[Arguments("{}")]
	[Arguments("{\"uid\":123}")]
	[Arguments("{\"uid\":\"\"}")]
	[Arguments("{\"uid\":\"line\\nbreak\"}")]
	public async Task InvalidOAuthIdentifiersAreRefused(string profile)
	{
		using var json = JsonDocument.Parse(profile);
		ExternalIdentityAdapter.OAuth(Provider("grapevine"), json.RootElement).Expect<Error<string>>();
		await Task.CompletedTask;
	}
	[Test]
	public async Task GoogleRequiresItsValidatedIssuerAndExactlyOneSubject()
	{
		ExternalIdentityAdapter.OpenId(Provider("google"), Claims("subject", "https://attacker.example")).Expect<Error<string>>();
		var alias = ExternalIdentityAdapter.OpenId(Provider("google"), Claims("subject", "accounts.google.com")).Expect<ExternalIdentityProof>();
		await Assert.That(alias.Key.Issuer).IsEqualTo("https://accounts.google.com");
		var principal = Claims("subject", "https://accounts.google.com");
		ExternalIdentityAdapter.OpenId(Provider("google"), principal).Expect<ExternalIdentityProof>();
		((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("sub", "duplicate"));
		ExternalIdentityAdapter.OpenId(Provider("google"), principal).Expect<Error<string>>();
		await Task.CompletedTask;
	}
	[Test]
	public async Task MicrosoftRequiresConcreteMatchingTenantIssuerAndConfiguredAudience()
	{
		const string work = "72f988bf-86f1-41af-91ab-2d7cd011db47";
		const string personal = "9188040d-6c67-4c5b-b112-36a304b66dad";
		var provider = Provider("microsoft");
		var workClaims = Claims("subject", "https://login.microsoftonline.com/" + work + "/v2.0", work);
		ExternalIdentityAdapter.OpenId(provider, workClaims).Expect<ExternalIdentityProof>();
		ExternalIdentityAdapter.OpenId(provider, Claims("subject", "https://login.microsoftonline.com/common/v2.0", work)).Expect<Error<string>>();
		ExternalIdentityAdapter.OpenId(provider with { Settings = provider.Settings with { MicrosoftAudience = "consumers" } }, workClaims).Expect<Error<string>>();
		ExternalIdentityAdapter.OpenId(provider with { Settings = provider.Settings with { AllowedTenants = [personal] } }, workClaims).Expect<Error<string>>();
		ExternalIdentityAdapter.OpenId(provider with { Settings = provider.Settings with { MicrosoftAudience = "organizations" } }, Claims("subject", "https://login.microsoftonline.com/" + personal + "/v2.0", personal)).Expect<Error<string>>();
		await Task.CompletedTask;
	}
}
