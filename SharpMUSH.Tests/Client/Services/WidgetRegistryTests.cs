using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Unit tests for <see cref="WidgetRegistry"/>.
/// </summary>
public class WidgetRegistryTests
{
	private static WidgetRegistry MakeRegistry() => new();

	// ─── Registration ──────────────────────────────────────────────────────

	[Test]
	public async Task Register_ThenGetWidget_ReturnsDescriptor()
	{
		var registry = MakeRegistry();
		var descriptor = new QuickLinksWidgetDescriptor();

		registry.Register(descriptor);
		var result = registry.GetWidget("QuickLinks");

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Name).IsEqualTo("QuickLinks");
	}

	[Test]
	public async Task Register_OverwritesExistingEntry_WithSameName()
	{
		var registry = MakeRegistry();
		var first = new QuickLinksWidgetDescriptor();
		var second = new QuickLinksWidgetDescriptor(); // same Name

		registry.Register(first);
		registry.Register(second);

		var result = registry.GetWidget("QuickLinks");
		await Assert.That(result).IsEqualTo(second);
	}

	[Test]
	public async Task Register_Null_Throws()
	{
		var registry = MakeRegistry();
		await Assert.ThrowsAsync<ArgumentNullException>(
			async () => registry.Register(null!));
	}

	// ─── GetWidget ─────────────────────────────────────────────────────────

	[Test]
	public async Task GetWidget_UnknownName_ReturnsNull()
	{
		var registry = MakeRegistry();
		var result = registry.GetWidget("NonExistent");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task GetWidget_NullName_Throws()
	{
		var registry = MakeRegistry();
		await Assert.ThrowsAsync<ArgumentNullException>(
			async () => registry.GetWidget(null!));
	}

	[Test]
	public async Task GetWidget_IsCaseSensitive()
	{
		var registry = MakeRegistry();
		registry.Register(new QuickLinksWidgetDescriptor());

		// Ordinal comparison — lowercase should not resolve
		var result = registry.GetWidget("quicklinks");
		await Assert.That(result).IsNull();
	}

	// ─── GetAllWidgets ─────────────────────────────────────────────────────

	[Test]
	public async Task GetAllWidgets_EmptyRegistry_ReturnsEmpty()
	{
		var registry = MakeRegistry();
		var result = registry.GetAllWidgets();

		await Assert.That(result.Count).IsEqualTo(0);
	}

	[Test]
	public async Task GetAllWidgets_ReturnsAllRegisteredWidgets()
	{
		var registry = MakeRegistry();
		registry.Register(new QuickLinksWidgetDescriptor());
		registry.Register(new WelcomeTextWidgetDescriptor());

		var result = registry.GetAllWidgets();
		await Assert.That(result.Count).IsEqualTo(2);
	}

	// ─── GetWidgetsForZone ─────────────────────────────────────────────────

	[Test]
	public async Task GetWidgetsForZone_ReturnsOnlyWidgetsAllowedInZone()
	{
		var registry = MakeRegistry();
		registry.Register(new QuickLinksWidgetDescriptor()); // TopBar, LeftSidebar, RightSidebar, Footer
		registry.Register(new WelcomeTextWidgetDescriptor()); // MainContent only

		var topBar = registry.GetWidgetsForZone(WidgetZone.TopBar);
		var mainContent = registry.GetWidgetsForZone(WidgetZone.MainContent);

		await Assert.That(topBar.Count).IsEqualTo(1);
		await Assert.That(topBar[0].Name).IsEqualTo("QuickLinks");

		await Assert.That(mainContent.Count).IsEqualTo(1);
		await Assert.That(mainContent[0].Name).IsEqualTo("WelcomeText");
	}

	[Test]
	public async Task GetWidgetsForZone_ZoneWithNoWidgets_ReturnsEmpty()
	{
		var registry = MakeRegistry();
		registry.Register(new WelcomeTextWidgetDescriptor()); // MainContent only

		var result = registry.GetWidgetsForZone(WidgetZone.Footer);
		await Assert.That(result.Count).IsEqualTo(0);
	}

	[Test]
	public async Task GetWidgetsForZone_AllZones_QuickLinksAllowed()
	{
		var registry = MakeRegistry();
		registry.Register(new QuickLinksWidgetDescriptor());

		var zones = new[] { WidgetZone.TopBar, WidgetZone.LeftSidebar, WidgetZone.RightSidebar, WidgetZone.Footer };
		var results = zones.Select(z => registry.GetWidgetsForZone(z)).ToList();

		foreach (var result in results)
			await Assert.That(result.Count).IsEqualTo(1);
	}
}
