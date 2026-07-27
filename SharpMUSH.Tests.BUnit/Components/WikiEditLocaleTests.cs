using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// A translation inherits the source page's category and tags structurally — <c>WikiTranslation</c> has
/// nowhere to store its own. These tests assert the editor makes that legible (visible but disabled with a
/// hint) rather than mysterious, and that the fields a translation <em>does</em> own stay editable.
/// </summary>
public class WikiEditLocaleTests : BunitContext
{
	public WikiEditLocaleTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<WikiMarkdigPipeline>();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static WikiArticle Draft() => new("Dragons", "body", null, "<p>body</p>")
	{
		Id = "1",
		Slug = "dragons",
		Category = "lore",
		Tags = ["myth"],
		Published = true,
	};

	// The locale selector is a MudSelect, which needs a MudPopoverProvider in the render tree, so the
	// editor is hosted inside MudHarness (shared with the schema-renderer shape tests). The provider
	// contributes only an empty container, so the markup assertions below are unaffected.
	private IRenderedComponent<WikiEdit> RenderEditor(
		string selectedLocale, Action<string>? onLocaleChanged = null)
	{
		var host = Render<MudHarness>(p => p.AddChildContent<WikiEdit>(cp => cp
			.Add(c => c.Article, Draft())
			.Add(c => c.SourceLocale, "en")
			.Add(c => c.SelectedLocale, selectedLocale)
			.Add(c => c.AvailableLocales, new[] { "en", "fr" })
			.Add(c => c.OnLocaleChanged,
				EventCallback.Factory.Create(this, onLocaleChanged ?? (_ => { })))));

		return host.FindComponent<WikiEdit>();
	}

	[Test]
	public async Task Category_and_tags_are_enabled_on_the_source_locale()
	{
		var cut = RenderEditor("en");

		await Assert.That(cut.Find(".wiki-edit-cat input").HasAttribute("disabled")).IsFalse();
		await Assert.That(cut.Find(".wiki-edit-taginput").HasAttribute("disabled")).IsFalse();
		await Assert.That(cut.FindAll(".wiki-edit-inherited")).IsEmpty();
	}

	[Test]
	public async Task Category_and_tags_are_disabled_on_a_translation()
	{
		var cut = RenderEditor("fr");

		await Assert.That(cut.Find(".wiki-edit-cat input").HasAttribute("disabled"))
			.IsTrue()
			.Because("a translation has nowhere to store its own category");
		await Assert.That(cut.Find(".wiki-edit-taginput").HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task Inherited_hint_explains_why_the_fields_are_disabled()
	{
		var cut = RenderEditor("fr");

		await Assert.That(cut.Markup).Contains("WkInheritedFromSource");
	}

	[Test]
	public async Task Title_body_and_published_stay_editable_on_a_translation()
	{
		var cut = RenderEditor("fr");

		await Assert.That(cut.Find(".wiki-edit-title").HasAttribute("disabled")).IsFalse();
		await Assert.That(cut.Find(".wiki-edit-textarea").HasAttribute("disabled")).IsFalse();
		await Assert.That(cut.Find(".wiki-edit-pub input").HasAttribute("disabled"))
			.IsFalse()
			.Because("a translation owns its own Published flag — that is how a translator drafts French while English stays live");
	}

	[Test]
	public async Task Region_variant_of_the_source_locale_is_not_treated_as_a_translation()
	{
		// SameLanguage ignores region, and it must here too: editing en-GB on an en-sourced page is still
		// editing the page itself, so disabling its category would lock the source page's own metadata.
		var cut = RenderEditor("en-GB");

		await Assert.That(cut.Instance.IsTranslationEdit).IsFalse();
		await Assert.That(cut.Find(".wiki-edit-cat input").HasAttribute("disabled")).IsFalse();
	}

	[Test]
	public async Task Locale_selector_offers_the_source_every_translation_and_an_add_option()
	{
		var cut = RenderEditor("en");

		await Assert.That(cut.Markup).Contains("WkLocaleSelector");
		await Assert.That(cut.Markup).Contains("WkAddTranslation");
	}

	[Test]
	public async Task Changing_the_locale_raises_OnLocaleChanged()
	{
		var raised = new List<string>();
		var cut = RenderEditor("en", raised.Add);

		await cut.Instance.SelectLocaleAsync("fr");

		await Assert.That(raised).IsEquivalentTo(new[] { "fr" });
	}

	[Test]
	public async Task Reselecting_the_current_locale_raises_nothing()
	{
		// MudSelect re-emits ValueChanged on its own initial binding pass. Re-raising there would navigate
		// the host straight back to the URL it is already on, and the editor would look like it reloads
		// itself the moment it opens.
		var raised = new List<string>();
		var cut = RenderEditor("en", raised.Add);

		await cut.Instance.SelectLocaleAsync("en");

		await Assert.That(raised).IsEmpty();
	}

	[Test]
	public async Task Source_locale_edit_does_not_send_a_translation_expected_revision()
	{
		// Editing the source locale goes through UpdatePageAsync, not the translation endpoint, so there is
		// no translation revision to compare against. Asserted here because getting it wrong the other way —
		// sending the page's revision number as a translation's — would make every first save look stale.
		var cut = RenderEditor("en");

		await Assert.That(cut.Instance.IsTranslationEdit).IsFalse();
	}

	[Test]
	public async Task Translation_edit_is_reported_as_a_translation()
	{
		// The negative above passes trivially if IsTranslationEdit is hardcoded false; this pins the other side.
		var cut = RenderEditor("fr");

		await Assert.That(cut.Instance.IsTranslationEdit).IsTrue();
	}
}
