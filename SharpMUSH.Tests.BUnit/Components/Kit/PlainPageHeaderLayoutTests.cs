using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>
/// The plain header (§4.9) puts the actions on the title's baseline while there is room and drops
/// them under the title when there is not, without a breakpoint: the text takes a flex basis and the
/// head wraps. Pages with two or more actions used to patch this in their own narrow tier with
/// <c>::deep .kit-page-head { flex-wrap: wrap; }</c>; the rule belongs to the component.
/// </summary>
public class PlainPageHeaderLayoutTests
{
	private static string Css => File.ReadAllText(Path.Join(ClientSource.RazorRoot, "Components/Kit/PlainPageHeader.razor.css"));

	private static string Rule(string selector) =>
		Regex.Match(Css, Regex.Escape(selector) + @"\s*\{(?<body>[^}]*)\}").Groups["body"].Value;

	[Test]
	public async Task TheHeadWraps_AndTheTextTakesABasis()
	{
		await Assert.That(Rule(".kit-page-head")).Contains("flex-wrap: wrap");
		await Assert.That(Rule(".kit-page-actions")).Contains("flex-wrap: wrap");
		await Assert.That(Regex.IsMatch(Rule(".kit-page-text"), @"flex:\s*1 1 \d+(\.\d+)?rem")).IsTrue()
			.Because("an auto basis never wraps: the text would shrink to nothing first");
	}

	[Test]
	public async Task NoPagePatchesTheHeaderItself()
	{
		var files = Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor.css", SearchOption.AllDirectories).ToList();
		var patched = files
			.Where(f => !f.EndsWith("PlainPageHeader.razor.css", StringComparison.Ordinal))
			.Where(f => Regex.IsMatch(File.ReadAllText(f), @"::deep\s+\.kit-page-(head|actions)\s*\{[^}]*flex-wrap"))
			.Select(f => Path.GetRelativePath(ClientSource.RazorRoot, f))
			.ToList();
		await Assert.That(files.Count).IsGreaterThan(50).Because("the client's stylesheets are copied beside the tests");
		await Assert.That(patched).IsEmpty();
	}
}
