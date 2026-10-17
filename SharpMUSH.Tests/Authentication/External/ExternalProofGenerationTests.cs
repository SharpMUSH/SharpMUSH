using Mediator;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.External;
namespace SharpMUSH.Tests.Authentication.External;

public class ExternalProofGenerationTests
{
	[Test]
	public async Task DisabledThenReenabledProviderCannotResurrectAnEarlierProof()
	{
		var settings = new ExternalProviderSettings { Enabled = true, ClientId = "client", PublicOrigin = "https://game.example", RegistrationId = "external-google", Generation = "initial" };
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetExternalProviderSettingsQuery>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(call.Arg<GetExternalProviderSettingsQuery>().Provider == "google" ? settings : new()));
		var identity = new ExternalIdentity("identity", "account", new("external-google", "https://accounts.google.com", "subject"), null, DateTimeOffset.UtcNow, null);
		mediator.Send(new GetExternalIdentitiesQuery("account"), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ExternalIdentity>>([identity]));
		var secrets = Substitute.For<ISecretStore>(); secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<FoundResult<string>>("fixture-secret"));
		var providers = new ExternalProviderCatalog(mediator, secrets, Substitute.For<IHostEnvironment>(), new AccountCredentialGate());
		var validator = new AccountProofAuthorityValidator(mediator, providers);
		var initial = (await providers.EffectiveAsync("google")).Expect<EffectiveExternalProvider>();
		var authority = new AccountProofAuthority("external", "identity", 3, initial.Generation);
		await Assert.That(await validator.IsCurrentAsync("account", authority)).IsTrue();
		settings = settings with { Enabled = false, Generation = "disabled" };
		await Assert.That(await validator.IsCurrentAsync("account", authority)).IsFalse();
		settings = settings with { Enabled = true, Generation = "reenabled" };
		await Assert.That(await validator.IsCurrentAsync("account", authority)).IsFalse();
	}
}
