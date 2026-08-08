using Microsoft.AspNetCore.Components;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// /websocket-test was a development harness that shipped to production: no <c>[Authorize]</c>, raw
/// MudBlazor against the Phosphor design system used everywhere else, a hardcoded
/// <c>ws://localhost:4202/ws</c>, and a topbar title that disagreed with its own heading. /play
/// exercises the same WebSocket path end to end, so the page is gone. This encodes that it stays
/// gone, and that its only unique dependency went with it.
/// </summary>
public class DevHarnessRouteTests
{
	private static IEnumerable<string> ClientRoutes() =>
		typeof(SharpMUSH.Client.Pages.Play).Assembly
			.GetTypes()
			.Where(t => typeof(IComponent).IsAssignableFrom(t))
			.SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>())
			.Select(r => r.Template);

	[Test]
	public async Task No_component_routes_the_websocket_dev_harness()
	{
		await Assert.That(ClientRoutes()).DoesNotContain("/websocket-test");
	}

	/// <summary>
	/// The harness was the only <c>@inject IWebSocketClientService</c> site — everything else builds
	/// its websocket client through ActivatorUtilities on the concrete type, deliberately (see
	/// AddTerminalServices). The interface itself stays: TerminalService takes it, and
	/// IPlayWebSocketClientService derives from it.
	/// </summary>
	[Test]
	public async Task The_websocket_client_interface_is_not_resolved_from_the_container()
	{
		var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		services.AddTerminalServices();

		await Assert.That(services.Any(d => d.ServiceType == typeof(IWebSocketClientService))).IsFalse();
	}
}
