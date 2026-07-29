using System.Globalization;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// The portal's locale list moved out of <c>LanguagePicker.razor</c> so the wiki editor's locale dropdown
/// reads the same list. If the two ever diverge, a language appears in one place and not the other.
/// </summary>
public class PortalLocalesTests
{
	[Test]
	public async Task Codes_AreTheLocalesWithSatelliteResources()
	{
		// This used to restate Codes as a hardcoded literal, which is a second copy of the thing under
		// test: it failed on every locale addition without proving anything, and the obvious response
		// was to bump the literal — the same edit that would paper over a genuinely missing resx.
		//
		// That a declared locale actually has translations is DeclaredLocaleCoverageTests' job, and that
		// it reaches SatelliteResourceLanguages is PortalSurfacesTests'. What neither covers is the
		// shape of the list itself, so that is what this asserts.
		await Assert.That(PortalLocales.Codes.Distinct().Count())
			.IsEqualTo(PortalLocales.Codes.Count)
			.Because("a duplicate code would offer the same language twice in the picker");

		var afterEnglish = PortalLocales.Codes.Skip(1).ToList();
		await Assert.That(afterEnglish)
			.IsEquivalentTo(afterEnglish.OrderBy(c => c, StringComparer.Ordinal).ToList())
			.Because("the documented order is English first, then alphabetically — an unsorted list makes "
				+ "a language hard to find in a picker this long");
	}

	[Test]
	public async Task English_is_offered_first()
	{
		// It is the neutral resource and the fallback Program.cs resets a bad stored tag to; burying it
		// in an alphabetical list makes the escape hatch hard to find.
		await Assert.That(PortalLocales.Codes[0]).IsEqualTo("en");
	}

	[Test]
	public async Task Every_supported_code_is_a_real_culture()
	{
		foreach (var code in PortalLocales.Codes)
		{
			await Assert.That(CultureInfo.GetCultureInfo(code, predefinedOnly: true).Name).IsEqualTo(code);
		}
	}

	[Test]
	public async Task DisplayName_UsesTheNativeNameCapitalised()
	{
		await Assert.That(PortalLocales.DisplayName("fr")).IsEqualTo("Français");
		await Assert.That(PortalLocales.DisplayName("de")).IsEqualTo("Deutsch");
		await Assert.That(PortalLocales.DisplayName("en")).IsEqualTo("English");
	}

	[Test]
	public async Task DisplayName_IsNeverEmptyForAnySupportedLocale()
	{
		// The language picker and the wiki chip rows render nothing but this; an empty string would
		// read as a rendering fault rather than as a language.
		foreach (var code in PortalLocales.Codes)
		{
			await Assert.That(PortalLocales.DisplayName(code)).IsNotEmpty().Because($"{code} has no display name");
		}
	}

	[Test]
	public async Task DisplayName_FallsBackToTheTagForAnUnknownLocale()
	{
		await Assert.That(PortalLocales.DisplayName("zz-ZZ"))
			.IsEqualTo("zz-ZZ")
			.Because("a game may translate into a locale the portal chrome has no resx for");
	}

	[Test]
	[Arguments("ja")]
	[Arguments("cs")]
	[Arguments("ko")]
	public async Task DisplayName_NamesALocaleTheChromeShipsNoResxFor(string tag)
	{
		// These tags are deliberately outside PortalLocales.Codes. The cases used to be pt-BR, zh-Hans
		// and ru, which made the name a lie once those locales shipped satellites — the test still
		// passed, but it had stopped covering the case it was written for.
		//
		// Wiki content may be translated into any locale the runtime knows, and the chip rows name it
		// with nothing but this. Asserted as "not the tag" rather than against a literal native name,
		// which is ICU-version data and would make this a test of the SDK.
		//
		// This says nothing about the browser: the suite runs on the desktop runtime, which always has
		// full ICU. Whether the *WASM* build does is a csproj property, checked by PortalSurfacesTests.
		//
		// Asserted rather than assumed, so that adding one of these languages to the portal fails here
		// loudly instead of quietly hollowing the test out again.
		await Assert.That(PortalLocales.Codes).DoesNotContain(tag)
			.Because($"{tag} must stay outside the shipped locales for this case to cover anything");

		await Assert.That(PortalLocales.DisplayName(tag)).IsNotEqualTo(tag);
	}
}
