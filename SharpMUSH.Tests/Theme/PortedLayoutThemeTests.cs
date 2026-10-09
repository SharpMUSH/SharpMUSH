using MarkupString;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.Theme;

/// <summary>SharpMUSH's own layout themes: the portal's themes for the terminal, and the default <c>sharpmush</c>.</summary>
public class PortedLayoutThemeTests
{
	/// <summary>The portal's genre themes share MarkupString's genre ids, which keep MarkupString's colours and looks.</summary>
	private static readonly string[] Genres =
		["fantasy", "historical", "horror", "modern", "mystery", "romance", "science-fiction", "spiritual"];

	public static IEnumerable<string> OwnNames() => LayoutThemes.Own.Select(theme => theme.Name);

	[Test]
	[MethodDataSource(nameof(OwnNames))]
	public async Task EachTheme_StandsOutFromItsBackground(string name)
		=> await Assert.That(LayoutThemes.OwnPreset(name)!.Check()).IsEmpty();

	[Test]
	public async Task EachPortalTheme_HasATerminalTheme_ForTheSameBackground()
	{
		foreach (var portal in BuiltInThemes.All.Where(theme => !Genres.Contains(theme.Id)))
		{
			var terminal = LayoutThemes.OwnPreset(portal.Id);
			await Assert.That(terminal).IsNotNull().Because(portal.Id);
			await Assert.That(terminal!.Mode).IsEqualTo(portal.Dark ? ThemeMode.Dark : ThemeMode.Light).Because(portal.Id);
		}
	}

	[Test]
	public async Task NoThemeName_IsAlsoAMarkupStringPreset()
		=> await Assert.That(LayoutThemes.Own.Where(theme => ThemePalette.Preset(theme.Name) is not null)).IsEmpty();

	[Test]
	public async Task TheGame_IsDrawnInSharpMUSHUnlessSetOtherwise()
	{
		await Assert.That(SharpMUSHOptions.Default().Cosmetic.LayoutTheme).IsEqualTo(LayoutThemes.Default);
		await Assert.That(LayoutThemes.SharpMUSH is { Mode: ThemeMode.Dark }).IsTrue();
		await Assert.That(LayoutThemes.SharpMUSH.BackgroundColor.ToHex()).IsEqualTo("#000000");
		await Assert.That(LayoutThemes.SharpMUSH[ThemeRole.Secondary]!.Value.Rgb!.Value.ToHex()).IsEqualTo(BuiltInThemes.Phosphor.Tokens[ThemeTokens.Accent]);
	}
}
