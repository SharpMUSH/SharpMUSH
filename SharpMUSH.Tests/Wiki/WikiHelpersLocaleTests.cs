using OneOf.Types;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// <see cref="WikiHelpers.NormalizeLocale"/> is the single gate between caller-supplied locale text and
/// every stored locale in the wiki. A tag that escapes it unparsed would later throw inside
/// <c>CultureInfo.GetCultureInfo</c> on a read path the spec guarantees cannot fail — and casing that
/// escapes it uncanonicalised would put <c>pt-BR</c> and <c>pt-br</c> in the store as two unrelated rows
/// the unique index is happy to accept.
/// </summary>
public class WikiHelpersLocaleTests
{
	[Test]
	[Arguments("en", "en")]
	[Arguments("EN", "en")]
	[Arguments("  fr  ", "fr")]
	[Arguments("fr-ca", "fr-CA")]
	[Arguments("FR-CA", "fr-CA")]
	[Arguments("pt-br", "pt-BR")]
	[Arguments("PT-BR", "pt-BR")]
	[Arguments("pt-BR", "pt-BR")]
	public async Task NormalizeLocale_CanonicalisesRecognisedTags(string input, string expected)
	{
		var result = WikiHelpers.NormalizeLocale(input);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0).IsEqualTo(expected);
	}

	[Test]
	public async Task NormalizeLocale_CollapsesEveryCasingOfATagOntoOneStoredValue()
	{
		// This is the hole case canonicalisation closes: three spellings, one row, one index entry.
		string[] spellings = ["pt-br", "PT-BR", "pt-BR"];

		var canonical = spellings.Select(s => WikiHelpers.NormalizeLocale(s).AsT0).Distinct().ToList();

		await Assert.That(canonical)
			.IsEquivalentTo(new[] { "pt-BR" })
			.Because("otherwise the unique (PageId, Locale) index accepts all three as different locales");
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("not a locale")]
	[Arguments("qq")]
	[Arguments("zz-ZZ")]
	public async Task NormalizeLocale_RejectsUnusableTagsWithAnError(string? input)
	{
		var result = WikiHelpers.NormalizeLocale(input);

		await Assert.That(result.IsT1)
			.IsTrue()
			.Because("a write boundary must refuse a non-locale rather than store an empty string");
		await Assert.That(result.AsT1).IsTypeOf<Error<string>>();
	}

	[Test]
	[Arguments("pt-br", "pt-BR")]
	[Arguments(null, "")]
	[Arguments("not a locale", "")]
	[Arguments("zz-ZZ", "")]
	public async Task NormalizeLocaleOrEmpty_IsThePermissiveReadPathForm(string? input, string expected)
	{
		await Assert.That(WikiHelpers.NormalizeLocaleOrEmpty(input))
			.IsEqualTo(expected)
			.Because("a reader typing a bad ?lang= gets the default page, never a 400");
	}

	[Test]
	public async Task NormalizeLocale_AgreesWithTheStartupValidationOfWikiDefaultLocale()
	{
		// ValidateSharpOptions restates this rule because SharpMUSH.Contracts references
		// SharpMUSH.Configuration and the dependency cannot run the other way. This is the test that
		// keeps the restatement honest.
		await Assert.That(WikiHelpers.NormalizeLocale(WikiOptions.DefaultLocaleFallback).IsT0).IsTrue();
		await Assert.That(new ValidateSharpOptions()
				.Validate(null, TestSharpMushOptions.Create(wikiDefaultLocale: "zz-ZZ")).Failed)
			.IsTrue()
			.Because("both must reject a well-formed tag that is not a real culture");
	}

	[Test]
	public async Task NeutralLocale_StripsTheRegion()
	{
		await Assert.That(WikiHelpers.NeutralLocale("fr-CA")).IsEqualTo("fr");
		await Assert.That(WikiHelpers.NeutralLocale("fr")).IsEqualTo("fr");
		await Assert.That(WikiHelpers.NeutralLocale("nonsense")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task SameLanguage_ComparesLanguagesNotTags()
	{
		await Assert.That(WikiHelpers.SameLanguage("fr-CA", "fr")).IsTrue();
		await Assert.That(WikiHelpers.SameLanguage("fr-CA", "en")).IsFalse();
		await Assert.That(WikiHelpers.SameLanguage("en", "en-GB")).IsTrue();
	}
}
