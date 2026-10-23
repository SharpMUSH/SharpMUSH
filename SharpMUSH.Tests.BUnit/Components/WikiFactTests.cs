using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// A page's facts in the portal: <c>WikiFactBox</c> under the page's body, and <c>WikiFactEditor</c> beside the
/// categories in the editor, which changes the draft's facts in place.
/// </summary>
public class WikiFactTests : BunitContext
{
	private static readonly WikiFactPropertyDto[] Properties =
	[
		new("pf2e:class", "page", true, []),
		new("pf2e:level", "number", false, []),
		new("pf2e:tradition", "keyword", true, ["arcane", "divine", "occult", "primal"]),
	];

	public WikiFactTests() => Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

	private IRenderedComponent<WikiFactEditor> Editor(List<WikiFactDto> facts, bool disabled = false) =>
		Render<WikiFactEditor>(p => p.Add(c => c.Facts, facts).Add(c => c.Properties, Properties).Add(c => c.Disabled, disabled));

	[Test]
	public async Task TheBoxShowsEachPropertyAndLinksAPage()
	{
		var cut = Render<WikiFactBox>(p => p.Add(c => c.Facts, new List<WikiFactDto>
		{
			new("pf2e:class", "wiki_page/7") { PageTitle = "Wizard", PageSlug = "wizard", PageNamespace = "main" },
			new("pf2e:level", "3"),
			new("pf2e:tradition", "arcane"),
			new("pf2e:tradition", "primal"),
		}));

		await Assert.That(cut.FindAll("dt").Select(d => d.TextContent)).IsEquivalentTo(["pf2e:class", "pf2e:level", "pf2e:tradition"]);
		var link = cut.Find("dd a");
		await Assert.That((link.TextContent, link.GetAttribute("href"))).IsEqualTo(("Wizard", "/wiki/main/wizard"));
		await Assert.That(cut.FindAll("dd")[2].TextContent).IsEqualTo("arcane · primal");
	}

	[Test]
	public async Task TheBoxIsAbsentWithoutFacts()
	{
		var cut = Render<WikiFactBox>(p => p.Add(c => c.Facts, new List<WikiFactDto>()));
		await Assert.That(cut.FindAll(".wiki-facts")).IsEmpty();
	}

	[Test]
	public async Task TheEditorChangesTheDraftsFactsInPlace()
	{
		var facts = new List<WikiFactDto>
		{
			new("pf2e:class", "wiki_page/7") { PageTitle = "Wizard", PageSlug = "wizard", PageNamespace = "main" },
			new("pf2e:level", "3"),
		};
		var cut = Editor(facts);

		// A page value shows the page's title, and what is typed over it is sent as typed.
		var inputs = cut.FindAll(".wiki-edit-fact input");
		await Assert.That(inputs[0].GetAttribute("value")).IsEqualTo("Wizard");
		await inputs[0].ChangeAsync(new ChangeEventArgs { Value = "Help:Sorcerer" });
		await Assert.That(facts[0]).IsEqualTo(new WikiFactDto("pf2e:class", "Help:Sorcerer"));
		await Assert.That(cut.FindAll(".wiki-edit-fact input")[1].GetAttribute("type")).IsEqualTo("number");

		// A single-valued property the page states is not offered again; a keyword starts at its first value.
		var adding = cut.Find(".wiki-edit-facts > select");
		await Assert.That(adding.QuerySelectorAll("option").Select(o => o.GetAttribute("value")))
			.IsEquivalentTo(["", "pf2e:class", "pf2e:tradition"]);
		await adding.ChangeAsync(new ChangeEventArgs { Value = "pf2e:tradition" });
		await cut.Find(".wiki-edit-fact-add").ClickAsync();
		await Assert.That(facts[^1]).IsEqualTo(new WikiFactDto("pf2e:tradition", "arcane"));
		await cut.FindAll(".wiki-edit-fact select")[0].ChangeAsync(new ChangeEventArgs { Value = "occult" });
		await Assert.That(facts[^1]).IsEqualTo(new WikiFactDto("pf2e:tradition", "occult"));

		await cut.FindAll(".wiki-edit-fact button")[1].ClickAsync();
		await Assert.That(facts.Select(f => f.Property)).IsEquivalentTo(["pf2e:class", "pf2e:tradition"]);
	}

	[Test]
	public async Task ATranslationCannotChangeItsPagesFacts()
	{
		var cut = Editor([new("pf2e:level", "3")], disabled: true);
		await Assert.That(cut.FindAll(".wiki-edit-facts input, .wiki-edit-facts select, .wiki-edit-facts button")
			.All(e => e.HasAttribute("disabled"))).IsTrue();
	}

	[Test]
	public async Task WithoutPropertiesTheEditorShowsNothing()
	{
		var cut = Render<WikiFactEditor>(p => p.Add(c => c.Facts, new List<WikiFactDto>()));
		await Assert.That(cut.Markup.Trim()).IsEmpty();
	}
}
