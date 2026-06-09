using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Unit tests for <see cref="LayoutService"/>.
/// </summary>
public class LayoutServiceTests
{
	/// <summary>
	/// Creates a JS runtime stub that returns <paramref name="storedJson"/> for localStorage.getItem.
	/// </summary>
	private static IJSRuntime MakeJs(string? storedJson = null)
	{
		var js = Substitute.For<IJSRuntime>();
		js.InvokeAsync<string?>(
				"localStorage.getItem",
				Arg.Any<object[]>())
			.Returns(storedJson);
		return js;
	}

	// ─── GetDefaultLayout ─────────────────────────────────────────────────

	[Test]
	public async Task GetDefaultLayout_ContainsAllZones()
	{
		var svc = new LayoutService(MakeJs());
		var layout = svc.GetDefaultLayout();

		// All 5 WidgetZone values should be present as keys
		foreach (var zone in Enum.GetValues<WidgetZone>())
			await Assert.That(layout.Zones.ContainsKey(zone)).IsTrue();
	}

	[Test]
	public async Task GetDefaultLayout_TopBarHasQuickLinks()
	{
		var svc = new LayoutService(MakeJs());
		var layout = svc.GetDefaultLayout();

		var topBar = layout.Zones[WidgetZone.TopBar];
		await Assert.That(topBar.Count).IsEqualTo(1);
		await Assert.That(topBar[0].WidgetName).IsEqualTo("QuickLinks");
	}

	[Test]
	public async Task GetDefaultLayout_MainContentHasWelcomeText()
	{
		var svc = new LayoutService(MakeJs());
		var layout = svc.GetDefaultLayout();

		var main = layout.Zones[WidgetZone.MainContent];
		await Assert.That(main.Count).IsEqualTo(1);
		await Assert.That(main[0].WidgetName).IsEqualTo("WelcomeText");
	}

	[Test]
	public async Task GetDefaultLayout_SidebarsDisabledByDefault()
	{
		var svc = new LayoutService(MakeJs());
		var layout = svc.GetDefaultLayout();

		await Assert.That(layout.Settings.LeftSidebarEnabled).IsFalse();
		await Assert.That(layout.Settings.RightSidebarEnabled).IsFalse();
	}

	// ─── GetLayoutAsync ───────────────────────────────────────────────────

	[Test]
	public async Task GetLayoutAsync_WhenLocalStorageEmpty_ReturnsDefault()
	{
		var svc = new LayoutService(MakeJs(null));
		var layout = await svc.GetLayoutAsync();

		// Must have all zones
		foreach (var zone in Enum.GetValues<WidgetZone>())
			await Assert.That(layout.Zones.ContainsKey(zone)).IsTrue();
	}

	[Test]
	public async Task GetLayoutAsync_CachesResult_JsCalledOnlyOnce()
	{
		var js = MakeJs(null);
		var svc = new LayoutService(js);

		var first = await svc.GetLayoutAsync();
		var second = await svc.GetLayoutAsync();

		await Assert.That(ReferenceEquals(first, second)).IsTrue();
		// JS was invoked exactly once (second call hit cache)
		await js.Received(1).InvokeAsync<string?>("localStorage.getItem", Arg.Any<object[]>());
	}

	[Test]
	public async Task GetLayoutAsync_MalformedJson_FallsBackToDefault()
	{
		var svc = new LayoutService(MakeJs("{not valid json"));
		var layout = await svc.GetLayoutAsync();

		// Should not throw; returns default
		await Assert.That(layout).IsNotNull();
		await Assert.That(layout.Zones.ContainsKey(WidgetZone.TopBar)).IsTrue();
	}

	// ─── SaveLayoutAsync ──────────────────────────────────────────────────

	[Test]
	public async Task SaveLayoutAsync_Null_Throws()
	{
		var svc = new LayoutService(MakeJs());
		await Assert.ThrowsAsync<ArgumentNullException>(
			async () => await svc.SaveLayoutAsync(null!));
	}

	[Test]
	public async Task SaveLayoutAsync_FiresOnLayoutChanged()
	{
		var js = MakeJs(null);
		js.InvokeAsync<string?>("localStorage.setItem", Arg.Any<object[]>())
			.Returns(default(ValueTask<string?>));

		var svc = new LayoutService(js);
		var fired = false;
		svc.OnLayoutChanged += () => fired = true;

		await svc.SaveLayoutAsync(svc.GetDefaultLayout());

		await Assert.That(fired).IsTrue();
	}

	[Test]
	public async Task SaveLayoutAsync_UpdatesCache_SubsequentGetReturnsNewLayout()
	{
		var js = MakeJs(null);
		var svc = new LayoutService(js);

		var modified = svc.GetDefaultLayout() with
		{
			Settings = new LayoutSettings(
				LeftSidebarEnabled: true,
				RightSidebarEnabled: true)
		};

		await svc.SaveLayoutAsync(modified);

		var result = await svc.GetLayoutAsync();
		await Assert.That(result.Settings.LeftSidebarEnabled).IsTrue();
	}
}
