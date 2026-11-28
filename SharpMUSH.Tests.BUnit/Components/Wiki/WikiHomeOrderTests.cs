using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Components.Widgets;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>
/// Staff set the order of the wiki home's category cards on the wiki admin page (<see cref="WikiHomeOrderEditor"/>),
/// and the wiki home shows its cards in that order.
/// </summary>
public class WikiHomeOrderTests : TrackingBunitContext
{
	private static readonly Dictionary<string, string> Names = new() { ["guides"] = "Guides", ["lore"] = "Lore", ["help"] = "Help" };

	private readonly WikiApiFake _fake;

	public WikiHomeOrderTests()
	{
		(_fake, _) = WikiApiFake.Install(this);
	}

	private IRenderedComponent<WikiHomeOrderEditor> RenderEditor(IReadOnlyList<string>? pinned)
		=> Render<WikiHomeOrderEditor>(p => p.Add(x => x.Pinned, pinned).Add(x => x.Names, Names));

	private static List<string> Rows(IRenderedComponent<WikiHomeOrderEditor> cut)
		=> [.. cut.FindAll(".wikiorder-name").Select(n => n.TextContent)];

	private static AngleSharp.Dom.IElement Button(IRenderedComponent<WikiHomeOrderEditor> cut, string text)
		=> cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

	[Test]
	public async Task TheWikiHomeShowsPinnedCardsInTheStoredOrder()
	{
		_fake.Pinned.Clear();
		_fake.Pinned.AddRange(["lore", "guides"]);
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/wiki");
		var cut = Render<WikiIndexWidget>();
		cut.WaitForAssertion(() => cut.Find(".wiki-cat-card"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll(".wiki-cat-card .wiki-cat-name").Select(n => n.TextContent).ToList())
			.IsEquivalentTo(["Lore", "Guides"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task TheEditorListsThePinsInOrderAndMovesARowWithItsArrows()
	{
		var cut = RenderEditor(["guides", "lore", "help"]);
		await Assert.That(Rows(cut)).IsEquivalentTo(["Guides", "Lore", "Help"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(Button(cut, "Save order").HasAttribute("disabled")).IsTrue().Because("nothing has changed yet");
		await Assert.That(cut.Find("button[aria-label='Move up: Guides']").HasAttribute("disabled")).IsTrue();
		await Assert.That(cut.Find("button[aria-label='Move down: Help']").HasAttribute("disabled")).IsTrue();

		await cut.Find("button[aria-label='Move up: Help']").ClickAsync(new());

		await Assert.That(Rows(cut)).IsEquivalentTo(["Guides", "Help", "Lore"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.Find(".wikiorder-note").TextContent).Contains("Unsaved changes");
	}

	[Test]
	public async Task SavingSendsTheOrderAndKeepsWhatTheServerStored()
	{
		_fake.Pinned.Add("help");
		IReadOnlyList<string>? raised = null;
		var cut = Render<WikiHomeOrderEditor>(p => p.Add(x => x.Pinned, ["guides", "lore", "help"]).Add(x => x.Names, Names)
			.Add(x => x.PinnedChanged, order => raised = order));
		await cut.Find("button[aria-label='Move down: Guides']").ClickAsync(new());
		await Button(cut, "Save order").ClickAsync(new());

		cut.WaitForAssertion(() =>
		{
			if (raised is null) throw new InvalidOperationException("not saved yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(_fake.PinOrderRequests.Single()).IsEquivalentTo(["lore", "guides", "help"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(raised!).IsEquivalentTo(["lore", "guides", "help"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.FindAll(".wikiorder-note")).IsEmpty();
		await Assert.That(Button(cut, "Save order").HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task ResetPutsBackTheStoredOrder()
	{
		var cut = RenderEditor(["guides", "lore"]);
		await cut.Find("button[aria-label='Move down: Guides']").ClickAsync(new());
		await Button(cut, "Reset").ClickAsync(new());

		await Assert.That(Rows(cut)).IsEquivalentTo(["Guides", "Lore"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(_fake.PinOrderRequests).IsEmpty();
	}

	[Test]
	public async Task AFailedSaveKeepsTheUnsavedOrder()
	{
		_fake.FailPinOrder = true;
		var cut = RenderEditor(["guides", "lore"]);
		await cut.Find("button[aria-label='Move down: Guides']").ClickAsync(new());
		await Button(cut, "Save order").ClickAsync(new());

		cut.WaitForAssertion(() => cut.Find(".wikiorder-note"), TimeSpan.FromSeconds(5));
		await Assert.That(Rows(cut)).IsEquivalentTo(["Lore", "Guides"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(Button(cut, "Save order").HasAttribute("disabled")).IsFalse().Because("the admin can try again");
	}

	[Test]
	public async Task APinAddedWhileEditingGoesLastWithoutLosingTheUnsavedOrder()
	{
		var cut = RenderEditor(["guides", "lore"]);
		await cut.Find("button[aria-label='Move down: Guides']").ClickAsync(new());

		cut.Render(p => p.Add(x => x.Pinned, ["guides", "lore", "help"]));

		await Assert.That(Rows(cut)).IsEquivalentTo(["Lore", "Guides", "Help"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task NoPinsSaysHowToAddOne()
	{
		var cut = RenderEditor([]);
		await Assert.That(cut.Find(".wikiorder-empty").TextContent).Contains("Pin one on the Categories card");
	}
}
