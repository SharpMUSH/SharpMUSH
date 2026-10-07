using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>
/// README §3 page sidebar: a section's sidebar goes into the shell's page-sidebar slot, with a
/// collapse button at its top right; collapsed, the sidebar renders its strip; the state is
/// remembered per section in localStorage (Q3), so the wiki's choice leaves the configuration's alone.
/// </summary>
public class SectionShellTests : BunitContext
{
	public SectionShellTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		Services.AddSingleton<SidebarCollapseService>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>The shell's outlet beside one section shell, as MainLayout and a section layout compose them.</summary>
	private sealed class Host : ComponentBase
	{
		[Parameter] public string Key { get; set; } = "wiki";
		[Parameter] public bool Hidden { get; set; }

		protected override void BuildRenderTree(RenderTreeBuilder b)
		{
			b.OpenElement(0, "aside");
			b.AddAttribute(1, "class", "slot");
			b.OpenComponent<SectionOutlet>(2);
			b.AddAttribute(3, nameof(SectionOutlet.SectionName), PageSidebarSlot.Name);
			b.CloseComponent();
			b.CloseElement();
			b.OpenComponent<SectionShell>(4);
			b.AddAttribute(5, nameof(SectionShell.Key), Key);
			b.AddAttribute(6, nameof(SectionShell.NavLabel), "Wiki");
			b.AddAttribute(9, nameof(SectionShell.Hidden), Hidden);
			b.AddAttribute(7, nameof(SectionShell.Nav), (RenderFragment<bool>)(collapsed => nb =>
			{
				nb.OpenElement(0, "div");
				nb.AddAttribute(1, "class", collapsed ? "side side--collapsed" : "side");
				nb.CloseElement();
			}));
			b.AddAttribute(8, nameof(SectionShell.ChildContent), (RenderFragment)(cb =>
			{
				cb.OpenElement(0, "p");
				cb.AddAttribute(1, "id", "body");
				cb.CloseElement();
			}));
			b.CloseComponent();
		}
	}

	[Test]
	public async Task TheSidebarGoesIntoTheShellSlot_AndTheBodyStays()
	{
		var cut = Render<Host>();
		await Assert.That(cut.Find(".slot .kit-pagebar .side")).IsNotNull();
		await Assert.That(cut.Find(".kit-section-body #body")).IsNotNull();
		await Assert.That(cut.FindAll(".kit-section-body .kit-pagebar").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Hidden_TakesTheSidebarOutOfTheSlot_AndKeepsTheBodyMounted()
	{
		// Play's focus mode steps the page sidebar away; the body (the terminal) must not remount.
		var cut = Render<Host>();
		cut.Render(p => p.Add(x => x.Hidden, true));
		await Assert.That(cut.FindAll(".slot .kit-pagebar").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-section-body #body")).IsNotNull();
		cut.Render(p => p.Add(x => x.Hidden, false));
		await Assert.That(cut.FindAll(".slot .kit-pagebar").Count).IsEqualTo(1);
	}

	[Test]
	public async Task TheToggle_CollapsesToTheStrip_AndSaysSo()
	{
		var cut = Render<Host>();
		var toggle = cut.Find(".slot button.kit-pagebar-toggle");
		await Assert.That(toggle.GetAttribute("aria-expanded")).IsEqualTo("true");

		await toggle.ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".slot .kit-pagebar--collapsed .side--collapsed"));
		await Assert.That(cut.Find(".slot button.kit-pagebar-toggle").GetAttribute("aria-expanded")).IsEqualTo("false");
		var saved = JSInterop.VerifyInvoke("localStorage.setItem");
		await Assert.That(saved.Arguments[0]).IsEqualTo("sharpmush.sidebar.wiki");
		await Assert.That(saved.Arguments[1]).IsEqualTo("1");
	}

	[Test]
	public async Task ARememberedCollapse_IsRestored()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "sharpmush.sidebar.wiki").SetResult("1");
		var wiki = Render<Host>(p => p.Add(x => x.Key, "wiki"));
		wiki.WaitForAssertion(() => wiki.Find(".kit-pagebar--collapsed"));
		await Assert.That(wiki.Find("button.kit-pagebar-toggle").GetAttribute("aria-expanded")).IsEqualTo("false");
	}

	[Test]
	public async Task AnotherSectionsCollapse_IsNotThisOnes()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "sharpmush.sidebar.wiki").SetResult("1");
		JSInterop.Setup<string?>("localStorage.getItem", "sharpmush.sidebar.config").SetResult(null);
		var config = Render<Host>(p => p.Add(x => x.Key, "config"));
		await Assert.That(config.FindAll(".kit-pagebar--collapsed").Count).IsEqualTo(0);
	}
}
