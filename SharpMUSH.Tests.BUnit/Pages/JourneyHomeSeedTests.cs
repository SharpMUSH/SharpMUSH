using System.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Server;
using SharpMUSH.Server.Resources;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Actual startup/starter services over a fresh standalone wiki store, never a live world.</summary>
public class JourneyHomeSeedTests
{
	private static IOptionsWrapper<SharpMUSHOptions> Options(string introduction = "")
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default() with { Onboarding = new() { Introduction = introduction } });
		return options;
	}

	private static async Task BootstrapAsync(IWikiService wiki)
	{
		using var startup = new StartupHandler(NullLogger<StartupHandler>.Instance,
			Substitute.For<IExpandedObjectDataService>(), Options(), wiki, Substitute.For<IMessageBus>());
		await startup.StartAsync(CancellationToken.None);
	}

	private static StarterWikiService Starter(IWikiService wiki, string introduction = "", IExpandedObjectDataService? data = null)
	{
		var options = Options(introduction);
		var writer = Substitute.For<IConfigOptionWriter>();
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(_ => ValueTask.FromResult(options.CurrentValue));
		return new StarterWikiService(wiki, data ?? Substitute.For<IExpandedObjectDataService>(), options,
			writer, NullLogger<StarterWikiService>.Instance);
	}

	private static async Task ApplyStarterAsync(IWikiService wiki, string introduction = "")
	{
		if (await Starter(wiki, introduction).ApplyAsync() is not Success)
			throw new InvalidOperationException("starter seed failed");
	}

	private static async Task<WikiPage> HomeAsync(IWikiService wiki) =>
		await wiki.GetBySlugAsync("home", WikiNamespace.Main) is WikiPage page
			? page : throw new InvalidOperationException("Home was not seeded");

	[Test]
	public async Task FreshBootstrapPlacesGuideAndReferenceBelowTheOriginalHeroIntro()
	{
		var wiki = InMemoryWikiStore.CreateService();
		await BootstrapAsync(wiki);
		var home = await HomeAsync(wiki);
		await Assert.That(home.MarkdownSource).IsEqualTo(SeededWikiPages.Home);
		await Assert.That(home.SourceLocale).IsEqualTo("en");
		await Assert.That(home.MarkdownSource).StartsWith("::: center\n![SharpMUSH logo](/assets/Logo.svg){width=20%}");
		await Assert.That(home.MarkdownSource).Contains("This is your MUSH's home page. It's stored as a wiki article and can be edited\nby any authorised user.");
		var lower = home.MarkdownSource.IndexOf("## Getting started", StringComparison.Ordinal);
		await Assert.That(lower).IsGreaterThan(home.MarkdownSource.IndexOf("by any authorised user.", StringComparison.Ordinal));
		await Assert.That(home.MarkdownSource.IndexOf("[Learn to Play](/welcome)", StringComparison.Ordinal)).IsGreaterThan(lower);
		await Assert.That(home.MarkdownSource.IndexOf("[Command Reference](/help)", StringComparison.Ordinal)).IsGreaterThan(lower);
		await Assert.That(home.RenderedHtml).Contains("href=\"/welcome\"");
		await Assert.That(home.RenderedHtml).Contains("href=\"/help\"");
	}

	[Test]
	public async Task FreshStarterHomeKeepsTheAuthoredIntroductionAheadOfTheLearningLinks()
	{
		var wiki = InMemoryWikiStore.CreateService();
		await BootstrapAsync(wiki);
		const string introduction = "Our own world and our own stories.\nAn administrator's introduction.";
		await ApplyStarterAsync(wiki, introduction);
		var home = await HomeAsync(wiki);
		await Assert.That(home.MarkdownSource).IsEqualTo(StarterWikiPages.HomeWith(introduction));
		await Assert.That(home.MarkdownSource.IndexOf("[Learn to Play](/welcome)", StringComparison.Ordinal))
			.IsGreaterThan(home.MarkdownSource.IndexOf(introduction, StringComparison.Ordinal));
		await Assert.That(home.MarkdownSource.IndexOf("[Command Reference](/help)", StringComparison.Ordinal))
			.IsGreaterThan(home.MarkdownSource.IndexOf("## Start here", StringComparison.Ordinal));
		await Assert.That(home.MarkdownSource).Contains("![SharpMUSH logo](/assets/Logo.svg){width=20%}");
	}

	[Test]
	public async Task ExistingCustomizedHomeSurvivesBootstrapAndOptionalStarterApplicationBytewise()
	{
		var wiki = InMemoryWikiStore.CreateService();
		const string custom = "::: center\n# Our hero ✦\nAn intentional administrator placeholder.\n:::\n\nOur policy:  preserve  spaces.\n\tDo not rewrite our content.\n";
		if (await wiki.CreateAsync("Home", custom, "#1", sourceLocale: "en") is not WikiPage)
			throw new InvalidOperationException("custom fixture not created");
		var before = await HomeAsync(wiki);
		await BootstrapAsync(wiki);
		await BootstrapAsync(wiki);
		await ApplyStarterAsync(wiki, "A different setup introduction");
		var after = await HomeAsync(wiki);
		await Assert.That(Encoding.UTF8.GetBytes(after.MarkdownSource).SequenceEqual(Encoding.UTF8.GetBytes(before.MarkdownSource))).IsTrue();
		await Assert.That(after.RenderedHtml).IsEqualTo(before.RenderedHtml);
		await Assert.That(after.RevisionNumber).IsEqualTo(before.RevisionNumber);
	}

	[Test]
	public async Task FrozenPriorDefaultUsesTheExactKnownSourceLoaderRepresentation()
	{
		// Raw resource: dc9822e Resources/Wiki/home.md; same StreamReader + TrimEnd loader as Home.
		var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SeededWikiPages.PreJourneyHome))).ToLowerInvariant();
		await Assert.That(digest).IsEqualTo("f9a21120a3855895afebe514bb2284663f98e4a446b4a599168d95182b217ac8");
		await Assert.That(SeededWikiPages.PreJourneyHome).IsNotEqualTo(SeededWikiPages.Home);
	}

	[Test]
	public async Task RestartLeavesTheExactPreviousDefaultUnchangedWithoutExplicitApplication()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var previous = SeededWikiPages.PreJourneyHome;
		if (await wiki.CreateAsync("Home", previous, "#1", sourceLocale: "en") is not WikiPage)
			throw new InvalidOperationException("previous seed fixture not created");
		await BootstrapAsync(wiki);
		await BootstrapAsync(wiki);
		await Assert.That((await HomeAsync(wiki)).MarkdownSource).IsEqualTo(previous);
	}

	[Test]
	public async Task ExplicitStarterApplyReplacesThePreviousDefaultBeforeRecordingAppliedAndRetrying()
	{
		var wiki = InMemoryWikiStore.CreateService();
		if (await wiki.CreateAsync("Home", SeededWikiPages.PreJourneyHome, "#1", sourceLocale: "en") is not WikiPage)
			throw new InvalidOperationException("previous seed fixture not created");
		await BootstrapAsync(wiki);
		await Assert.That((await HomeAsync(wiki)).MarkdownSource).IsEqualTo(SeededWikiPages.PreJourneyHome);

		StarterWikiState? state = null;
		var data = Substitute.For<IExpandedObjectDataService>();
		data.GetExpandedServerDataAsync<StarterWikiState>().Returns(_ => ValueTask.FromResult<StarterWikiState?>(state));
		data.SetExpandedServerDataAsync(Arg.Any<StarterWikiState>(), Arg.Any<bool>()).Returns(call =>
		{
			state = call.Arg<StarterWikiState>();
			return ValueTask.CompletedTask;
		});
		const string introduction = "Our staff-written introduction.";
		var service = Starter(wiki, introduction, data);
		await Assert.That(await service.AppliedAsync()).IsFalse();
		await Assert.That(await service.ApplyAsync() is Success).IsTrue();
		var replaced = await HomeAsync(wiki);
		await Assert.That(replaced.MarkdownSource).IsEqualTo(StarterWikiPages.HomeWith(introduction));
		await Assert.That(replaced.MarkdownSource).Contains("[Learn to Play](/welcome)");
		await Assert.That(replaced.MarkdownSource).Contains("[Command Reference](/help)");
		await Assert.That(await service.AppliedAsync()).IsTrue();

		// A successful apply must actually have replaced Home; a retry must not overwrite a later edit.
		const string edited = "Staff edited this after explicitly applying the starter pages.";
		if (await wiki.UpdateAsync(replaced.Id, edited, "#1", "staff edit") is not WikiPage afterEdit)
			throw new InvalidOperationException("edit fixture failed");
		await Assert.That(await service.ApplyAsync() is Success).IsTrue();
		var afterRetry = await HomeAsync(wiki);
		await Assert.That(afterRetry.MarkdownSource).IsEqualTo(edited);
		await Assert.That(afterRetry.RevisionNumber).IsEqualTo(afterEdit.RevisionNumber);
	}

	[Test]
	public async Task NearMatchOfThePreviousDefaultRemainsCustomizedDuringExplicitApply()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var customized = SeededWikiPages.PreJourneyHome + "\n\nA staff customization; do not prefix-match me.";
		if (await wiki.CreateAsync("Home", customized, "#1", sourceLocale: "en") is not WikiPage)
			throw new InvalidOperationException("custom fixture not created");
		await BootstrapAsync(wiki);
		await ApplyStarterAsync(wiki);
		var after = await HomeAsync(wiki);
		await Assert.That(Encoding.UTF8.GetBytes(after.MarkdownSource).SequenceEqual(Encoding.UTF8.GetBytes(customized))).IsTrue();
	}
}
