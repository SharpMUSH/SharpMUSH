using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Layout;
using SharpMUSH.Tests.BUnit.Components.Characters;
using SharpMUSH.Tests.BUnit.Components.Scenes;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>ScenesLayout puts the Scenes sidebar in the shell's page-sidebar slot and the page beside it.</summary>
public class ScenesLayoutTests : TrackingBunitContext
{
	[Test]
	public async Task HostsTheSidebar_InTheSlot_AndTheBody()
	{
		var (api, _, _) = CharactersApiFake.Install(this);
		api.Extra[SceneJson.Recent] = SceneJson.List(SceneJson.Scene("S1", "Salt Market at Dusk"));
		api.Extra[SceneJson.Active] = SceneJson.List(SceneJson.Scene("S1", "Salt Market at Dusk"));
		Services.AddSingleton(CharactersApiFake.Anonymous(this));
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/scenes");

		var cut = Render<PageSidebarHost>(h => h.AddChildContent<ScenesLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<p id=\"body\">x</p>"))));

		cut.WaitForAssertion(() => cut.Find(".test-pagebar .scenes-side-live a.kit-row"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".test-pagebar .kit-pagebar.scenes-shell")).IsNotNull().Because("the sidebar renders in the shell's page-sidebar slot");
		await Assert.That(cut.Find(".test-pagebar .kit-pagebar").GetAttribute("data-section")).IsEqualTo("scenes");
		await Assert.That(cut.Find(".kit-section-body.scenes-shell #body")).IsNotNull();
	}
}
