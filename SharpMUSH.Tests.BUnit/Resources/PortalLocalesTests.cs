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
	public async Task Supported_ContainsTheTwoLocalesWithSatelliteResources()
	{
		await Assert.That(PortalLocales.Codes).IsEquivalentTo(new[] { "en", "fr" });
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
		await Assert.That(PortalLocales.DisplayName("en")).IsEqualTo("English");
	}

	[Test]
	public async Task DisplayName_FallsBackToTheTagForAnUnknownLocale()
	{
		await Assert.That(PortalLocales.DisplayName("zz-ZZ"))
			.IsEqualTo("zz-ZZ")
			.Because("a game may translate into a locale the portal chrome has no resx for");
	}

	[Test]
	public async Task Flag_IsPresentForEverySupportedLocale()
	{
		foreach (var (code, flag) in PortalLocales.Supported)
		{
			await Assert.That(flag).IsNotEmpty().Because($"{code} has no flag emoji");
		}
	}

	[Test]
	public async Task Flag_FallsBackToAGlobeForAnUnsupportedLocale()
	{
		// The editor offers translations into locales the chrome has no flag for, and the history and admin
		// chip rows render whatever locales exist. An empty chip would look like a rendering fault.
		await Assert.That(PortalLocales.Flag("pt-BR")).IsEqualTo("\U0001F310");
	}

	[Test]
	public async Task Flag_IsCaseInsensitive()
	{
		// Locales arrive from the server already canonicalised, but a hand-typed ?lang=FR reaches the read
		// path unchanged, and matching that to the globe would look like the language is unsupported.
		await Assert.That(PortalLocales.Flag("FR")).IsEqualTo(PortalLocales.Flag("fr"));
	}
}
