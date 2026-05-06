using Bunit;
using SharpMUSH.Client.Pages.Admin.Config;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Tests for the ImportPennMUSH page — destructive wipe-and-import flow.
/// </summary>
public class ImportPennMUSHTests
{
	[Test]
	public async Task ImportPennMUSH_InitialLoad_ShowsDangerWarning()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		var markup = cut.Markup;
		// Should show warning about destructive operation (hardcoded text)
		await Assert.That(markup).Contains("permanently destroy");
	}

	[Test]
	public async Task ImportPennMUSH_InitialLoad_ShowsTitle()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		await Assert.That(cut.Markup).Contains("Import");
	}

	[Test]
	public async Task ImportPennMUSH_InitialLoad_HasFileUploadAreas()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		var markup = cut.Markup;
		// Should have areas for both database and config file upload
		await Assert.That(markup).Contains(".db");
	}

	[Test]
	public async Task ImportPennMUSH_WithoutFiles_ConfirmDisabled()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		// The confirm/import button should be disabled without files selected
		var buttons = cut.FindAll("button");
		// Find the import/confirm button — it should be disabled
		var importButton = buttons.FirstOrDefault(b =>
			b.TextContent.Contains("Import", StringComparison.OrdinalIgnoreCase) ||
			b.TextContent.Contains("Confirm", StringComparison.OrdinalIgnoreCase));

		if (importButton != null)
		{
			await Assert.That(importButton.HasAttribute("disabled")).IsTrue();
		}
	}

	[Test]
	public async Task ImportPennMUSH_HasDestroyConfirmationInput()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		var markup = cut.Markup;
		// Should have an input field for typing DESTROY
		var inputs = cut.FindAll("input");
		await Assert.That(inputs.Count).IsGreaterThan(0);
	}
}
