using System.Text;
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

	private static async Task ApplyStarterAsync(IWikiService wiki, string introduction = "")
	{
		var options = Options(introduction);
		var writer = Substitute.For<IConfigOptionWriter>();
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(_ => ValueTask.FromResult(options.CurrentValue));
		var service = new StarterWikiService(wiki, Substitute.For<IExpandedObjectDataService>(), options,
			writer, NullLogger<StarterWikiService>.Instance);
		if (await service.ApplyAsync() is not Success) throw new InvalidOperationException("starter seed failed");
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
	public async Task PreviousStoredBootSeedIsNotPretendedToAutoUpgrade()
	{
		// Frozen prior default from A dc9822e, not rewritten to match the new seed during the test.
		const string previous = """
			::: center
			![SharpMUSH logo](/assets/Logo.svg){width=20%}

			This is your MUSH's home page. It's stored as a wiki article and can be edited
			by any authorised user.
			:::

			## Getting started
			- [Get started with Sharpie](/welcome) for an optional guide matched to your experience.
			- [Sign in or register](/login), then create or select a character to open [Play](/play).
			- Prefer a MU* client? Make an account with `register <name> [email] <password>`,
			  a character with `create <character> <password>`, and next time log in with
			  `connect <character> <password>`.
			- Read this game's rules and character requirements before joining a scene.

			## About SharpMUSH
			SharpMUSH is a modern, open-source MUSH server written in .NET, targeting
			PennMUSH compatibility. See the [[Help:Markdown Guide]] for formatting help.
			""";
		var wiki = InMemoryWikiStore.CreateService();
		if (await wiki.CreateAsync("Home", previous, "#1", sourceLocale: "en") is not WikiPage)
			throw new InvalidOperationException("previous seed fixture not created");
		await BootstrapAsync(wiki);
		await ApplyStarterAsync(wiki);
		var after = await HomeAsync(wiki);
		await Assert.That(after.MarkdownSource).IsEqualTo(previous);
		await Assert.That(after.MarkdownSource).DoesNotContain("[Command Reference](/help)");
	}
}
