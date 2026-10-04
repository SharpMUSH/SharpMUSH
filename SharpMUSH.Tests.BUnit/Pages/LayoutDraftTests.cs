using SharpMUSH.Client.Pages.Admin.Layout;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The layout editor's list operations, without rendering the page: what a drop, a move, a removal
/// and a resize do to the draft, and what publishing it writes.
/// </summary>
public class LayoutDraftTests
{
	private static readonly WidgetZone[] Zones = [WidgetZone.MainContent, WidgetZone.RightSidebar];

	private static readonly LayoutSettings Settings = new(false, true, false, "280px", "280px");

	private static LayoutDraft Draft(params (WidgetZone Zone, string Name, int Order, int Span)[] placements) =>
		new(Zones,
			new LayoutConfiguration(
				placements.GroupBy(p => p.Zone).ToDictionary(
					g => g.Key,
					g => g.Select(p => new WidgetPlacement(p.Name, p.Order, null, p.Span)).ToList()),
				Settings),
			name => $"label:{name}");

	private static List<string> Names(LayoutDraft draft, WidgetZone zone) =>
		draft.ToLayout(Settings).Zones[zone].Select(p => p.WidgetName).ToList();

	private static LayoutEditItem Placed(LayoutDraft draft, string name) => draft.Placed.Single(i => i.WidgetName == name);

	[Test]
	public async Task PlacementsLoadInOrderWithTheirLabelsAndSpans()
	{
		var draft = Draft((WidgetZone.MainContent, "B", 1, 6), (WidgetZone.MainContent, "A", 0, 99));

		var placed = draft.Placed.ToList();

		await Assert.That(placed.Select(i => i.WidgetName)).IsEquivalentTo(["A", "B"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(placed[0].DisplayName).IsEqualTo("label:A");
		// An out-of-range stored span is full width.
		await Assert.That(placed[0].Span).IsEqualTo(LayoutDraft.GridColumns);
		await Assert.That(placed[1].Span).IsEqualTo(6);
	}

	[Test]
	public async Task ATemplateDroppedOnAZoneAddsAFreshPlacementThere()
	{
		var draft = Draft((WidgetZone.MainContent, "A", 0, 12));
		var template = new LayoutEditItem { WidgetName = "Stats", DisplayName = "Stats", IsTemplate = true };

		var changed = draft.Drop(template, nameof(WidgetZone.MainContent), index: 0);

		await Assert.That(changed).IsTrue();
		await Assert.That(Names(draft, WidgetZone.MainContent)).IsEquivalentTo(["Stats", "A"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(draft.Placed.Any(i => ReferenceEquals(i, template))).IsFalse();
	}

	[Test]
	public async Task ATemplateDroppedBackOnThePaletteChangesNothing()
	{
		var draft = Draft((WidgetZone.MainContent, "A", 0, 12));

		var changed = draft.Drop(new LayoutEditItem { WidgetName = "Stats", IsTemplate = true }, LayoutDraft.Palette, 0);

		await Assert.That(changed).IsFalse();
		await Assert.That(draft.Placed.Count()).IsEqualTo(1);
	}

	[Test]
	public async Task APlacementDroppedOnAnotherZoneMovesThereAtTheClampedIndex()
	{
		var draft = Draft((WidgetZone.MainContent, "A", 0, 12), (WidgetZone.RightSidebar, "R", 0, 12));
		var a = Placed(draft, "A");

		var changed = draft.Drop(a, nameof(WidgetZone.RightSidebar), index: 99);

		await Assert.That(changed).IsTrue();
		await Assert.That(a.Zone).IsEqualTo(nameof(WidgetZone.RightSidebar));
		await Assert.That(Names(draft, WidgetZone.MainContent)).IsEmpty();
		await Assert.That(Names(draft, WidgetZone.RightSidebar)).IsEquivalentTo(["R", "A"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task APlacementDroppedOnThePaletteIsRemoved()
	{
		var draft = Draft((WidgetZone.MainContent, "A", 0, 12));

		var changed = draft.Drop(Placed(draft, "A"), LayoutDraft.Palette, 0);

		await Assert.That(changed).IsTrue();
		await Assert.That(draft.Placed).IsEmpty();
	}

	[Test]
	public async Task MovingSwapsNeighboursAndStopsAtTheEnds()
	{
		var draft = Draft((WidgetZone.MainContent, "A", 0, 12), (WidgetZone.MainContent, "B", 1, 12));
		var a = Placed(draft, "A");

		await Assert.That(draft.CanMove(a, -1)).IsFalse();
		await Assert.That(draft.MoveBy(a, -1)).IsFalse();
		await Assert.That(draft.MoveBy(a, 1)).IsTrue();
		await Assert.That(Names(draft, WidgetZone.MainContent)).IsEquivalentTo(["B", "A"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(draft.CanMove(a, 1)).IsFalse();
	}

	[Test]
	public async Task PublishingNumbersEachZoneFromZeroAndKeepsSpans()
	{
		var draft = Draft((WidgetZone.MainContent, "A", 5, 4), (WidgetZone.MainContent, "B", 9, 8));

		var layout = draft.ToLayout(Settings);

		await Assert.That(layout.Settings).IsEqualTo(Settings);
		await Assert.That(layout.Zones[WidgetZone.MainContent].Select(p => (p.WidgetName, p.Order, p.Span)))
			.IsEquivalentTo([("A", 0, 4), ("B", 1, 8)], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(layout.Zones[WidgetZone.RightSidebar]).IsEmpty();
	}

	[Test]
	[Arguments("ArrowRight", false, 7)]
	[Arguments("ArrowRight", true, 9)]
	[Arguments("ArrowLeft", true, 3)]
	[Arguments("Enter", false, 6)]
	public async Task ArrowKeysResizeByOneColumnOrThree(string key, bool shift, int expected)
	{
		await Assert.That(LayoutDraft.KeyedSpan(6, key, shift)).IsEqualTo(expected);
	}

	[Test]
	public async Task ResizingStaysWithinTheGrid()
	{
		await Assert.That(LayoutDraft.KeyedSpan(12, "ArrowRight", shift: true)).IsEqualTo(12);
		await Assert.That(LayoutDraft.KeyedSpan(1, "ArrowLeft", shift: false)).IsEqualTo(1);
		await Assert.That(LayoutDraft.DraggedSpan(6, travelled: 260, columnWidth: 100)).IsEqualTo(9);
		await Assert.That(LayoutDraft.DraggedSpan(6, travelled: -10_000, columnWidth: 100)).IsEqualTo(1);
	}
}
