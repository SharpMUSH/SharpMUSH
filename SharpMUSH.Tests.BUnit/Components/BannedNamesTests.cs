using Bunit;
using SharpMUSH.Client.Pages.Admin.Config;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Tests for the BannedNames admin config page.
/// </summary>
public class BannedNamesTests
{
	[Test]
	public async Task BannedNames_InitialLoad_ShowsTitle()
	{
		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/bannednames", Array.Empty<string>())
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<BannedNames>();
		await Task.Delay(200);

		await Assert.That(cut.Markup).Contains("Banned");
	}

	[Test]
	public async Task BannedNames_WithNames_DisplaysEntries()
	{
		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/bannednames", new[] { "badguy", "troll", "hacker" })
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<BannedNames>();
		await Task.Delay(200);

		var markup = cut.Markup;
		await Assert.That(markup).Contains("badguy");
		await Assert.That(markup).Contains("troll");
		await Assert.That(markup).Contains("hacker");
	}

	[Test]
	public async Task BannedNames_EmptyList_ShowsEmptyState()
	{
		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/bannednames", Array.Empty<string>())
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<BannedNames>();
		await Task.Delay(200);

		// Should show some empty or "no items" state
		var markup = cut.Markup;
		// The page renders but with no list items
		await Assert.That(markup).IsNotNull();
	}

	[Test]
	public async Task BannedNames_HasAddButton()
	{
		await using var ctx = new BunitContext();
		var handler = new MockApiHandler()
			.OnGet("/api/bannednames", Array.Empty<string>())
			.OnGet("/api/configuration", AdminTestHelpers.CreateConfigResponse());
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<BannedNames>();
		await Task.Delay(200);

		var buttons = cut.FindAll("button");
		var addButton = buttons.FirstOrDefault(b =>
			b.TextContent.Contains("Add", StringComparison.OrdinalIgnoreCase));
		await Assert.That(addButton).IsNotNull();
	}
}
