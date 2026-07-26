using System.Globalization;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Every other test in this project stubs <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>
/// with a double that echoes the key back, so nothing else here notices when the real resource lookup
/// misses and the portal renders raw keys ("SceneArchive") instead of their values ("Scenes"). These
/// tests exercise the production registration against the embedded resx.
/// </summary>
public class SharedResourceLocalizationTests
{
	[Test]
	public async Task The_shared_resource_set_is_found()
	{
		var strings = PortalLocalizer.Create().GetAllStrings(includeParentCultures: true).ToList();

		await Assert.That(strings).IsNotEmpty();
	}

	[Test]
	public async Task Multi_word_keys_resolve_to_their_resource_value()
	{
		var loc = PortalLocalizer.Create();

		await Assert.That(loc["SceneArchive"].ResourceNotFound).IsFalse();
		await Assert.That(loc["SceneArchive"].Value).IsEqualTo("Scenes");
	}

	[Test]
	public async Task Translated_cultures_resolve_to_their_satellite_value()
	{
		var loc = PortalLocalizer.Create();
		var previous = CultureInfo.CurrentUICulture;
		CultureInfo.CurrentUICulture = new CultureInfo("fr");
		try
		{
			await Assert.That(loc["AdminPanel"].Value).IsEqualTo("Panneau d'administration");
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
	}

	[Test]
	public async Task No_resource_value_is_left_as_its_own_camel_case_key()
	{
		var unlocalized = PortalLocalizer.Create()
			.GetAllStrings(includeParentCultures: true)
			.Where(s => s.Value == s.Name && Regex.IsMatch(s.Name, "[a-z][A-Z]"))
			.Select(s => s.Name)
			.ToList();

		await Assert.That(unlocalized).IsEmpty();
	}

	[Test]
	public async Task Every_widget_zone_has_a_label_in_the_resx()
	{
		var loc = PortalLocalizer.Create();

		var missing = Enum.GetValues<WidgetZone>()
			.Where(z => loc[z.ResourceKey()].ResourceNotFound)
			.Select(z => z.ResourceKey())
			.ToList();

		await Assert.That(missing).IsEmpty();
	}

	[Test]
	public async Task Widget_zone_labels_read_as_words()
	{
		var loc = PortalLocalizer.Create();

		await Assert.That(loc[WidgetZone.MainContent.ResourceKey()].Value).IsEqualTo("Main Content");

		var raw = Enum.GetValues<WidgetZone>()
			.Where(z => Regex.IsMatch(loc[z.ResourceKey()].Value, "[a-z][A-Z]"))
			.Select(z => z.ToString())
			.ToList();

		await Assert.That(raw).IsEmpty();
	}
}
