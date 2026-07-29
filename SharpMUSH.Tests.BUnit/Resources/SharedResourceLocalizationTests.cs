using System.Globalization;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
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
		using var culture = CultureScope.For("en");
		var loc = PortalLocalizer.Create();

		await Assert.That(loc["SceneArchive"].ResourceNotFound).IsFalse();
		await Assert.That(loc["SceneArchive"].Value).IsEqualTo("Scenes");
	}

	[Test]
	[Arguments("fr", "Panneau d'administration")]
	[Arguments("de", "Admin-Panel")]
	public async Task Translated_cultures_resolve_to_their_satellite_value(string tag, string expected)
	{
		using var culture = CultureScope.For(tag);
		var loc = PortalLocalizer.Create();

		await Assert.That(loc["AdminPanel"].Value).IsEqualTo(expected);
	}

	[Test]
	public async Task No_resource_value_is_left_as_its_own_camel_case_key()
	{
		using var culture = CultureScope.For("en");
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
		using var culture = CultureScope.For("en");
		var loc = PortalLocalizer.Create();

		await Assert.That(loc[WidgetZone.MainContent.ResourceKey()].Value).IsEqualTo("Main Content");

		var raw = Enum.GetValues<WidgetZone>()
			.Where(z => Regex.IsMatch(loc[z.ResourceKey()].Value, "[a-z][A-Z]"))
			.Select(z => z.ToString())
			.ToList();

		await Assert.That(raw).IsEmpty();
	}

	[Test]
	public async Task Every_portal_permission_has_a_label_group_and_description_in_the_resx()
	{
		var loc = PortalLocalizer.Create();

		var missing = PortalPermission.All
			.SelectMany(d => new[] { d.LabelKey, d.GroupKey, d.DescriptionKey })
			.Distinct()
			.Where(k => loc[k].ResourceNotFound)
			.ToList();

		await Assert.That(missing).IsEmpty();
	}

	[Test]
	public async Task Every_wiki_localization_string_is_in_the_resx()
	{
		var loc = PortalLocalizer.Create();

		string[] keys =
		[
			"WikiFallbackNotice", "WikiFallbackCreateTranslation", "WikiAvailableTranslations",
			"WkLocaleSelector", "WkAddTranslation", "WkSourceLocaleLabel", "WkInheritedFromSource",
			"WkTranslationSaved", "WkTranslationSaveFailed", "WkDeleteTranslation",
			"WkTranslationDeleteConfirmTitle", "WkTranslationDeleteConfirmText",
			"WkCustomLocalePlaceholder", "WkHistoryLocale",
			"WkTranslationConflict", "WkTranslationReload",
			"WikiTranslations", "WikiLocaleFilter", "WikiAllLocales", "WikiUntranslatedOnly",
			"ResWikiStatTranslations",
		];

		var missing = keys.Where(k => loc[k].ResourceNotFound).ToList();

		await Assert.That(missing).IsEmpty();
	}

	[Test]
	public async Task Every_displayed_enum_member_has_a_label_in_the_resx()
	{
		var loc = PortalLocalizer.Create();

		var keys = Enum.GetValues<MushObjectType>().Select(v => v.ResourceKey())
			.Concat(Enum.GetValues<ApplicationKind>().Select(v => v.ResourceKey()))
			.Concat(Enum.GetValues<PackageRevisionKind>().Select(v => v.ResourceKey()));

		var missing = keys.Where(k => loc[k].ResourceNotFound).ToList();

		await Assert.That(missing).IsEmpty();
	}
}
