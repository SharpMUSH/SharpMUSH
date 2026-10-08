using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Component tests for <see cref="ThemeProvider"/>: it writes the current theme's tokens as a stylesheet and hands
/// MudBlazor the matching palette, and follows the service when the theme changes.
/// Extends BunitContext directly because this project does not reference SharpMUSH.Tests.
/// </summary>
public class ThemeProviderTests : BunitContext
{
	private sealed class Holder
	{
		public ResolvedTheme Theme { get; set; } = ThemeResolver.Resolve(BuiltInThemes.Phosphor);
	}

	private readonly Holder _current = new();
	private Action? _changed;
	private readonly IThemeService _service = Substitute.For<IThemeService>();

	public ThemeProviderTests()
	{
		Services.AddMudServices();
		Services.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
		_service.Current.Returns(_ => _current.Theme);
		_service.OnThemeChanged += Arg.Do<Action>(h => _changed = h);
		Services.AddSingleton(_service);
	}

	[TUnit.Core.Test]
	public async Task RendersChildContent()
	{
		var cut = Render<ThemeProvider>(p => p.AddChildContent("<span id='child'>hello</span>"));

		await Assert.That(cut.Find("#child").TextContent).IsEqualTo("hello");
	}

	[TUnit.Core.Test]
	public async Task WritesTheThemesTokensAsAStylesheet()
	{
		var cut = Render<ThemeProvider>(p => p.AddChildContent("<span></span>"));

		var css = cut.Find("style#sharp-theme").TextContent;
		await Assert.That(css).StartsWith(":root{color-scheme:dark;");
		await Assert.That(css).Contains("--accent:#00f5b7;");
		await Assert.That(cut.FindComponent<MudThemeProvider>().Instance.GetState(x => x.IsDarkMode)).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task FollowsTheServiceToALightThemeWithACharactersAccent()
	{
		var cut = Render<ThemeProvider>(p => p.AddChildContent("<span></span>"));

		_current.Theme = ThemeResolver.Resolve(BuiltInThemes.Daylight, "#5aa9ff");
		_changed!.Invoke();

		cut.WaitForState(() => cut.Find("style#sharp-theme").TextContent.Contains("color-scheme:light"));
		var mud = cut.FindComponent<MudThemeProvider>().Instance;
		await Assert.That(mud.GetState(x => x.IsDarkMode)).IsFalse();
		await Assert.That(mud.Theme!.PaletteLight.Primary).IsEqualTo(new MudBlazor.Utilities.MudColor(_current.Theme.Accent));
	}

	[TUnit.Core.Test]
	public async Task PutsTheThemesPartsOnThePageAndFollowsAChange()
	{
		var cut = Render<ThemeProvider>(p => p.AddChildContent("<span></span>"));

		_current.Theme = ThemeResolver.Resolve(BuiltInThemes.Fantasy);
		_changed!.Invoke();

		cut.WaitForAssertion(() =>
		{
			var applied = JSInterop.Invocations["sharpmushLayout.applyThemeParts"];
			if (applied.Count != 2) throw new InvalidOperationException($"{applied.Count} calls");
		});
		var parts = JSInterop.Invocations["sharpmushLayout.applyThemeParts"].Select(i => (IReadOnlyDictionary<string, string>)i.Arguments[0]!).ToList();
		await Assert.That(parts[0][ThemeStyles.Texture]).IsEqualTo("none");
		await Assert.That(parts[1][ThemeStyles.Texture]).IsEqualTo("parchment");
		await Assert.That(parts[1][ThemeStyles.Scheme]).IsEqualTo("light");
	}

	[TUnit.Core.Test]
	public async Task StopsFollowingTheServiceWhenDisposed()
	{
		var cut = Render<ThemeProvider>(p => p.AddChildContent("<span></span>"));

		cut.Instance.Dispose();

		_service.Received().OnThemeChanged -= Arg.Any<Action>();
		await Task.CompletedTask;
	}
}
