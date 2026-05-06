using Bunit;
using SharpMUSH.Client.Pages.Admin.Config;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Tests for the Sitelock admin config page.
/// </summary>
public class SitelockTests
{
	[Test]
	public async Task Sitelock_InitialLoad_ShowsTitle()
	{
		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/sitelock", new Dictionary<string, string[]>())
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<Sitelock>();
		await Task.Delay(200);

		await Assert.That(cut.Markup).Contains("Sitelock");
	}

	[Test]
	public async Task Sitelock_WithRules_DisplaysHostPatterns()
	{
		var rules = new Dictionary<string, string[]>
		{
			["192.168.1.*"] = new[] { "!connect", "!create" },
			["10.0.0.*"] = new[] { "!connect" }
		};

		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/sitelock", rules)
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<Sitelock>();
		await Task.Delay(200);

		var markup = cut.Markup;
		await Assert.That(markup).Contains("192.168.1.*");
		await Assert.That(markup).Contains("10.0.0.*");
	}

	[Test]
	public async Task Sitelock_WithRules_DisplaysAccessRules()
	{
		var rules = new Dictionary<string, string[]>
		{
			["192.168.1.*"] = new[] { "!connect", "!create" }
		};

		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/sitelock", rules)
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<Sitelock>();
		await Task.Delay(200);

		var markup = cut.Markup;
		await Assert.That(markup).Contains("!connect");
	}

	[Test]
	public async Task Sitelock_EmptyRules_ShowsEmptyState()
	{
		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/sitelock", new Dictionary<string, string[]>())
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<Sitelock>();
		await Task.Delay(200);

		await Assert.That(cut.Markup).IsNotNull();
	}

	[Test]
	public async Task Sitelock_HasAddButton()
	{
		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/sitelock", new Dictionary<string, string[]>())
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<Sitelock>();
		await Task.Delay(200);

		var buttons = cut.FindAll("button");
		var addButton = buttons.FirstOrDefault(b =>
			b.TextContent.Contains("Add", StringComparison.OrdinalIgnoreCase));
		await Assert.That(addButton).IsNotNull();
	}
}
