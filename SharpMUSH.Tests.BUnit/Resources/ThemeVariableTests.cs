using System.Text.RegularExpressions;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// The portal paints with the theme's custom properties, so a theme (and a theme's own stylesheet) reaches every
/// page. A colour written into a component's CSS stays the same on every theme: dark text on a light theme's
/// surface, or the reverse. Colours live in <c>tokens.css</c>, which a theme overrides.
/// </summary>
public partial class ThemeVariableTests
{
	private static IEnumerable<string> ComponentCss() =>
		Directory.EnumerateFiles(ClientSource.RazorRoot, "*.css", SearchOption.AllDirectories)
			.Concat(Directory.EnumerateFiles(ClientSource.CssRoot, "*.css").Where(f => Path.GetFileName(f) != "tokens.css"))
			.Concat(Directory.EnumerateFiles(ClientSource.ThemePartsRoot, "*.css"));

	[Test]
	public async Task NoComponentStylesheetWritesAColourOfItsOwn()
	{
		var offenders = new List<string>();
		foreach (var file in ComponentCss())
		{
			var lines = File.ReadAllLines(file);
			for (var i = 0; i < lines.Length; i++)
			{
				// Comments may name colours; neutral black and white shades are depth, not colour.
				var line = Comment().Replace(lines[i], "");
				foreach (Match m in Literal().Matches(line))
				{
					// A fallback inside var() only shows where the property is unset (MarkupString's own CSS).
					if (Fallback().IsMatch(line[..m.Index]))
					{
						continue;
					}

					offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Value}");
				}
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because("a colour belongs in tokens.css as a custom property a theme can set; see docs/guides/portal-themes.md");
	}

	[Test]
	public async Task EveryStylesheetHookNamesAClassThePortalRenders()
	{
		var sources = string.Join("\n", Directory.EnumerateFiles(ClientSource.RazorRoot, "*.*", SearchOption.AllDirectories)
			.Concat(Directory.EnumerateFiles(ClientSource.CssRoot, "*.css"))
			.Select(File.ReadAllText));

		foreach (var hook in ThemeStylesheet.Hooks.Where(h => !h.Selector.StartsWith(".mud-", StringComparison.Ordinal)))
		{
			var name = hook.Selector.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
			await Assert.That(Regex.IsMatch(sources, $@"(?<![\w-]){Regex.Escape(name)}(?![\w-])")).IsTrue()
				.Because($"the starter stylesheet offers {hook.Selector} for {hook.What}");
		}
	}

	[Test]
	public async Task TokensCssDeclaresEveryDerivedColour()
	{
		var tokens = File.ReadAllText(Path.Join(ClientSource.CssRoot, "tokens.css"));
		foreach (var (name, hex) in ThemeResolver.StatusColors.Concat(ThemeResolver.SyntaxColors).Concat(ThemeResolver.HueColors))
		{
			await Assert.That(tokens).Contains($"--{name}: {hex};");
		}
	}

	[Test]
	public async Task TokensCssDeclaresPhosphorsToneAndAnsiColours()
	{
		var tokens = File.ReadAllText(Path.Join(ClientSource.CssRoot, "tokens.css"));
		var phosphor = ThemeResolver.Resolve(BuiltInThemes.Phosphor);
		foreach (var name in Enumerable.Range(1, 6).Concat(Enumerable.Range(9, 6)).SelectMany(slot => new[] { $"ms-ansi-{slot}", $"ms-ansi-bg-{slot}" })
			.Append("tone-tertiary").Append("highlight"))
		{
			await Assert.That(tokens).Contains($"--{name}: {phosphor.Token(name)};").Because(name);
		}
	}

	// A hex colour, or rgb()/rgba(), of anything but pure black or white.
	[GeneratedRegex(@"#(?!(?:000|fff|000000|ffffff)\b)[0-9a-fA-F]{3,8}\b|rgba?\(\s*(?!0\s*,\s*0\s*,\s*0\s*[,)]|255\s*,\s*255\s*,\s*255\s*[,)]|var\()[^)]*\)")]
	private static partial Regex Literal();

	[GeneratedRegex(@"var\(--[\w-]+\s*,\s*(var\(--[\w-]+\s*,\s*)?$")]
	private static partial Regex Fallback();

	[GeneratedRegex(@"/\*.*?(\*/|$)|^\s*\*.*$")]
	private static partial Regex Comment();
}
