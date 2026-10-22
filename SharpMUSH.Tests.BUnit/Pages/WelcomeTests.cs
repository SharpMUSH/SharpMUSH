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
		public string Genre { get; set; } = "Fantasy";
		public bool FeaturesEnabled { get; set; }
		public bool FailGuide { get; set; }
		public override Task<bool> HasFeatureAsync(string feature) => Task.FromResult(FeaturesEnabled);
		public override Task<string> GameNameAsync() => Task.FromResult("Our shared world");
		public override Task<OnboardingPresentation?> OnboardingAsync() => FailGuide ? Task.FromResult<OnboardingPresentation?>(null) : Task.FromResult<OnboardingPresentation?>(new("game-1", "Welcome to the harbour.", Genre, "", "minimalist", "", null, null, null));
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
	public async Task AdminChecklistShowsMascotForEachTopicAndRespectsImagery()
	{
		ComponentFactories.AddStub<GameIdentityForm>();
		var page = Render<SharpMUSH.Client.Pages.Admin.Onboarding>();
		var guide = Services.GetRequiredService<OnboardingService>();
		page.WaitForAssertion(() =>
		{
			if (page.FindAll(".admin-checklist img").Count != GuideStepIds.Administrator.Count)
				throw new InvalidOperationException("Each administrator topic must show its mascot.");
		});
		foreach (var step in GuideStepIds.Administrator)
		{
			// A topic without its own illustration shows the genre's general one.
			var illustrated = SharpieArtCatalog.Approved.Any(art => art.Topic == $"administrator-{step}");
			var image = page.Find($"[data-art-topic='administrator-{step}'] img");
			await Assert.That(image.GetAttribute("src")).Contains(illustrated
				? $"sharpie-fantasy-minimalist-administrator-{step}.png" : "sharpie-fantasy-minimalist.png");
			await Assert.That(image.GetAttribute("alt")).IsEqualTo("");
		}
		await page.InvokeAsync(() => guide.SetImageryAsync(GuideImagery.Reduced));
		await Assert.That(page.FindAll(".admin-topic-mascot-reduced").Count).IsEqualTo(GuideStepIds.Administrator.Count);
		await page.InvokeAsync(() => guide.SetImageryAsync(GuideImagery.None));
		await Assert.That(page.FindAll(".admin-checklist img").Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(1, true)]
	[Arguments(42, false)]
	public async Task AdminChecklistWarnsOnlyWhileGodIsTheActiveCharacter(int dbref, bool warned)
	{
		ComponentFactories.AddStub<GameIdentityForm>();
		Services.GetRequiredService<IAccountAuthState>().ActiveCharacter
			.Returns(new AccountAuthService.CharacterSummary(dbref, 1L, "Staff", ""));
		var page = Render<SharpMUSH.Client.Pages.Admin.Onboarding>();
		page.WaitForAssertion(() => page.Find("[data-art-topic='administrator-staff-character']"));
		await Assert.That(page.FindAll("[data-art-topic='administrator-staff-character'] .admin-check-warning").Count).IsEqualTo(warned ? 1 : 0);
	}

	[Test]
	public async Task InactiveCompanionRespectsGenreAndReducedImagery()
	{
		var guide = Services.GetRequiredService<OnboardingService>();
		await guide.LoadAsync(GuideJourney.Player);
		var panel = Render<GuideCompanion>(p => p.Add(x => x.Guide, guide));
		await Assert.That(panel.Find("img").GetAttribute("src")).Contains("sharpie-fantasy-minimalist-shared-welcome.png");
		await panel.InvokeAsync(() => guide.SetImageryAsync(GuideImagery.Reduced));
		await Assert.That(panel.Find("img").GetAttribute("width")).IsEqualTo("40");
		await panel.InvokeAsync(() => guide.SetImageryAsync(GuideImagery.None));
		await Assert.That(panel.FindAll("img").Count).IsEqualTo(0);
	}

	[Test]
	public async Task GuideTooltipSupportsFocusHoverAndEscapeWithoutActivatingControl()
	{
		var clicked = 0;
		var tip = Render<GuideTooltip>(p => p
			.Add(x => x.Text, "Stop this guide")
			.AddChildContent(builder =>
			{
				builder.OpenElement(0, "button");
				builder.AddAttribute(1, "aria-label", "Stop this guide");
				builder.AddAttribute(2, "onclick", Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => clicked++));
				builder.CloseElement();
			}));
		await Assert.That(tip.FindAll(".guide-tooltip-label").Count).IsEqualTo(0);
		await tip.Find(".guide-tooltip").FocusInAsync();
		await Assert.That(tip.Find(".guide-tooltip-label").TextContent).IsEqualTo("Stop this guide");
		await tip.Find(".guide-tooltip").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
		await Assert.That(tip.FindAll(".guide-tooltip-label").Count).IsEqualTo(0);
		await Assert.That(clicked).IsEqualTo(0);
		await tip.Find(".guide-tooltip").FocusOutAsync();
		await tip.Find(".guide-tooltip").MouseEnterAsync();
		await Assert.That(tip.FindAll(".guide-tooltip-label").Count).IsEqualTo(1);
		await tip.Find(".guide-tooltip").MouseLeaveAsync();
		await Assert.That(tip.FindAll(".guide-tooltip-label").Count).IsEqualTo(0);
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
	[Arguments(false)]
	[Arguments(true)]
	public async Task SelectingWelcomePathFocusesLessonOnce(bool preview)
	{
		Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(preview ? "/welcome?preview=true" : "/welcome");
		var page = Render<Welcome>();
		var before = JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");
		await page.Find("[data-guide-path=mush]").ClickAsync();
		await Assert.That(JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus")).IsEqualTo(before + 1);
		await page.Find(".welcome-imagery select").ChangeAsync("Reduced");
		await Assert.That(JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus")).IsEqualTo(before + 1);
	}

	[Test]
	public async Task PreviewExploreRemainsSelectedWithoutPersistingProgress()
	{
		Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/welcome?preview=true");
		var page = Render<Welcome>();
		var guide = Services.GetRequiredService<OnboardingService>();
		var original = guide.Progress;
		await Assert.That(page.FindAll("[data-guide-path][aria-pressed=true]").Count).IsEqualTo(0);
		await page.Find("[data-guide-path=mush]").ClickAsync();
		await Assert.That(page.Find("[data-guide-path=mush]").GetAttribute("aria-pressed")).IsEqualTo("true");
		await page.Find("[data-guide-path=explore]").ClickAsync();
		await Assert.That(page.Find("[data-guide-path=explore]").GetAttribute("aria-pressed")).IsEqualTo("true");
		await Assert.That(page.FindAll("[data-guide-path][aria-pressed=true]").Count).IsEqualTo(1);
		await Assert.That(page.FindAll(".guide-lesson").Count).IsEqualTo(0);
		await page.Find("[data-guide-path=mush]").ClickAsync();
		await page.Find(".guide-stop").ClickAsync();
		await Assert.That(page.FindAll("[data-guide-path][aria-pressed=true]").Count).IsEqualTo(0);
		await Assert.That(guide.Progress).IsEqualTo(original);
		await Assert.That(JSInterop.Invocations.Any(call => call.Identifier.Contains("setItem", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PreviewFinishesWithoutSavingPlayerProgress(bool skip)
	{
		Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/welcome?preview=true");
		var page = Render<Welcome>();
		var guide = Services.GetRequiredService<OnboardingService>();
		var original = guide.Progress;
		await page.Find("[data-guide-path=mush]").ClickAsync();
		for (var i = 0; i < GuideCatalog.Player(GuidePath.Mush).Count; i++)
			await page.Find(skip ? ".guide-skip" : ".guide-panel-actions .guide-action").ClickAsync();
		await Assert.That(page.FindAll(".welcome-completion").Count).IsEqualTo(1);
		await Assert.That(page.FindAll(".guide-lesson").Count).IsEqualTo(0);
		await Assert.That(guide.Progress).IsEqualTo(original);
		await Assert.That(JSInterop.Invocations.Any(call => call.Identifier.Contains("setItem", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	[Arguments("Fantasy", "fantasy")]
	[Arguments("Science Fiction", "science-fiction")]
	public async Task ApprovedMsspArtworkAppearsWithoutPreview(string genre, string filename)
	{
		((Info)Services.GetRequiredService<ServerInfoService>()).Genre = genre;
		var page = Render<Welcome>();
		await Assert.That(page.Find("img").GetAttribute("src")).Contains($"sharpie-{filename}-minimalist-shared-welcome.png");
		await page.Find("[data-guide-path=mush]").ClickAsync();
		await Assert.That(page.FindAll("img").Count).IsEqualTo(1);
		await Assert.That(page.Find("img").GetAttribute("src")).Contains($"sharpie-{filename}-minimalist-player-place.png");
		await page.Find(".welcome-imagery select").ChangeAsync("None");
		await Assert.That(page.FindAll("img").Count).IsEqualTo(0);
	}

	[Test]
	public async Task MascotChangesWithJourneyAndNeverDuplicatesActiveLesson()
	{
		((Info)Services.GetRequiredService<ServerInfoService>()).Genre = "Unknown";
		var page = Render<Welcome>();
		await Assert.That(page.Find("img.welcome-art").GetAttribute("src")).Contains("sharpie-primary.svg");
		await page.Find("[data-guide-path=mush]").ClickAsync();
		await Assert.That(page.FindAll("img").Count).IsEqualTo(1);
		await Assert.That(page.Find("img.guide-mascot").GetAttribute("src")).Contains("sharpie-minimal.svg");
		for (var i = 0; i < 5; i++)
			await page.Find(".guide-panel-actions .guide-action").ClickAsync();
		await Assert.That(page.Find(".welcome-completed img").GetAttribute("src")).Contains("sharpie-smile.svg");
		await page.Find(".welcome-imagery select").ChangeAsync("None");
		await Assert.That(page.FindAll("img").Count).IsEqualTo(0);
		await page.Find(".welcome-imagery select").ChangeAsync("Reduced");
		await Assert.That(page.FindAll("img").Count).IsEqualTo(1);
		await Assert.That(page.Find(".welcome").GetAttribute("data-guide-imagery")).IsEqualTo("reduced");
	}

	[Test]
	public async Task VeteranFinishesAfterItsFiveLessons()
	{
		var page = Render<Welcome>();
		await page.Find("[data-guide-path=mush]").ClickAsync();
		for (var i = 0; i < 5; i++)
			await page.Find(".guide-panel-actions .guide-action").ClickAsync();
		await Assert.That(page.FindAll(".welcome-explore").Count).IsEqualTo(1);
		await Assert.That(page.FindAll(".guide-lesson").Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FinishingWelcomeFocusesCompletion(bool skip)
	{
		var page = Render<Welcome>();
		await page.Find("[data-guide-path=mush]").ClickAsync();
		for (var i = 0; i < 4; i++)
			await page.Find(".guide-panel-actions .guide-action").ClickAsync();
		var before = JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");
		await page.Find(skip ? ".guide-panel-actions .guide-skip" : ".guide-panel-actions .guide-action").ClickAsync();
		await Assert.That(page.Find(".welcome-completion").GetAttribute("tabindex")).IsEqualTo("-1");
		await Assert.That(JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus")).IsEqualTo(before + 1);
		await page.Find(".welcome-imagery select").ChangeAsync("Reduced");
		await Assert.That(JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus")).IsEqualTo(before + 1);
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
	public async Task InactiveCompanionOffersPaths_AndStartsALesson()
	{
		var guide = Services.GetRequiredService<OnboardingService>();
		await guide.LoadAsync(GuideJourney.Player);
		var companion = Render<GuideCompanion>(parameters => parameters.Add(component => component.Guide, guide));
		await Assert.That(companion.FindAll(".guide-start button").Count).IsEqualTo(4);
		await companion.Find("[data-guide-path=Discord]").ClickAsync();
		await Assert.That(companion.FindAll(".guide-panel").Count).IsEqualTo(1);
		await Assert.That(guide.Progress.Active).IsTrue();
		await Assert.That(companion.Find(".guide-lesson h2").GetAttribute("tabindex")).IsEqualTo("-1");
		await Assert.That(JSInterop.Invocations.Any(call => call.Identifier == "Blazor._internal.domWrapper.focus")).IsTrue();
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

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SuccessfulInsertionReturnsToPlay_RefusalKeepsTheGuide(bool inserted)
	{
		var guide = Services.GetRequiredService<OnboardingService>();
		await guide.LoadAsync(GuideJourney.Player);
		await guide.ChoosePathAsync(GuidePath.Discord);
		await guide.SetCurrentStepAsync("useful-action");
		var returns = 0;
		var companion = Render<GuideCompanion>(parameters => parameters
			.Add(component => component.Guide, guide)
			.Add(component => component.InsertLook, () => Task.FromResult(inserted))
			.Add(component => component.OnInserted, () => returns++));
		await companion.Find("article button.guide-action").ClickAsync();
		await Assert.That(returns).IsEqualTo(inserted ? 1 : 0);
		await Assert.That(guide.Progress.Active).IsTrue();
		await Assert.That(companion.FindAll("details.guide-companion, .guide-companion > summary").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ExploreFromPlayReturnsToComposerWithoutDismissingProgress()
	{
		var guide = Services.GetRequiredService<OnboardingService>();
		await guide.LoadAsync(GuideJourney.Player);
		await guide.ChoosePathAsync(GuidePath.Mush);
		await guide.SetCurrentStepAsync("continue");
		var returns = 0;
		var companion = Render<GuideCompanion>(parameters => parameters
			.Add(component => component.Guide, guide)
			.Add(component => component.OnExplore, () => returns++));
		await companion.Find("article button[aria-label='Explore the game']").ClickAsync();
		await Assert.That(returns).IsEqualTo(1);
		await Assert.That(guide.Progress.Active).IsTrue();
		await Assert.That(guide.Progress.CurrentStepId).IsEqualTo("continue");
	}
}
