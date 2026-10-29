using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Onboarding;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

public class JourneyEntryLinksTests : BunitContext
{
	[Test]
	public async Task ConfiguredWelcomeKeepsItsExactTitleAndRouteBesideLearnAndReference()
	{
		Services.AddEchoLocalizer();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true, onboarding:
				new OnboardingPresentation("game", "authored introduction", "", "", "", "",
						new GuideLink("The Keeper's Welcome", "/wiki/main/keepers_welcome"),
						new GuideLink("House Rules", "/wiki/policy/house_rules"),
						new GuideLink("Character Guide", "/wiki/help/character_guide"))));
		var cut = Render<JourneyEntryLinks>();
		cut.WaitForAssertion(() => cut.Find("a[href='/wiki/main/keepers_welcome']"));
		await Assert.That(cut.Find("a[href='/wiki/main/keepers_welcome']").TextContent).IsEqualTo("The Keeper's Welcome");
		await Assert.That(cut.Find("a[href='/welcome']").TextContent).IsEqualTo("NavJourneyLearn");
		await Assert.That(cut.Find("a[href='/help']").TextContent).IsEqualTo("NavJourneyReference");
		await Assert.That(cut.FindAll("a[href='/wiki']")).IsEmpty();
		await Assert.That(cut.Find("a[href='/wiki/policy/house_rules']").TextContent).IsEqualTo("House Rules");
		await Assert.That(cut.Find("a[href='/wiki/help/character_guide']").TextContent).IsEqualTo("Character Guide");
	}

	[Test]
	public async Task AnEnabledWikiDoesNotBecomeAnInventedAboutOrPolicyDestination()
	{
		Services.AddEchoLocalizer();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true, features: [SharpMUSH.Library.Models.Portal.Setup.GameFeatures.WikiReader]));
		var cut = Render<JourneyEntryLinks>();
		await Assert.That(cut.FindAll("a[href='/wiki'], a[href='/about']")).IsEmpty();
		await Assert.That(cut.FindAll(".journey-about")).IsEmpty();
	}

	[Test]
	public async Task ConfiguredRulesAndCharacterGuideRemainVisibleWithoutAWelcomeLink()
	{
		Services.AddEchoLocalizer();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true, onboarding:
			new OnboardingPresentation("game", "", "", "", "", "", null,
				new GuideLink("Our Rules", "/wiki/policy/rules"), new GuideLink("Make a character", "/wiki/main/chargen"))));
		var cut = Render<JourneyEntryLinks>();
		cut.WaitForAssertion(() => cut.Find("a[href='/wiki/policy/rules']"));
		await Assert.That(cut.Find("a[href='/wiki/policy/rules']").TextContent).IsEqualTo("Our Rules");
		await Assert.That(cut.Find("a[href='/wiki/main/chargen']").TextContent).IsEqualTo("Make a character");
		await Assert.That(cut.FindAll("a[href='/wiki'], a[href='/about']")).IsEmpty();
	}

	[Test]
	public async Task EnglishIdentityCopyMatchesTheSharedJourneyContractExactly()
	{
		using var culture = CultureScope.For("en");
		var loc = PortalLocalizer.Create();
		await Assert.That(loc["AuthJourneyIdentity"].Value).IsEqualTo("Your account is your private sign-in. A character is who you play in the shared world. One account can own several characters; check which character you are playing before acting.");
	}

	[Test]
	public async Task ClaimGuidanceDistinguishesSelectionFromLinkingAndDoesNotPromiseEligibility()
	{
		using var culture = CultureScope.For("en");
		var loc = PortalLocalizer.Create();
		await Assert.That(loc["NavClaimCharacterBlurb"].Value).IsEqualTo("Already on this account? Select that character instead of claiming it again. To link an unlinked character you own, use its character password after signing in. If you don't have that password, ask staff. Account and character passwords are separate; eligibility and any transfer requirements are checked by the game.");
		await Assert.That(loc["NavClaimInstead"].Value).IsEqualTo("Need to link an existing character?");
	}

	[Test]
	public async Task AnUnavailableWikiDoesNotInventAnAboutDestination()
	{
		Services.AddEchoLocalizer();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true, features: []));
		var cut = Render<JourneyEntryLinks>();
		await Assert.That(cut.FindAll("a").Select(a => a.GetAttribute("href") ?? "").ToArray())
				.IsEquivalentTo(new[] { "/welcome", "/help" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
