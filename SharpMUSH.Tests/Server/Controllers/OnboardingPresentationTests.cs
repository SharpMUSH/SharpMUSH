using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Authentication;
using SharpMUSH.Tests.Wiki;

namespace SharpMUSH.Tests.Server.Controllers;

public class OnboardingPresentationTests
{
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task PrivateWikiTitleIsAbsent(bool wikiEnabled)
	{
		var wiki = InMemoryWikiStore.CreateService();
		var secret = (await wiki.CreateAsync("Secret character plans", "private", "#1")).Expect<WikiPage>();
		await wiki.SetMetadataAsync(secret.Id, [], published: false);
		var publicPage = (await wiki.CreateAsync("Start here", "welcome", "#1")).Expect<WikiPage>();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default() with
		{
			Onboarding = new() { WelcomePage = "main/" + publicPage.Slug, CharacterGuidePage = "main/" + secret.Slug }
		});
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetOnboardingGameIdQuery>(), Arg.Any<CancellationToken>()).Returns("game-1");
		var access = new WikiAccessService(wiki, InMemoryRoleRegistry.Seeded(), new PermissionResolver());
		var features = Substitute.For<IGameFeatureReader>();
		features.EnabledAsync().Returns(wikiEnabled ? new[] { GameFeatures.WikiReader } : Array.Empty<string>());
		var presentation = await new OnboardingPresentationService(options, mediator, wiki, access, features,
			NullLogger<OnboardingPresentationService>.Instance).PublicAsync(default);
		var json = JsonSerializer.Serialize(presentation);
		await Assert.That(json.Contains(secret.Slug, StringComparison.Ordinal)).IsFalse();
		await Assert.That(json.Contains(secret.Title, StringComparison.Ordinal)).IsFalse();
		await Assert.That(presentation!.Welcome?.Title).IsEqualTo(wikiEnabled ? publicPage.Title : null);
	}

	[Test]
	public async Task FailedPresentationDoesNotAdvertiseLessons()
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default());
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetOnboardingGameIdQuery>(), Arg.Any<CancellationToken>())
			.Returns<string>(_ => throw new InvalidOperationException("store unavailable"));
		var presentation = await new OnboardingPresentationService(options, mediator, Substitute.For<IWikiService>(),
			Substitute.For<IWikiAccessService>(), Substitute.For<IGameFeatureReader>(), NullLogger<OnboardingPresentationService>.Instance).PublicAsync(default);
		await Assert.That(presentation).IsNull();
	}

	[Test]
	public async Task LegacyOptionsPreserveIdentity()
	{
		var original = SharpMUSHOptions.Default();
		var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(original))!;
		json.AsObject().Remove("Onboarding");
		var read = JsonSerializer.Deserialize<SharpMUSHOptions>(json.ToJsonString())!;
		await Assert.That(read.Net.MudName).IsEqualTo(original.Net.MudName);
		await Assert.That(read.Onboarding.ArtStyle).IsEqualTo("minimalist");
		await Assert.That(read.Onboarding.Introduction).IsEmpty();
	}
}
