using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Table-driven cover of every step in the fallback chain. The resolver is deliberately permission-blind:
/// callers hand it an already-visibility-filtered candidate set, which is what lets these tests exercise
/// the rules with no database and no auth graph.
/// </summary>
public class WikiLocaleResolverTests
{
	private static IWikiLocaleResolver BuildResolver(string defaultLocale = "en")
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create(wikiDefaultLocale: defaultLocale));
		return new WikiLocaleResolver(monitor);
	}

	[Test]
	public async Task Step2_ExactMatchWins()
	{
		var result = BuildResolver().Resolve("fr", sourceLocale: "en", available: ["fr", "de"]);

		await Assert.That(result.Locale).IsEqualTo("fr");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task Step2_ExactMatchIsCaseInsensitive()
	{
		var result = BuildResolver().Resolve("FR", sourceLocale: "en", available: ["fr"]);

		await Assert.That(result.Locale).IsEqualTo("fr");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task Step3_RegionFindsItsNeutralParent()
	{
		var result = BuildResolver().Resolve("fr-CA", sourceLocale: "en", available: ["fr"]);

		await Assert.That(result.Locale).IsEqualTo("fr");
		await Assert.That(result.IsFallback)
			.IsFalse()
			.Because("serving fr to an fr-CA reader is the same language, not a fallback");
	}

	[Test]
	public async Task Step3_NeutralFindsARegionalTranslation()
	{
		var result = BuildResolver().Resolve("fr", sourceLocale: "en", available: ["fr-CA"]);

		await Assert.That(result.Locale).IsEqualTo("fr-CA");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task Step4_FallsToTheConfiguredDefaultWhenATranslationExistsForIt()
	{
		var result = BuildResolver("de").Resolve("fr", sourceLocale: "en", available: ["de"]);

		await Assert.That(result.Locale).IsEqualTo("de");
		await Assert.That(result.IsFallback).IsTrue();
	}

	[Test]
	public async Task Step5_FallsToTheSourceLocaleAsTheTerminalGuarantee()
	{
		var result = BuildResolver().Resolve("fr", sourceLocale: "en", available: []);

		await Assert.That(result.Locale).IsEqualTo("en");
		await Assert.That(result.IsFallback).IsTrue();
	}

	[Test]
	public async Task Step5_SourceLocaleWinsEvenWhenItIsNotTheConfiguredDefault()
	{
		// A page authored only in French on an en-default game still has something to serve.
		var result = BuildResolver("en").Resolve("de", sourceLocale: "fr", available: []);

		await Assert.That(result.Locale).IsEqualTo("fr");
		await Assert.That(result.IsFallback).IsTrue();
	}

	[Test]
	public async Task StampedSourceLocaleIsNotReinterpretedWhenTheConfiguredDefaultChanges()
	{
		// The regression test for the bug the design fixes. Same page, two different configured defaults:
		// the served locale must be the page's own stamped SourceLocale both times. If the resolver ever
		// re-derives an "effective" source locale from configuration, this fails.
		var onEnglishGame = BuildResolver("en").Resolve("de", sourceLocale: "fr", available: []);
		var onGermanGame = BuildResolver("de").Resolve("es", sourceLocale: "fr", available: []);

		await Assert.That(onEnglishGame.Locale).IsEqualTo("fr");
		await Assert.That(onGermanGame.Locale)
			.IsEqualTo("fr")
			.Because("SourceLocale is materialised per page; changing wiki_default_locale must not "
				+ "reinterpret what language an existing page was authored in");
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("not a locale")]
	public async Task UnparseableOrAbsentRequestBecomesTheConfiguredDefault(string? requested)
	{
		var result = BuildResolver("en").Resolve(requested, sourceLocale: "en", available: ["fr"]);

		await Assert.That(result.Locale).IsEqualTo("en");
		await Assert.That(result.IsFallback)
			.IsFalse()
			.Because("a reader who asked for nothing is not being shown a fallback");
	}

	[Test]
	public async Task RequestingTheSourceLocaleIsNeverAFallback()
	{
		var result = BuildResolver().Resolve("en", sourceLocale: "en", available: ["fr"]);

		await Assert.That(result.Locale).IsEqualTo("en");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task SourceLocaleIsPreferredOverATranslationThatShadowsIt()
	{
		// No row may shadow the source; if a stale one exists, the page still wins.
		var result = BuildResolver().Resolve("en", sourceLocale: "en", available: ["en", "fr"]);

		await Assert.That(result.Locale).IsEqualTo("en");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task NormalizeRequested_FallsToTheConfiguredDefault()
	{
		var resolver = BuildResolver("fr");

		await Assert.That(resolver.NormalizeRequested("junk")).IsEqualTo("fr");
		await Assert.That(resolver.NormalizeRequested("pt-br")).IsEqualTo("pt-BR");
		await Assert.That(resolver.DefaultLocale).IsEqualTo("fr");
	}

	[Test]
	public async Task DefaultLocale_FallsBackToEnglishWhenConfigurationIsGarbage()
	{
		// Unreachable in production: ValidateSharpOptions (Task 1) fails startup on an unparseable
		// wiki_default_locale. Kept as belt-and-braces so a bad value from a hand-edited stored config
		// degrades to a readable page instead of throwing inside a render.
		await Assert.That(BuildResolver("not a locale").DefaultLocale)
			.IsEqualTo(WikiOptions.DefaultLocaleFallback)
			.Because("a misconfigured default must not break every wiki read");
	}
}
