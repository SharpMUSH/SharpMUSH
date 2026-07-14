using Bunit;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the redesigned (Phosphor) NavMenu sidebar. It defaults to collapsed (icon-only);
/// expanded mode is opt-in via <c>IsCollapsed="false"</c>. Core links always render; admin
/// destinations are permission-gated and only appear for an authorized user.
/// </summary>
public class NavMenuTests : MudBlazorTestContext
{
	private IRenderedComponent<NavMenu> RenderCollapsed()
	{
		this.AddAuthorization(); // AuthorizeView requires an authentication state; unauthenticated by default
		return Render<NavMenu>();
	}

	private IRenderedComponent<NavMenu> RenderExpanded()
	{
		this.AddAuthorization();
		return Render<NavMenu>(parameters => parameters.Add(p => p.IsCollapsed, false));
	}

	[Test]
	public async Task Collapsed_RendersCoreLinksWithoutText()
	{
		var cut = RenderCollapsed();

		// Core links are present but render as icons only (no text label) when collapsed.
		// (Scope the Home link to the nav list — the logo also points at "/".)
		await Assert.That(cut.Find("a.phosphor-nav-link[href='/']").TextContent.Trim()).IsEmpty();
		await Assert.That(cut.Find("a[href='/softcode']").TextContent.Trim()).IsEmpty();
	}

	[Test]
	public async Task Collapsed_HidesBrandTitle()
	{
		var cut = RenderCollapsed();

		// The brand/title block in the logo only renders when expanded.
		await Assert.That(cut.FindAll(".phosphor-brand").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Expanded_RendersCoreNavigationLinks()
	{
		var cut = RenderExpanded();

		// The always-visible (ungated, non-character-scoped) destinations.
		// (The Home nav link is distinct from the logo, which also links to "/".)
		await Assert.That(cut.FindAll("a.phosphor-nav-link[href='/']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("a[href='/scenes']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("a[href='/wiki']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("a[href='/characters']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("a[href='/softcode']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("a[href='/help']").Count).IsEqualTo(1);

		// The profile card (and its /account link) is auth-gated: anonymous visitors
		// browse without an account, so no profile card renders for them.
		await Assert.That(cut.FindAll("a[href='/account']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ProfileCard_Shown_WhenAuthenticated()
	{
		var auth = this.AddAuthorization();
		auth.SetAuthorized("admin");

		var cut = Render<NavMenu>(parameters => parameters.Add(p => p.IsCollapsed, false));

		await Assert.That(cut.FindAll("a[href='/account']").Count).IsEqualTo(1);
	}

	[Test]
	public async Task Expanded_HomeLinkText_IsCorrect()
	{
		var cut = RenderExpanded();
		await Assert.That(cut.Find("a.phosphor-nav-link[href='/']").TextContent).Contains("Home");
	}

	[Test]
	public async Task Expanded_SoftcodeLinkText_IsCorrect()
	{
		var cut = RenderExpanded();
		await Assert.That(cut.Find("a[href='/softcode']").TextContent).Contains("Softcode");
	}

	[Test]
	public async Task Expanded_ShowsBrandTitle()
	{
		var cut = RenderExpanded();
		await Assert.That(cut.FindAll(".phosphor-brand").Count).IsEqualTo(1);
	}

	[Test]
	public async Task AdminLinks_Hidden_WhenNotAuthorized()
	{
		// RenderExpanded adds an (unauthenticated) authorization context, so policy-gated
		// admin destinations must not render.
		var cut = RenderExpanded();

		await Assert.That(cut.FindAll("a[href='/admin/config']").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("a[href='/admin/roles']").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("a[href='/admin/wiki']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ConfigLink_Shown_WhenAuthorizedWithConfigAdmin()
	{
		var auth = this.AddAuthorization();
		auth.SetAuthorized("admin");
		auth.SetPolicies("config.admin");

		var cut = Render<NavMenu>(parameters => parameters.Add(p => p.IsCollapsed, false));

		var configLink = cut.Find("a[href='/admin/config']");
		await Assert.That(configLink.TextContent).Contains("Config");
	}
}
