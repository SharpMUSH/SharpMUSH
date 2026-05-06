using Bunit;
using SharpMUSH.Client.Components;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Tests for the ConfigNavDrawer component — admin config sidebar navigation.
/// </summary>
public class ConfigNavDrawerTests
{
	[Test]
	public async Task ConfigNavDrawer_ShowsServerGroup()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigNavDrawer>();

		await Assert.That(cut.Markup).Contains("Server");
	}

	[Test]
	public async Task ConfigNavDrawer_ShowsSecurityGroup()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigNavDrawer>();

		await Assert.That(cut.Markup).Contains("Security");
	}

	[Test]
	public async Task ConfigNavDrawer_ShowsDangerZone()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigNavDrawer>();

		await Assert.That(cut.Markup).Contains("Danger");
	}

	[Test]
	public async Task ConfigNavDrawer_HasSearchField()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigNavDrawer>();

		var inputs = cut.FindAll("input");
		await Assert.That(inputs.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task ConfigNavDrawer_ContainsSitelockLink()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigNavDrawer>();

		await Assert.That(cut.Markup).Contains("sitelock");
	}

	[Test]
	public async Task ConfigNavDrawer_ContainsBannedNamesLink()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigNavDrawer>();

		await Assert.That(cut.Markup).Contains("bannednames");
	}
}
