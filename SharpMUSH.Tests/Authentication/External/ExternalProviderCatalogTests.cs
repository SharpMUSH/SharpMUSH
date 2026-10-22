using Mediator;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Authentication.External;
namespace SharpMUSH.Tests.Authentication.External;

public class ExternalProviderCatalogTests
{
	private static (ExternalProviderCatalog Catalog, IMediator Mediator, ISecretStore Secrets) Create(Dictionary<string, ExternalProviderSettings> settings)
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetExternalProviderSettingsQuery>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(settings.GetValueOrDefault(call.Arg<GetExternalProviderSettingsQuery>().Provider) ?? new()));
		mediator.Send(Arg.Any<SaveExternalProviderSettingsCommand>(), Arg.Any<CancellationToken>()).Returns(call => { var command = call.Arg<SaveExternalProviderSettingsCommand>(); settings[command.Provider] = command.Settings; return ValueTask.FromResult(Unit.Value); });
		mediator.Send(Arg.Any<GetExternalProviderAccountCountQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(0));
		var secrets = Substitute.For<ISecretStore>();
		secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<FoundResult<string>>(new NotFound()));
		return (new ExternalProviderCatalog(mediator, secrets, Substitute.For<IHostEnvironment>(), new AccountCredentialGate()), mediator, secrets);
	}
	private static ExternalProviderSettings Enabled => new() { Enabled = true, ClientId = "client", PublicOrigin = "https://game.example", RegistrationId = "external-google", Generation = "initial" };
	[Test]
	public async Task DefaultsAreDisabledAndMissingSecretRefusesEffectiveConfiguration()
	{
		var (catalog, _, _) = Create([]);
		var statuses = await catalog.StatusesAsync();
		await Assert.That(statuses.Count).IsEqualTo(ExternalProviderDefinition.All.Count);
		await Assert.That(statuses.Any(status => status.Ready)).IsFalse();
		(await catalog.EffectiveAsync("google")).Expect<Error<string>>();
	}
	/// <summary>A refused save says which setting is wrong, and stores nothing.</summary>
	[Test]
	public async Task RefusedSettingsNameWhatIsWrong()
	{
		Dictionary<string, ExternalProviderSettings> settings = [];
		var (catalog, _, _) = Create(settings);
		var refused = (await catalog.SaveAsync("google", Enabled with { PublicOrigin = "game.example", RegistrationId = "" }, true)).Expect<Error<string>>();
		await Assert.That(refused.Value).StartsWith("Public origin must be");
		await Assert.That(settings.ContainsKey("google")).IsFalse();
	}
	[Test]
	public async Task EveryRegistrationMapsBackToItsProvider()
	{
		var (catalog, _, _) = Create(new() { ["google"] = Enabled with { RegistrationId = "google-2024" } });
		var providers = await catalog.ProvidersByRegistrationAsync();
		await Assert.That(providers["google-2024"]).IsEqualTo("google");
		await Assert.That(providers["external-github"]).IsEqualTo("github");
		await Assert.That(providers.Count).IsEqualTo(ExternalProviderDefinition.All.Count);
	}
	[Test]
	public async Task DisableIsImmediateAndEverySaveChangesGeneration()
	{
		Dictionary<string, ExternalProviderSettings> settings = new() { ["google"] = Enabled };
		var (catalog, _, secrets) = Create(settings);
		secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<FoundResult<string>>("secret"));
		var initial = (await catalog.EffectiveAsync("google")).Expect<EffectiveExternalProvider>();
		var saved = (await catalog.SaveAsync("google", Enabled with { Enabled = false }, true)).Expect<ExternalProviderStatus>();
		await Assert.That(saved.Settings.Generation).IsNotEqualTo(initial.Settings.Generation);
		(await catalog.EffectiveAsync("google")).Expect<Error<string>>();
	}
	[Test]
	public async Task RotationAndChangedRegistrationRequireRestartAndNeverExposeSecret()
	{
		var (catalog, _, secrets) = Create(new() { ["google"] = Enabled });
		secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<FoundResult<string>>("original-secret"));
		var initial = (await catalog.EffectiveAsync("google")).Expect<EffectiveExternalProvider>();
		secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<FoundResult<string>>("rotated-secret"));
		(await catalog.EffectiveAsync("google")).Expect<Error<string>>();
		var status = (await catalog.StatusesAsync()).Single(s => s.Id == "google");
		await Assert.That(status.RestartRequired).IsTrue();
		await Assert.That(System.Text.Json.JsonSerializer.Serialize(status)).DoesNotContain("rotated-secret");
		await Assert.That(initial.Generation).IsNotEqualTo(status.Settings.Generation);
	}
	[Test]
	public async Task AffectedAccountsRequireAcknowledgementAndRegistrationCannotBeRebound()
	{
		var (catalog, mediator, _) = Create(new() { ["google"] = Enabled });
		mediator.Send(Arg.Any<GetExternalProviderAccountCountQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(2));
		(await catalog.SaveAsync("google", Enabled with { Enabled = false }, false)).Expect<Error<string>>();
		(await catalog.SaveAsync("google", Enabled with { RegistrationId = "other" }, true)).Expect<Error<string>>();
	}
	[Test]
	public async Task GrapevineNeedsNoSecretButClientChangesRequireRestart()
	{
		var (catalog, _, _) = Create(new() { ["grapevine"] = Enabled with { RegistrationId = "external-grapevine" } });
		(await catalog.EffectiveAsync("grapevine")).Expect<EffectiveExternalProvider>();
		var saved = (await catalog.SaveAsync("grapevine", Enabled with { RegistrationId = "external-grapevine", ClientId = "replacement" }, true)).Expect<ExternalProviderStatus>();
		await Assert.That(saved.RestartRequired).IsTrue();
		await Assert.That(saved.Ready).IsFalse();
	}
	[Test]
	public async Task ExistingIdentityNamespaceCannotBeReusedForAnotherClientEvenWithAcknowledgement()
	{
		var (catalog, mediator, _) = Create(new() { ["google"] = Enabled });
		mediator.Send(Arg.Any<GetExternalProviderAccountCountQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(2));
		(await catalog.SaveAsync("google", Enabled with { ClientId = "replacement" }, true)).Expect<Error<string>>();
	}
	[Test]
	public async Task TrustChangesStillRequireAcknowledgementWhenAlreadyDisabled()
	{
		var (catalog, mediator, _) = Create(new() { ["google"] = Enabled with { Enabled = false } });
		mediator.Send(Arg.Any<GetExternalProviderAccountCountQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(2));
		(await catalog.SaveAsync("google", Enabled with { Enabled = false, PublicOrigin = "https://other.example" }, false)).Expect<Error<string>>();
	}
}
