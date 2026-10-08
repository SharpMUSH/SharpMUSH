using System.Text.RegularExpressions;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// A theme names its parts and <c>css/themes/</c> draws them: one rule per choice, keyed on a data attribute, with
/// drawings under <c>themes/drawings/</c> used as masks the theme's colours fill.
/// </summary>
public partial class ThemePartsCssTests
{
	// Without comments, which name drawings and attributes by way of example.
	private static string PartsCss() => Comment().Replace(
		string.Join("\n", Directory.EnumerateFiles(ClientSource.ThemePartsRoot, "*.css").Select(File.ReadAllText)), "");

	private static IEnumerable<string> Drawings() => Directory.EnumerateFiles(ClientSource.DrawingsRoot, "*.svg", SearchOption.AllDirectories);

	private static string DrawingPath(string file) => Path.GetRelativePath(ClientSource.DrawingsRoot, file).Replace('\\', '/');

	[Test]
	public async Task EveryChoiceHasARule()
	{
		var css = PartsCss();
		// A Simple frame, ornament or effect is only a set of parts; the parts have the rules.
		string[] bundles = [ThemeStyles.Frame, ThemeStyles.Ornament, ThemeStyles.Effect, ThemeStyles.Mode];
		var missing = ThemeStyles.Choices
			.Where(c => !bundles.Contains(c.Key))
			.SelectMany(c => c.Value.Select(choice => $"[data-{c.Key}=\"{choice}\"]"))
			.Where(selector => !css.Contains(selector, StringComparison.Ordinal))
			.ToList();

		await Assert.That(missing).IsEmpty();
	}

	[Test]
	public async Task EveryRuleMatchesAChoice()
	{
		var unknown = PartSelector().Matches(PartsCss())
			.Select(m => (Key: m.Groups["key"].Value, Value: m.Groups["value"].Value))
			.Where(p => p.Key != ThemeStyles.Scheme && !ThemeStyles.IsValid(p.Key, p.Value))
			.Select(p => $"{p.Key}={p.Value}")
			.Distinct()
			.ToList();

		await Assert.That(unknown).IsEmpty();
	}

	[Test]
	public async Task EveryDrawingARuleNamesExists()
	{
		var files = Drawings().Select(DrawingPath).ToHashSet();
		var missing = DrawingUrl().Matches(PartsCss()).Select(m => m.Groups["path"].Value).Where(p => !files.Contains(p)).Distinct().ToList();

		await Assert.That(missing).IsEmpty();
	}

	[Test]
	public async Task EveryDrawingIsUsed()
	{
		var named = DrawingUrl().Matches(PartsCss()).Select(m => m.Groups["path"].Value).ToHashSet();
		var unused = Drawings().Select(DrawingPath).Where(p => !named.Contains(p)).ToList();

		await Assert.That(unused).IsEmpty();
	}

	[Test]
	public async Task DrawingsAreBlackOnClear()
	{
		var offenders = new List<string>();
		foreach (var file in Drawings())
		{
			var svg = File.ReadAllText(file);
			// White only cuts a shape out through the drawing's own <mask>, which paints nothing.
			var cuts = svg.Contains("<mask", StringComparison.Ordinal);
			offenders.AddRange(Paint().Matches(svg)
				.Select(m => m.Groups["value"].Value.ToLowerInvariant())
				.Where(v => v is not ("#000" or "#000000" or "black" or "none") && !v.StartsWith("url(", StringComparison.Ordinal)
					&& !(cuts && v is "white" or "#fff" or "#ffffff"))
				.Select(v => $"{DrawingPath(file)}: {v}"));
		}

		await Assert.That(offenders).IsEmpty().Because("a drawing is a mask: the theme's colour fills it, so it carries none of its own");
	}

	[Test]
	public async Task PartsCssLoadsNothingButDrawings()
	{
		var css = PartsCss();

		await Assert.That(css).DoesNotContain("data:");
		await Assert.That(css).DoesNotContain("@import");
		await Assert.That(Regex.Matches(css, @"url\(").Count).IsEqualTo(DrawingUrl().Matches(css).Count);
	}

	[Test]
	public async Task EveryBuiltInThemesPartsHaveRulesTheStarterCanList()
	{
		var css = PartsCss();
		foreach (var theme in BuiltInThemes.All.Skip(2))
		{
			var rules = ThemeStylesheet.PartRules(css, ThemeResolver.Resolve(theme).Parts);
			await Assert.That(rules.Any(r => r.Contains("--texture", StringComparison.Ordinal))).IsTrue().Because(theme.Id);
			await Assert.That(rules.Any(r => r.Contains("--font-display", StringComparison.Ordinal))).IsTrue().Because(theme.Id);
		}
	}

	[GeneratedRegex(@"\[data-(?<key>[a-z0-9-]+)=""(?<value>[^""]*)""\]")]
	private static partial Regex PartSelector();

	[GeneratedRegex(@"url\(\s*['""]?/themes/drawings/(?<path>[^'"")\s]+)['""]?\s*\)")]
	private static partial Regex DrawingUrl();

	[GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
	private static partial Regex Comment();

	[GeneratedRegex(@"(?:fill|stroke|stop-color|flood-color|color)\s*[=:]\s*['""]?(?<value>url\([^)]*\)|[#\w]+)")]
	private static partial Regex Paint();
}
