using Bunit;
using MudBlazor.Services;
using SharpMUSH.Client.Pages.Admin.Packages;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>An installed package's row: what it does first, the rest under Details, the rarer actions in its menu.</summary>
public class InstalledPackageRowTests : BunitContext
{
	public InstalledPackageRowTests()
	{
		Services.AddMudServices().AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static InstalledPackageDto Package(string? description) =>
		new(new InstalledPackageRecord("plus-help", "1.2.0", "bundled", "plus-help", "abc123", null, DateTimeOffset.UnixEpoch, 1), 4, 1, [], "Plus help")
		{
			Description = description,
		};

	[Test]
	public async Task TheDescriptionLeadsAndTheVersionWaitsUnderDetails()
	{
		var cut = Render<MudHarness>(p => p.AddChildContent<InstalledPackageRow>(r => r.Add(x => x.Package, Package("+help topics for players."))));

		await Assert.That(cut.Find(".pkg-row-name").TextContent).IsEqualTo("Plus help");
		await Assert.That(cut.Find(".pkg-row-desc").TextContent).IsEqualTo("+help topics for players.");
		await Assert.That(cut.Find(".pkg-row-head").TextContent).DoesNotContain("v1.2.0");
		await Assert.That(cut.Find("details.pkg-row-details").TextContent).Contains("v1.2.0");
		await Assert.That(cut.Find("details.pkg-row-details .pkg-row-id").TextContent).IsEqualTo("plus-help");
	}

	[Test]
	public async Task WithoutADescriptionNoEmptyLineIsDrawn()
	{
		var cut = Render<MudHarness>(p => p.AddChildContent<InstalledPackageRow>(r => r.Add(x => x.Package, Package(null))));

		await Assert.That(cut.FindAll(".pkg-row-desc")).IsEmpty();
	}

	[Test]
	public async Task ReadmeHistoryAndUninstallAreInTheRowMenu()
	{
		var cut = Render<MudHarness>(p => p.AddChildContent<InstalledPackageRow>(r => r.Add(x => x.Package, Package(null))));
		await Assert.That(cut.FindAll(".pkg-row-actions .mud-icon-button[aria-label^='PkgShowReadme']")).IsEmpty();

		await cut.Find("button[aria-label='NavRowMore(Plus help)']").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".kit-rowmenu-item--danger"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-rowmenu-item").Select(i => i.TextContent.Trim()))
			.IsEquivalentTo(["PkgShowReadme(Plus help)", "PkgRevisionHistory", "PkgUninstall"]);
	}
}
