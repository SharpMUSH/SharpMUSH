using Bunit;
using SharpMUSH.Client.Pages.Admin.Config;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Tests for the Restrictions admin config page — command and function restrictions.
/// </summary>
public class RestrictionsTests
{
	[Test]
	public async Task Restrictions_InitialLoad_ShowsTitle()
	{
		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/restrictions/commands", new Dictionary<string, string[]>())
			.OnGet("/api/restrictions/functions", new Dictionary<string, string[]>())
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<Restrictions>();
		await Task.Delay(200);

		await Assert.That(cut.Markup).Contains("Restriction");
	}

	[Test]
	public async Task Restrictions_WithCommandRestrictions_DisplaysCommands()
	{
		var commands = new Dictionary<string, string[]>
		{
			["@nuke"] = new[] { "wizard" },
			["@newpassword"] = new[] { "wizard", "royalty" }
		};

		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/restrictions/commands", commands)
			.OnGet("/api/restrictions/functions", new Dictionary<string, string[]>())
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<Restrictions>();
		await Task.Delay(200);

		var markup = cut.Markup;
		await Assert.That(markup).Contains("@nuke");
	}

	[Test]
	public async Task Restrictions_HasTabs()
	{
		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/restrictions/commands", new Dictionary<string, string[]>())
			.OnGet("/api/restrictions/functions", new Dictionary<string, string[]>())
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<Restrictions>();
		await Task.Delay(200);

		var markup = cut.Markup;
		// Should have tab content for commands and functions
		await Assert.That(markup).Contains("Command");
		await Assert.That(markup).Contains("Function");
	}
}
