using Bunit;
using SharpMUSH.Client.Pages.Admin.Config;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Tests for the ConfigIndex page — the admin configuration landing page.
/// </summary>
public class ConfigIndexTests
{
	[Test]
	public async Task ConfigIndex_InitialLoad_ShowsCategoryCards()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigIndex>();

		var markup = cut.Markup;
		// The config index shows grouped category cards
		await Assert.That(markup).Contains("Server");
	}

	[Test]
	public async Task ConfigIndex_ShowsSecuritySection()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigIndex>();

		await Assert.That(cut.Markup).Contains("Security");
	}

	[Test]
	public async Task ConfigIndex_ShowsPerformanceSection()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigIndex>();

		await Assert.That(cut.Markup).Contains("Performance");
	}

	[Test]
	public async Task ConfigIndex_HasExportAction()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigIndex>();

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Export");
	}

	[Test]
	public async Task ConfigIndex_HasImportAction()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ConfigIndex>();

		await Assert.That(cut.Markup).Contains("Import");
	}
}
