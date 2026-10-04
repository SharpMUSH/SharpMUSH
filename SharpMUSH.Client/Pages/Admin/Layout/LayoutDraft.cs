using System.Text.Json;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Pages.Admin.Layout;

/// <summary>One widget on the layout editor's board: a placement in a zone, or a palette template.</summary>
public sealed class LayoutEditItem
{
	public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
	public string WidgetName { get; init; } = string.Empty;
	public string DisplayName { get; init; } = string.Empty;
	public string Zone { get; set; } = LayoutDraft.Palette;
	public JsonElement? Config { get; set; }
	public bool IsTemplate { get; init; }

	/// <summary>Grid width in columns (1–12). Only meaningful in grid zones.</summary>
	public int Span { get; set; } = LayoutDraft.GridColumns;
}

/// <summary>
/// The layout editor's working copy: which widgets sit in which zone, in what order and at what width,
/// and the <see cref="LayoutConfiguration"/> that publishing it writes.
/// </summary>
/// <remarks>
/// <para>Lifted out of <c>LayoutEditor.razor</c>, where these list operations sat among the drag,
/// pointer-capture and dialog handling and could only be exercised by rendering the page. The page
/// keeps everything that touches MudBlazor or the browser; this holds the state those gestures change
/// and the rules for changing it, and is tested on its own.</para>
///
/// <para>Each zone's list is mutated in place. The page hands MudBlazor's drop container one flattened
/// list that it also mutates in place (MudBlazor#7191), and re-derives it from <see cref="Placed"/>
/// after every change.</para>
/// </remarks>
public sealed class LayoutDraft
{
	/// <summary>The drop-zone identifier of the palette; a placement dropped there is removed.</summary>
	public const string Palette = "palette";

	/// <summary>Columns a grid zone lays out. Mirrors <c>repeat(12, 1fr)</c> in the stylesheet.</summary>
	public const int GridColumns = 12;

	private readonly IReadOnlyList<WidgetZone> _zoneOrder;
	private readonly Dictionary<string, List<LayoutEditItem>> _zones;

	/// <param name="zones">The scope's zones, in display order.</param>
	/// <param name="layout">The published layout to start from.</param>
	/// <param name="labelOf">The palette label for a placed widget's name.</param>
	public LayoutDraft(IReadOnlyList<WidgetZone> zones, LayoutConfiguration layout, Func<string, string> labelOf)
	{
		_zoneOrder = zones;
		_zones = zones.ToDictionary(
			z => z.ToString(),
			z => (layout.Zones.TryGetValue(z, out var list) ? list : [])
				.OrderBy(p => p.Order)
				.Select(p => new LayoutEditItem
				{
					WidgetName = p.WidgetName,
					DisplayName = labelOf(p.WidgetName),
					Zone = z.ToString(),
					Config = p.Config,
					Span = ClampSpan(p.Span)
				})
				.ToList());
	}

	/// <summary>Every placement, zone by zone in display order, each zone in placement order.</summary>
	public IEnumerable<LayoutEditItem> Placed => _zoneOrder.SelectMany(z => _zones[z.ToString()]);

	/// <summary>A stored span outside 1–12 is a full-width placement.</summary>
	public static int ClampSpan(int span) => span is >= 1 and <= GridColumns ? span : GridColumns;

	/// <summary>
	/// The span a pointer resize has reached: the span it started at, plus the whole columns the pointer
	/// has travelled, kept within 1–12.
	/// </summary>
	public static int DraggedSpan(int startSpan, double travelled, double columnWidth) =>
		Math.Clamp(startSpan + (int)Math.Round(travelled / columnWidth), 1, GridColumns);

	/// <summary>
	/// The span an arrow key moves <paramref name="span"/> to — one column, three with Shift — or the
	/// same span for any other key.
	/// </summary>
	public static int KeyedSpan(int span, string key, bool shift)
	{
		var step = shift ? 3 : 1;
		var delta = key switch
		{
			"ArrowRight" => step,
			"ArrowLeft" => -step,
			_ => 0
		};
		return Math.Clamp(ClampSpan(span) + delta, 1, GridColumns);
	}

	/// <summary>Whether <paramref name="item"/> has a neighbour <paramref name="delta"/> places along its zone.</summary>
	public bool CanMove(LayoutEditItem item, int delta)
	{
		if (!_zones.TryGetValue(item.Zone, out var list))
		{
			return false;
		}

		var index = list.IndexOf(item);
		return index >= 0 && index + delta >= 0 && index + delta < list.Count;
	}

	/// <summary>Swaps <paramref name="item"/> with that neighbour.</summary>
	/// <returns>Whether anything moved.</returns>
	public bool MoveBy(LayoutEditItem item, int delta)
	{
		if (!CanMove(item, delta))
		{
			return false;
		}

		var list = _zones[item.Zone];
		var index = list.IndexOf(item);
		(list[index], list[index + delta]) = (list[index + delta], list[index]);
		return true;
	}

	/// <summary>
	/// Applies a drop: a palette template dropped on a zone adds a fresh placement there (the template
	/// stays in the palette); a placement dropped on a zone moves there; a placement dropped on the
	/// palette is removed.
	/// </summary>
	/// <param name="target">The drop zone's identifier — a zone name, or <see cref="Palette"/>.</param>
	/// <param name="index">Where in the target the item landed; clamped to the zone.</param>
	/// <returns>Whether the layout changed.</returns>
	public bool Drop(LayoutEditItem item, string target, int index)
	{
		if (item.IsTemplate)
		{
			if (target == Palette || !_zones.TryGetValue(target, out var list))
			{
				return false;
			}

			InsertAt(list, new LayoutEditItem { WidgetName = item.WidgetName, DisplayName = item.DisplayName, Zone = target }, index);
			return true;
		}

		RemoveFromZones(item);
		if (target == Palette)
		{
			return true;
		}

		if (!_zones.TryGetValue(target, out var destination))
		{
			return false;
		}

		item.Zone = target;
		InsertAt(destination, item, index);
		return true;
	}

	/// <summary>Takes <paramref name="item"/> off the board.</summary>
	public void Remove(LayoutEditItem item) => RemoveFromZones(item);

	/// <summary>The layout the draft describes: each zone's placements numbered in order.</summary>
	public LayoutConfiguration ToLayout(LayoutSettings settings) =>
		new(_zoneOrder.ToDictionary(
				z => z,
				z => _zones[z.ToString()]
					.Select((it, idx) => new WidgetPlacement(it.WidgetName, idx, it.Config, ClampSpan(it.Span)))
					.ToList()),
			settings);

	private static void InsertAt(List<LayoutEditItem> list, LayoutEditItem item, int index) =>
		list.Insert(Math.Clamp(index, 0, list.Count), item);

	private void RemoveFromZones(LayoutEditItem item)
	{
		foreach (var list in _zones.Values)
		{
			list.Remove(item);
		}
	}
}
