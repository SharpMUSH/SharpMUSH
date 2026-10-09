using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Escape closes the language menu (PopoverMenu), and the button is the only control around its icon: MudMenu
/// wrapped it in a div with role="button" and a tab stop of its own.
/// </summary>
public class LanguagePickerTests : TrackingBunitContext
{
	public LanguagePickerTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task Escape_ClosesTheOpenMenu()
	{
		var cut = Render<MudHarness>(p => p.AddChildContent<LanguagePicker>());
		var button = cut.Find(".kit-popmenu > button");
		await Assert.That(cut.FindAll("[role='button']").Count).IsEqualTo(0);
		await Assert.That(button.GetAttribute("aria-expanded")).IsEqualTo("false");
		await button.ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".mud-popover-open"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-popmenu > button").GetAttribute("aria-expanded")).IsEqualTo("true");
		await Assert.That(cut.FindAll(".lang-item[aria-current='true']").Count).IsEqualTo(1);

		await cut.Find(".kit-popmenu > button").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mud-popover-open").Count > 0) throw new InvalidOperationException("menu still open");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".mud-popover-open").Count).IsEqualTo(0);
	}
}
