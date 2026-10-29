using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Every transition and animation in the portal times itself with the motion tokens in <c>tokens.css</c>, so the
/// portal moves one way throughout and reduced motion (globals.css zeroes the spatial durations) reaches all of it.
/// </summary>
public partial class MotionTokenTests
{
	private static IEnumerable<string> PortalCss() =>
		Directory.EnumerateFiles(ClientSource.RazorRoot, "*.css", SearchOption.AllDirectories)
			.Concat(Directory.EnumerateFiles(ClientSource.CssRoot, "*.css", SearchOption.AllDirectories));

	[Test]
	public async Task NoStylesheetTimesAMotionOfItsOwn()
	{
		var offenders = new List<string>();
		foreach (var file in PortalCss())
		{
			var css = Comment().Replace(File.ReadAllText(file), "");
			foreach (Match m in Declaration().Matches(css))
			{
				var value = m.Groups["value"].Value;
				// A loop plays at its own pace; it is not a change from one state to another.
				if (value.Contains("infinite", StringComparison.Ordinal))
				{
					continue;
				}

				var bare = Variable().Replace(value, "");
				if (Literal().Match(bare) is { Success: true } literal)
				{
					offenders.Add($"{Path.GetFileName(file)}: {m.Value.Trim()} ({literal.Value})");
				}
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because("a duration or curve belongs in tokens.css; use a --motion-* pair (docs/guides/portal-themes.md)");
	}

	[GeneratedRegex(@"(?<![\w-])(?:transition|animation)(?:-duration|-timing-function|-delay)?\s*:\s*(?<value>[^;}]+)")]
	private static partial Regex Declaration();

	[GeneratedRegex(@"var\([^()]*\)")]
	private static partial Regex Variable();

	// Any time but zero, any easing keyword, any curve.
	[GeneratedRegex(@"(?<![\w.-])(?:0*[1-9]\d*(?:\.\d+)?|0*\.\d*[1-9]\d*)m?s\b|\b(?:ease(?:-in|-out|-in-out)?|step-start|step-end)\b|cubic-bezier\(|linear\(|steps\(")]
	private static partial Regex Literal();

	[GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
	private static partial Regex Comment();
}
