using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// The portal draws its Material icons in one style, Outlined. A Filled icon is only the "on" half of a pair whose
/// "off" half is Outlined, written on the same line: a pinned pin, a category that has its page, a step that is done.
/// </summary>
public partial class IconStyleTests
{
	[Test]
	public async Task EveryMaterialIconIsOutlinedUnlessItMarksAState()
	{
		var offenders = new List<string>();
		foreach (var file in Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor", SearchOption.AllDirectories))
		{
			var lines = File.ReadAllLines(file);
			for (var i = 0; i < lines.Length; i++)
			{
				foreach (Match filled in FilledIcon().Matches(lines[i]))
				{
					if (!lines[i].Contains("Icons.Material.Outlined.", StringComparison.Ordinal))
					{
						offenders.Add($"{Path.GetFileName(file)}:{i + 1} {filled.Value}");
					}
				}
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because("icons are Outlined; Filled is only the on state of an icon whose off state is Outlined");
	}

	[GeneratedRegex(@"Icons\.Material\.Filled\.[A-Za-z0-9]+")]
	private static partial Regex FilledIcon();
}
