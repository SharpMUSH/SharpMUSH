using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// README §3's frame, as MainLayout and shell.css compose it: the rail on a desktop and the drawer
/// on touch chrome; the page-sidebar slot; no desktop top bar (Q1) but a slim header on touch chrome;
/// the global TopBar zone as a strip only when it has placements.
/// </summary>
public class ShellFrameTests
{
	private static string Layout() => File.ReadAllText(Path.Join(ClientSource.RazorRoot, "Layout", "MainLayout.razor"));

	private static string Shell() => File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));

	/// <summary>The body of the first <c>@media</c> block whose condition starts with <paramref name="condition"/>.</summary>
	private static string MediaBlock(string css, string condition)
	{
		var start = css.IndexOf("\n@media " + condition, StringComparison.Ordinal);
		if (start < 0) return string.Empty;
		var open = css.IndexOf('{', start);
		var depth = 0;
		for (var i = open; i < css.Length; i++)
		{
			if (css[i] == '{') depth++;
			else if (css[i] == '}' && --depth == 0) return css[open..i];
		}
		return string.Empty;
	}

	[Test]
	public async Task TheLayout_HostsTheRail_TheDrawer_AndThePageSidebarSlot()
	{
		var layout = Layout();
		await Assert.That(layout).Contains("<NavRail ");
		await Assert.That(layout).Contains("<NavMenu ");
		await Assert.That(layout).Contains("<SectionOutlet SectionName=\"@PageSidebarSlot.Name\" />");
		await Assert.That(layout).DoesNotContain("PageTitle()").Because("Q1: the page sidebar and banner name the page");
		await Assert.That(layout).Contains("phosphor-topzone").Because("Q1: the global TopBar zone is a strip above main");
	}

	[Test]
	public async Task TheTopBarZone_RendersOnlyWhenItHasPlacements()
	{
		var layout = Layout();
		var zone = Regex.Match(layout, @"@if \((?<cond>[^\n]*WidgetZone\.TopBar[^\n]*)\)\s*\{\s*<div class=""phosphor-topzone"">");
		await Assert.That(zone.Success).IsTrue();
		await Assert.That(zone.Groups["cond"].Value).Contains("Count > 0");
	}

	[Test]
	public async Task ADesktop_ShowsTheRail_AndHidesTheDrawerAndHeader()
	{
		var shell = Shell();
		var desktop = MediaBlock(shell, "(min-width: 761px) and (pointer: fine)");
		await Assert.That(desktop).Contains(".phosphor-sidebar");
		await Assert.That(desktop).Contains(".phosphor-topbar");
		await Assert.That(Regex.IsMatch(desktop, @"\.phosphor-topbar\s*\{[^}]*display:\s*none")).IsTrue();
	}

	[Test]
	public async Task TouchChrome_HidesTheRail()
	{
		var touch = MediaBlock(Shell(), "(max-width: 760px), (pointer: coarse)");
		await Assert.That(Regex.IsMatch(touch, @"\.phosphor-rail\s*\{[^}]*display:\s*none")).IsTrue();
	}

	[Test]
	public async Task TheSectionButton_ShowsOnlyWhereThereIsASectionSidebar()
		=> await Assert.That(Shell()).Contains(".phosphor-shell:not(:has(.kit-pagebar)) .phosphor-pagebar-btn");
}
