using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The standalone /register form was folded into Login.razor's Register tab; the /register route
/// now survives only as a thin redirect to the single chromeless auth surface, so existing links
/// and bookmarks keep working.
/// </summary>
public class RegisterRedirectTests : BunitContext
{
	[TUnit.Core.Test]
	public async Task Register_RedirectsToLoginRegisterTab()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		Render<SharpMUSH.Client.Pages.Register>();

		var nav = Services.GetRequiredService<NavigationManager>();
		await Assert.That(nav.Uri.EndsWith("/login?tab=register")).IsTrue();
	}
}
