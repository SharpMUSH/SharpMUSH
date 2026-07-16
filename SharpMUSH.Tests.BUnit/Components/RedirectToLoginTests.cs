using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Regression coverage for RedirectToLogin's OIDC-era dead end: it used to call
/// <c>Navigation.NavigateToLogin("authentication/login")</c>, a route that no longer exists now
/// that OIDC wiring has been removed. App.razor's AuthorizeRouteView renders this component
/// whenever an anonymous visitor hits an <c>[Authorize]</c> page, so a broken redirect here
/// silently stranded them with no way back into the portal. It must now land on the real
/// /login page, carrying a returnUrl so a successful sign-in can send the visitor back to
/// wherever they were headed.
/// </summary>
public class RedirectToLoginTests : BunitContext
{
	[TUnit.Core.Test]
	public async Task NavigatesToLoginWithReturnUrl_ForCurrentPath()
	{
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/admin/config");

		Render<RedirectToLogin>();

		var expected = new Uri(new Uri(nav.BaseUri), $"/login?returnUrl={Uri.EscapeDataString("/admin/config")}");
		await Assert.That(nav.Uri).IsEqualTo(expected.ToString());
	}

	[TUnit.Core.Test]
	public async Task NavigatesToLoginWithReturnUrl_PreservesQueryString()
	{
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/wiki?q=test+topic");

		Render<RedirectToLogin>();

		var expected = new Uri(new Uri(nav.BaseUri), $"/login?returnUrl={Uri.EscapeDataString("/wiki?q=test+topic")}");
		await Assert.That(nav.Uri).IsEqualTo(expected.ToString());
	}
}
