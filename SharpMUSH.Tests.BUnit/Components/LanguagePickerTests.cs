using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Escape closes the language menu. MudMenu leaves it open, so a keyboard user had to tab away or click
/// elsewhere (the Play terminal-settings menu has the same handling).
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
		await cut.Find(".mud-menu button").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".mud-popover-open"), TimeSpan.FromSeconds(5));

		await cut.Find(".mud-menu button").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mud-popover-open").Count > 0) throw new InvalidOperationException("menu still open");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".mud-popover-open").Count).IsEqualTo(0);
	}
}
