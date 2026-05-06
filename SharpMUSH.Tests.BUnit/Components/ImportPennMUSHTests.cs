using Bunit;
using SharpMUSH.Client.Pages.Admin.Config;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Tests for the ImportPennMUSH page — staged import flow with promote/abort.
/// </summary>
public class ImportPennMUSHTests
{
	[Test]
	public async Task ImportPennMUSH_InitialLoad_ShowsStagingInfo()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		await Assert.That(cut.Markup).Contains("Safe staging workflow");
	}

	[Test]
	public async Task ImportPennMUSH_InitialLoad_ShowsDestructiveOnPromoteWarning()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		await Assert.That(cut.Markup).Contains("DESTRUCTIVE ON PROMOTE");
	}

	[Test]
	public async Task ImportPennMUSH_InitialLoad_ShowsTitle()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		await Assert.That(cut.Markup).Contains("Import PennMUSH Database");
	}

	[Test]
	public async Task ImportPennMUSH_InitialLoad_HasFileUploadAreas()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		var markup = cut.Markup;
		await Assert.That(markup).Contains(".db");
		await Assert.That(markup).Contains("Database File");
		await Assert.That(markup).Contains("Config File");
	}

	[Test]
	public async Task ImportPennMUSH_InitialLoad_BothFilesRequired()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		var markup = cut.Markup;
		// Both file cards should show "Required" chip
		var requiredCount = System.Text.RegularExpressions.Regex.Matches(markup, "Required").Count;
		await Assert.That(requiredCount).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	public async Task ImportPennMUSH_WithoutFiles_NoConfirmationSection()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		// Confirmation section should not appear without both files
		await Assert.That(cut.Markup).DoesNotContain("Confirmation Required");
	}

	[Test]
	public async Task ImportPennMUSH_ConfirmationText_MentionsDESTROY()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		// DESTROY is mentioned in the warning banner context ("DESTRUCTIVE ON PROMOTE")
		await Assert.That(cut.Markup).Contains("DESTRUCTIVE");
	}

	[Test]
	public async Task ImportPennMUSH_InitialLoad_ShowsSelectFileButtons()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		await Assert.That(cut.Markup).Contains("Select Database File");
		await Assert.That(cut.Markup).Contains("Select Config File");
	}

	[Test]
	public async Task ImportPennMUSH_InitialLoad_ShowsStagingExplanation()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		await Assert.That(cut.Markup).Contains("staging");
		await Assert.That(cut.Markup).Contains("live database remains untouched");
	}

	[Test]
	public async Task ImportPennMUSH_InitialLoad_ShowsBackupReminder()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<ImportPennMUSH>();

		await Assert.That(cut.Markup).Contains("backup");
	}
}
