using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Components.Onboarding;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.BUnit.Pages;

public class WelcomeTests : BunitContext
{
	private sealed class Info(IHttpClientFactory factory) : ServerInfoService(factory)
	{
		public bool FeaturesEnabled { get; set; }
		public bool FailGuide { get; set; }
		public override Task<bool> HasFeatureAsync(string feature) => Task.FromResult(FeaturesEnabled);
		public override Task<string> GameNameAsync() => Task.FromResult("Our shared world");
		public override Task<OnboardingPresentation?> OnboardingAsync() => FailGuide ? Task.FromResult<OnboardingPresentation?>(null) : Task.FromResult<OnboardingPresentation?>(new("game-1", "Welcome to the harbour.", "Fantasy", "", "minimalist", "", null, null, null));
	}
	public WelcomeTests()
	{
		Services.AddLocalization();
		var factory = Substitute.For<IHttpClientFactory>();
		Services.AddSingleton(factory);
		Services.AddSingleton<IAccountAuthState>(Substitute.For<IAccountAuthState>());
		Services.AddSingleton<ServerInfoService>(new Info(factory));
		Services.AddScoped<OnboardingService>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task Companion_keeps_retry_available_when_progress_reload_fails()
	{
		var guide = Services.GetRequiredService<OnboardingService>();
		await guide.LoadAsync(GuideJourney.Player);
		await guide.ChoosePathAsync(GuidePath.Discord);
		var companion = Render<GuideCompanion>(p => p.Add(x => x.Guide, guide));
		((Info)Services.GetRequiredService<ServerInfoService>()).FailGuide = true;
		await companion.InvokeAsync(() => guide.LoadAsync(GuideJourney.Player));
		await Assert.That(companion.FindAll(".guide-recovery button").Count).IsEqualTo(1);
		((Info)Services.GetRequiredService<ServerInfoService>()).FailGuide = false;
		await companion.Find(".guide-recovery button").ClickAsync();
		await Assert.That(guide.Ready).IsTrue();
		await Assert.That(companion.FindAll(".guide-recovery").Count).IsEqualTo(0);
	}

	[Test]
	public async Task EveryLessonHasMatchingDefaultResourcesAndPreservesNavigation()
	{
		var text = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<SharpMUSH.Client.Resources.SharedResource>>();
		foreach (var path in Enum.GetValues<GuidePath>())
			await Assert.That(GuideLocalization.Player(path, text)).IsEquivalentTo(GuideCatalog.Player(path));
		await Assert.That(GuideLocalization.Administrator(text)).IsEquivalentTo(GuideCatalog.Administrator);
	}

	[Test]
	public async Task LessonsUseTheSelectedPortalLocalizer()
	{
		var localizer = Substitute.For<Microsoft.Extensions.Localization.IStringLocalizer<SharpMUSH.Client.Resources.SharedResource>>();
		localizer[Arg.Any<string>()].Returns(call => new Microsoft.Extensions.Localization.LocalizedString(call.Arg<string>(), "translated:" + call.Arg<string>()));
		Services.AddSingleton(localizer);
		var page = Render<Welcome>();
		await page.Find("[data-guide-path=mush]").ClickAsync();
		await Assert.That(page.Find(".guide-lesson h2").TextContent).IsEqualTo("translated:GuideLesson_Mush_place_Title");
	}

	[Test]
	public async Task VeteranFinishesAfterItsFourLessons()
	{
		var page = Render<Welcome>();
		await page.Find("[data-guide-path=mush]").ClickAsync();
		for (var i = 0; i < 4; i++)
			await page.Find(".guide-panel-actions .guide-action").ClickAsync();
		await Assert.That(page.FindAll(".welcome-explore").Count).IsEqualTo(1);
		await Assert.That(page.FindAll(".guide-lesson").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ReadingGuideWorksAnonymouslyAndOffersFourPaths()
	{
		var page = Render<Welcome>();
		page.WaitForElement("[data-guide-path=discord]");
		await Assert.That(page.FindAll("[data-guide-path]").Count).IsEqualTo(4);
		await Assert.That(page.Markup.Contains("Our shared world", StringComparison.Ordinal)).IsTrue();
		await page.Find("[data-guide-path=discord]").ClickAsync();
		await Assert.That(page.Find(".guide-lesson").TextContent.Contains("Discord channels", StringComparison.Ordinal)).IsTrue();
	}

	[Test]
	public async Task ExternalReturnUrlIsRejected()
	{
		Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/welcome?returnUrl=%2F%2Fevil.example%2F");
		var page = Render<Welcome>();
		page.WaitForElement("[data-guide-path=explore]");
		await page.Find("[data-guide-path=explore]").ClickAsync();
		var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
		await Assert.That(navigation.Uri).IsEqualTo(navigation.BaseUri + "play");
	}
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LeavingCompanionReturnsFocusThroughItsHost(bool complete)
	{
		var guide = Services.GetRequiredService<OnboardingService>();
		await guide.LoadAsync(GuideJourney.Player);
		await guide.ChoosePathAsync(GuidePath.Mush);
		foreach (var lesson in GuideCatalog.Player(GuidePath.Mush).Where(item => item.Id != "continue"))
			await guide.CompleteStepAsync(lesson.Id);
		var returns = 0;
		var companion = Render<GuideCompanion>(parameters => parameters
			.Add(component => component.Guide, guide)
			.Add(component => component.OnExit, () => returns++));
		await companion.Find(complete ? ".guide-panel-actions .guide-action" : ".guide-panel-heading button").ClickAsync();
		await Assert.That(returns).IsEqualTo(1);
		await Assert.That(companion.FindAll(".guide-companion").Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task GameAdditionsOnlyLinkToEnabledApplications(bool enabled)
	{
		((Info)Services.GetRequiredService<ServerInfoService>()).FeaturesEnabled = enabled;
		var page = Render<Welcome>();
		page.WaitForElement("[data-guide-path=mush]");
		await page.Find("[data-guide-path=mush]").ClickAsync();
		await page.FindAll(".welcome-steps button").Last().ClickAsync();
		await Assert.That(page.FindAll(".guide-context a[href='/scenes']").Count).IsEqualTo(enabled ? 1 : 0);
		await Assert.That(page.FindAll(".guide-context a[href='/wiki']").Count).IsEqualTo(enabled ? 1 : 0);
	}

}
