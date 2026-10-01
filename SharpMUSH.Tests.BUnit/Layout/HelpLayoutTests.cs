using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Pages;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>HelpLayout puts the Help sidebar in the shell's page-sidebar slot and the page beside it.</summary>
public class HelpLayoutTests : TrackingBunitContext
{
	[Test]
	public async Task HostsTheSidebar_InTheSlot_AndTheBody()
	{
		HelpApi.Install(this, isStaff: false, HelpPageTests.Corpora);
		Services.AddLocalization().AddSingleton<SidebarCollapseService>();
		AddAuthorization();
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/help");

		var cut = Render<PageSidebarHost>(h => h.AddChildContent<HelpLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<p id=\"body\">x</p>"))));

		cut.WaitForAssertion(() => cut.Find(".test-pagebar .help-side-browse a.kit-row"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".test-pagebar .kit-pagebar.help-shell")).IsNotNull().Because("the sidebar renders in the shell's page-sidebar slot");
		await Assert.That(cut.Find(".test-pagebar .kit-pagebar").GetAttribute("data-section")).IsEqualTo("help");
		await Assert.That(cut.Find(".kit-section-body.help-shell #body")).IsNotNull();
	}
}
