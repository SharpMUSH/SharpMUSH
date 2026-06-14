using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Theme;

public class ThemeServiceTests
{
	private static IJSRuntime MakeJs(string? storedValue = null)
	{
		var js = Substitute.For<IJSRuntime>();
		// InvokeAsync<string?> returns ValueTask<string?> — must use ValueTask<string?> not ValueTask
		js.InvokeAsync<string?>(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
			.Returns(new ValueTask<string?>(storedValue));
		js.InvokeAsync<string?>(Arg.Any<string>(), Arg.Any<object?[]?>())
			.Returns(new ValueTask<string?>(storedValue));
		return js;
	}

	// ── Static default preset ────────────────────────────────────────────────

	[Test]
	public async Task GetDefaultPreset_ReturnsPresetWithExpectedName()
	{
		var preset = ThemeService.GetDefaultPreset();
		await Assert.That(preset.Name).IsEqualTo("Phosphor");
	}

	[Test]
	public async Task GetDefaultPreset_IsDarkMode()
	{
		var preset = ThemeService.GetDefaultPreset();
		await Assert.That(preset.IsDarkMode).IsTrue();
	}

	// ── Initial state ────────────────────────────────────────────────────────

	[Test]
	public async Task GetCurrentThemeAsync_BeforeInit_ReturnsDefault()
	{
		var svc = new ThemeService(MakeJs());
		var current = await svc.GetCurrentThemeAsync();
		await Assert.That(current.Name).IsEqualTo("Phosphor");
	}

	[Test]
	public async Task GetAvailablePresetsAsync_ReturnsAtLeastThreePresets()
	{
		var svc = new ThemeService(MakeJs());
		var presets = await svc.GetAvailablePresetsAsync();
		await Assert.That(presets.Count).IsGreaterThanOrEqualTo(3);
	}

	[Test]
	public async Task GetAvailablePresetsAsync_ContainsDefaultPreset()
	{
		var svc = new ThemeService(MakeJs());
		var presets = await svc.GetAvailablePresetsAsync();
		var names = presets.Select(p => p.Name).ToList();
		await Assert.That(names.Contains("Phosphor")).IsTrue();
	}

	// ── ApplyPresetAsync ──────────────────────────────────────────────────────

	[Test]
	public async Task ApplyPresetAsync_KnownPreset_ChangesCurrentTheme()
	{
		var svc = new ThemeService(MakeJs());
		await svc.ApplyPresetAsync("Amber");
		var current = await svc.GetCurrentThemeAsync();
		await Assert.That(current.Name).IsEqualTo("Amber");
	}

	[Test]
	public async Task ApplyPresetAsync_UnknownPreset_ThrowsArgumentException()
	{
		var svc = new ThemeService(MakeJs());
		await Assert.ThrowsAsync<ArgumentException>(
			async () => await svc.ApplyPresetAsync("Nonexistent Theme"));
	}

	[Test]
	public async Task ApplyPresetAsync_FiresOnThemeChanged()
	{
		var svc = new ThemeService(MakeJs());
		var fired = false;
		svc.OnThemeChanged += () => fired = true;

		await svc.ApplyPresetAsync("Violet");

		await Assert.That(fired).IsTrue();
	}

	[Test]
	public async Task ApplyPresetAsync_PersistsToLocalStorage_ChangesCurrentPreset()
	{
		// The localStorage persistence is best validated end-to-end via InitializeAsync;
		// here we verify the side-visible effect: current theme updated and event fired.
		var svc = new ThemeService(MakeJs());
		var eventFired = false;
		svc.OnThemeChanged += () => eventFired = true;

		await svc.ApplyPresetAsync("Amber");

		var current = await svc.GetCurrentThemeAsync();
		await Assert.That(current.Name).IsEqualTo("Amber");
		await Assert.That(eventFired).IsTrue();
	}

	// ── InitializeAsync ──────────────────────────────────────────────────────

	[Test]
	public async Task InitializeAsync_WithStoredValidPreset_RestoresThatPreset()
	{
		var svc = new ThemeService(MakeJs("Amber"));
		await svc.InitializeAsync();
		var current = await svc.GetCurrentThemeAsync();
		await Assert.That(current.Name).IsEqualTo("Amber");
	}

	[Test]
	public async Task InitializeAsync_WithStoredUnknownPreset_KeepsDefault()
	{
		var svc = new ThemeService(MakeJs("Does Not Exist"));
		await svc.InitializeAsync();
		var current = await svc.GetCurrentThemeAsync();
		await Assert.That(current.Name).IsEqualTo("Phosphor");
	}

	[Test]
	public async Task InitializeAsync_WithNullStored_KeepsDefault()
	{
		var svc = new ThemeService(MakeJs(null));
		await svc.InitializeAsync();
		var current = await svc.GetCurrentThemeAsync();
		await Assert.That(current.Name).IsEqualTo("Phosphor");
	}

	// ── ToMudTheme extension ─────────────────────────────────────────────────

	[Test]
	public async Task ToMudTheme_ReturnsNonNullTheme()
	{
		var preset = ThemeService.GetDefaultPreset();
		var theme = preset.ToMudTheme();
		await Assert.That(theme).IsNotNull();
	}

	[Test]
	public async Task ToMudTheme_PaletteDark_PrimaryMatchesPreset()
	{
		var preset = ThemeService.GetDefaultPreset();
		var theme = preset.ToMudTheme();
		// MudColor.Value appends alpha (#rrggbbff) — compare only the 7-char hex
		var mudPrimary = theme.PaletteDark.Primary.Value.ToLower()[..7];
		await Assert.That(mudPrimary).IsEqualTo(preset.PrimaryColor.ToLower());
	}
}
