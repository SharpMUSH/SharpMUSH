using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Component tests for <see cref="ThemeProvider"/>.
/// Extends BunitContext directly because this project does not reference SharpMUSH.Tests.
/// </summary>
public abstract class ThemeProviderTestBase : BunitContext
{
	protected ThemeProviderTestBase()
	{
		Services.AddMudServices();
		Services.AddLocalization();
		// Allow all JS interop calls (MudThemeProvider uses JS for system-preference detection)
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	protected static IThemeService MakeService(ThemePreset? preset = null)
	{
		preset ??= new ThemePreset(
			Name: "Default Dark",
			PrimaryColor: "#4caf50",
			SecondaryColor: "#81c784",
			TertiaryColor: "#a5d6a7",
			BackgroundColor: "#1a1a1a",
			SurfaceColor: "#242424",
			AppBarColor: "#1a1a1a",
			DrawerBackgroundColor: "#1e1e1e",
			IsDarkMode: true);

		var svc = Substitute.For<IThemeService>();
		svc.GetCurrentThemeAsync().Returns(Task.FromResult(preset));
		svc.GetAvailablePresetsAsync()
			.Returns(Task.FromResult<IReadOnlyList<ThemePreset>>([preset]));
		return svc;
	}
}

public class ThemeProviderRenderTests : ThemeProviderTestBase
{
	[TUnit.Core.Test]
	public async Task ThemeProvider_RendersChildContent()
	{
		var svc = MakeService();
		Services.AddSingleton(svc);

		var cut = Render<ThemeProvider>(p => p
			.AddChildContent("<span id='child'>hello</span>"));

		var span = cut.Find("#child");
		await Assert.That(span.TextContent).IsEqualTo("hello");
	}

	[TUnit.Core.Test]
	public async Task ThemeProvider_RendersMudThemeProvider()
	{
		var svc = MakeService();
		Services.AddSingleton(svc);

		var cut = Render<ThemeProvider>(p => p
			.AddChildContent("<span></span>"));

		// MudThemeProvider is rendered as a component — verify it is present in the tree
		var mudTheme = cut.FindComponent<MudThemeProvider>();
		await Assert.That(mudTheme.Instance is not null).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task ThemeProvider_CallsGetCurrentThemeAsync_OnInit()
	{
		var svc = MakeService();
		Services.AddSingleton(svc);

		_ = Render<ThemeProvider>(p => p
			.AddChildContent("<span></span>"));

		await svc.Received().GetCurrentThemeAsync();
	}
}

public class ThemeProviderEventTests : ThemeProviderTestBase
{
	[TUnit.Core.Test]
	public async Task ThemeProvider_SubscribesToOnThemeChanged_OnInit()
	{
		Action? capturedHandler = null;
		var svc = Substitute.For<IThemeService>();
		svc.GetCurrentThemeAsync().Returns(Task.FromResult(
			new ThemePreset("Default Dark", "#4caf50", "#81c784", "#a5d6a7",
				"#1a1a1a", "#242424", "#1a1a1a", "#1e1e1e", true)));

		svc.OnThemeChanged += Arg.Do<Action>(h => capturedHandler = h);
		Services.AddSingleton(svc);

		_ = Render<ThemeProvider>(p => p
			.AddChildContent("<span></span>"));

		await Assert.That(capturedHandler is not null).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task ThemeProvider_Dispose_UnsubscribesFromOnThemeChanged()
	{
		var svc = MakeService();
		Services.AddSingleton(svc);

		var cut = Render<ThemeProvider>(p => p
			.AddChildContent("<span></span>"));

		cut.Instance.Dispose();

		svc.Received().OnThemeChanged -= Arg.Any<Action>();
	}
}
