using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Returns an empty JSON array for any request (the live preview's widgets degrade gracefully).</summary>
file sealed class EmptyArrayHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("[]", Encoding.UTF8, "application/json")
		});
}

/// <summary>
/// Smoke tests for the drag-and-drop layout editor: the widget palette is filtered to widgets whose
/// allowed zones overlap the scope's zones, and the scope's zones are rendered as drop targets.
/// </summary>
public class LayoutEditorTests : TrackingBunitContext
{
	private ILayoutService _layout = default!;

	public LayoutEditorTests()
	{
		var registry = new WidgetRegistry();
		registry.Register(new WikiIndexWidgetDescriptor());
		registry.Register(new QuickLinksWidgetDescriptor());
		registry.Register(new CharacterGalleryWidgetDescriptor());
		// Spacer is the second placement in the reorder tests: no services, so the live preview renders it.
		registry.Register(new SpacerWidgetDescriptor());

		var layout = new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] = [new WidgetPlacement("WikiIndex", 0, null)]
			},
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false));

		_layout = Substitute.For<ILayoutService>();
		_layout.GetLayoutAsync(LayoutScopes.WikiIndex).Returns(Task.FromResult(layout));

		var apiClient = Track(new HttpClient(new EmptyArrayHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		// Rendering the component directly bypasses the router's [Authorize] gate, so no auth setup is needed.
		// The live preview renders real widgets, so register the services those widgets inject.
		Services
			.AddMudServices()
			.AddSingleton<IWidgetRegistry>(registry)
			.AddSingleton(_layout)
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task PaletteFiltersByScopeZones_AndRendersZones()
	{
		var cut = Render<SharpMUSH.Client.Pages.Admin.Layout.LayoutEditor>(p => p
			.Add(x => x.Scope, LayoutScopes.WikiIndex));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("LayWidgetWikiIndex"))
				throw new InvalidOperationException("editor not loaded yet");
		}, TimeSpan.FromSeconds(5));

		// The stubbed localizer echoes keys, so the palette renders each widget's resource key.
		var markup = cut.Markup;
		await Assert.That(markup).Contains("LayWidgetWikiIndex");
		await Assert.That(markup).Contains("LayWidgetCharacterGallery");
		// QuickLinks has no MainContent zone, so it is filtered out of this scope's palette.
		await Assert.That(markup).DoesNotContain("LayWidgetQuickLinks");
		await Assert.That(markup).Contains("WidgetZoneMainContent");
	}

	/// <summary>
	/// The zone drop targets must set <c>AllowReorder</c>. Without it MudBlazor renders no
	/// <c>mud-dropitem-placeholder</c> — so a drag shows no landing skeleton — and, worse,
	/// <c>CommitTransaction</c> hands <c>ItemDropped</c> an index of <c>-1</c>, which clamps to 0 and
	/// silently teleports every drop to the front of the zone.
	/// </summary>
	[TUnit.Core.Test]
	public async Task ZoneDropTargetsAllowReorder_SoADropSkeletonRenders()
	{
		var cut = await RenderEditorAsync();

		await Assert.That(cut.Markup).Contains("mud-dropitem-placeholder");
	}

	/// <summary>
	/// A range input nested inside the element it resizes slides out from under the pointer as the
	/// element shrinks. The width control is an edge handle instead, exposed to assistive tech as a
	/// separator with a value.
	/// </summary>
	[TUnit.Core.Test]
	public async Task GridItemsExposeAResizeHandle_NotANestedRangeSlider()
	{
		var cut = await RenderEditorAsync();

		await Assert.That(cut.Markup).DoesNotContain("type=\"range\"");

		var handle = cut.Find(".le-resize");
		await Assert.That(handle.GetAttribute("role")).IsEqualTo("separator");
		await Assert.That(handle.GetAttribute("aria-valuenow")).IsEqualTo("12");
		await Assert.That(handle.GetAttribute("aria-valuemin")).IsEqualTo("1");
		await Assert.That(handle.GetAttribute("aria-valuemax")).IsEqualTo("12");
		await Assert.That(handle.GetAttribute("tabindex")).IsEqualTo("0");
	}

	/// <summary>Dragging is never the only path to a width: the handle resizes by keyboard too.</summary>
	[TUnit.Core.Test]
	public async Task ArrowKeysOnTheResizeHandleChangeTheColumnSpan()
	{
		var cut = await RenderEditorAsync();
		await Assert.That(cut.FindAll(".mud-drop-item.le-span-12").Count).IsEqualTo(1);

		cut.Find(".le-resize").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
		await Assert.That(cut.FindAll(".mud-drop-item.le-span-11").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".le-resize").GetAttribute("aria-valuenow")).IsEqualTo("11");

		// Shift takes bigger bites: 11 - 3 - 3 - 3 = 2.
		for (var i = 0; i < 3; i++)
		{
			cut.Find(".le-resize").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft", ShiftKey = true });
		}

		await Assert.That(cut.FindAll(".mud-drop-item.le-span-2").Count).IsEqualTo(1);

		// Clamped at the low end rather than wrapping or going to zero.
		cut.Find(".le-resize").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft", ShiftKey = true });
		cut.Find(".le-resize").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
		await Assert.That(cut.FindAll(".mud-drop-item.le-span-1").Count).IsEqualTo(1);

		cut.Find(".le-resize").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
		await Assert.That(cut.FindAll(".mud-drop-item.le-span-2").Count).IsEqualTo(1);
	}

	/// <summary>
	/// Dragging the grip snaps to whole columns. The grip captures the pointer, so the gesture keeps
	/// arriving there after the widget has shrunk away from under it.
	/// </summary>
	[TUnit.Core.Test]
	public async Task DraggingTheResizeGripSnapsToWholeColumns()
	{
		// One column of travel is 40px, so -160px is exactly four columns narrower.
		JSInterop.Setup<double>("sharpmushLayout.gridColumnWidth", _ => true).SetResult(40d);
		var cut = await RenderEditorAsync();

		cut.Find(".le-resize").PointerDown(new PointerEventArgs { ClientX = 800, PointerId = 7 });
		cut.Find(".le-resize").PointerMove(new PointerEventArgs { ClientX = 640, PointerId = 7 });
		await Assert.That(cut.FindAll(".mud-drop-item.le-span-8").Count).IsEqualTo(1);

		// Rounds to the nearest column rather than truncating: 30px past the boundary is most of one.
		cut.Find(".le-resize").PointerMove(new PointerEventArgs { ClientX = 670, PointerId = 7 });
		await Assert.That(cut.FindAll(".mud-drop-item.le-span-9").Count).IsEqualTo(1);

		// Clamped, not run off the end: 12 columns of travel past full width is still full width.
		cut.Find(".le-resize").PointerMove(new PointerEventArgs { ClientX = 1400, PointerId = 7 });
		await Assert.That(cut.FindAll(".mud-drop-item.le-span-12").Count).IsEqualTo(1);

		cut.Find(".le-resize").PointerUp(new PointerEventArgs { PointerId = 7 });
		await Assert.That(cut.Find(".le-resize").GetAttribute("aria-valuenow")).IsEqualTo("12");
	}

	/// <summary>
	/// Below the narrow breakpoint the zone collapses to one column, so there is no width to drag
	/// against. JS reports 0 and the gesture must not start rather than divide by it.
	/// </summary>
	[TUnit.Core.Test]
	public async Task AZoneWithNoMeasurableColumnsDoesNotStartAResize()
	{
		JSInterop.Setup<double>("sharpmushLayout.gridColumnWidth", _ => true).SetResult(0d);
		var cut = await RenderEditorAsync();

		cut.Find(".le-resize").PointerDown(new PointerEventArgs { ClientX = 800, PointerId = 7 });
		cut.Find(".le-resize").PointerMove(new PointerEventArgs { ClientX = 200, PointerId = 7 });

		await Assert.That(cut.FindAll(".mud-drop-item.le-span-12").Count).IsEqualTo(1);
	}

	/// <summary>
	/// The non-drag reorder path, which is what keyboard and touch users actually have. Also the only
	/// reorder assertion that does not depend on simulating an HTML5 drag transaction.
	/// </summary>
	[TUnit.Core.Test]
	public async Task MoveDownReordersWithinTheZone()
	{
		var cut = await RenderEditorAsync(
			new WidgetPlacement("WikiIndex", 0, null),
			new WidgetPlacement("Spacer", 1, null));

		await Assert.That(PlacedNames(cut)).IsEqualTo("LayWidgetWikiIndex,LayWidgetSpacer");

		cut.FindAll(".le-item-btn--down")[0].Click();
		await Assert.That(PlacedNames(cut)).IsEqualTo("LayWidgetSpacer,LayWidgetWikiIndex");

		cut.FindAll(".le-item-btn--up")[1].Click();
		await Assert.That(PlacedNames(cut)).IsEqualTo("LayWidgetWikiIndex,LayWidgetSpacer");
	}

	/// <summary>Ends of the zone are dead ends, not wrap-arounds.</summary>
	[TUnit.Core.Test]
	public async Task MoveButtonsAreDisabledAtTheEndsOfTheZone()
	{
		var cut = await RenderEditorAsync(
			new WidgetPlacement("WikiIndex", 0, null),
			new WidgetPlacement("Spacer", 1, null));

		await Assert.That(cut.FindAll(".le-item-btn--up")[0].HasAttribute("disabled")).IsTrue();
		await Assert.That(cut.FindAll(".le-item-btn--down")[1].HasAttribute("disabled")).IsTrue();
		await Assert.That(cut.FindAll(".le-item-btn--down")[0].HasAttribute("disabled")).IsFalse();
	}

	/// <summary>Names of the widgets placed in a zone, in render order. Joined so the assertion is order-sensitive.</summary>
	private static string PlacedNames(IRenderedComponent<SharpMUSH.Client.Pages.Admin.Layout.LayoutEditor> cut)
		=> string.Join(",", cut.FindAll(".le-zone-drop .le-item-name").Select(e => e.TextContent.Trim()));

	private async Task<IRenderedComponent<SharpMUSH.Client.Pages.Admin.Layout.LayoutEditor>> RenderEditorAsync(
		params WidgetPlacement[] placements)
	{
		if (placements.Length > 0)
		{
			_layout.GetLayoutAsync(LayoutScopes.WikiIndex).Returns(Task.FromResult(new LayoutConfiguration(
				new Dictionary<WidgetZone, List<WidgetPlacement>> { [WidgetZone.MainContent] = [.. placements] },
				new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false))));
		}

		var cut = Render<SharpMUSH.Client.Pages.Admin.Layout.LayoutEditor>(p => p
			.Add(x => x.Scope, LayoutScopes.WikiIndex));

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".le-zone-drop .le-item").Count == 0)
				throw new InvalidOperationException("editor not loaded yet");
		}, TimeSpan.FromSeconds(5));

		await Task.CompletedTask;
		return cut;
	}
}
