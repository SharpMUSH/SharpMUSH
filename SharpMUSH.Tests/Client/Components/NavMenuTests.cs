using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Services;

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

		// The profile card is the account-panel trigger button, not a direct link. Account
		// management, character switching, and logout live inside the popover it opens — so
		// /account is only reachable once the panel is open, not from the closed default render.
		var card = cut.Find("button.phosphor-profile-card");
		await Assert.That(card.GetAttribute("aria-haspopup")).IsEqualTo("true");
		// The anonymous "Sign in" card (the NotAuthorized branch) must not be the one rendered.
		await Assert.That(cut.FindAll("a.phosphor-profile-card[href='/login']").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("a[href='/account']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Expanded_HomeLinkText_IsCorrect()
	{
		var cut = RenderExpanded();
		await Assert.That(cut.Find("a.phosphor-nav-link[href='/']").TextContent).Contains("Home");
	}

	[Test]
	public async Task SoftcodeLink_Hidden_WhenNotAuthorized()
	{
		// The softcode editor is gated behind the softcode.use policy; an anonymous/guest
		// visitor (RenderExpanded is unauthenticated) must not see the nav item.
		var cut = RenderExpanded();
		await Assert.That(cut.FindAll("a[href='/softcode']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task SoftcodeLink_Shown_WhenAuthorizedWithSoftcodeUse()
	{
		var auth = this.AddAuthorization();
		auth.SetAuthorized("player");
		auth.SetPolicies("softcode.use");

		var cut = Render<NavMenu>(parameters => parameters.Add(p => p.IsCollapsed, false));

		var softcodeLink = cut.Find("a[href='/softcode']");
		await Assert.That(softcodeLink.TextContent).Contains("Softcode");
	}

	[Test]
	public async Task Expanded_ShowsBrandTitle()
	{
		var cut = RenderExpanded();
		await Assert.That(cut.FindAll(".phosphor-brand").Count).IsEqualTo(1);
	}

	[Test]
	public async Task Expanded_Brand_ShowsConfiguredGameName()
	{
		var serverInfo = Substitute.For<ServerInfoService>(Substitute.For<IHttpClientFactory>());
		serverInfo.GuestLoginsEnabledAsync().Returns(Task.FromResult(true));
		serverInfo.GameNameAsync().Returns(Task.FromResult("My Grand Game"));
		Services.AddSingleton(serverInfo);

		var cut = RenderExpanded();

		await Assert.That(cut.Find(".phosphor-brand").TextContent.Trim()).IsEqualTo("My Grand Game");
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
