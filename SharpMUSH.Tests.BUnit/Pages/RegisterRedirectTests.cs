using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

file sealed class RegisterStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

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
		Services.AddSingleton<IStringLocalizer<SharedResource>, RegisterStubLocalizer<SharedResource>>();

		Render<SharpMUSH.Client.Pages.Register>();

		var nav = Services.GetRequiredService<NavigationManager>();
		await Assert.That(nav.Uri.EndsWith("/login?tab=register")).IsTrue();
	}
}
