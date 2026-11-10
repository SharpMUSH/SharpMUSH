using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Library.Models.Portal;
using ThemeColor = SharpMUSH.Library.Models.Portal.ThemeColor;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class KitHelperTests
{
	[Test]
	public async Task NameColor_FitsBothDesktopAndMobilePoseSurfaces()
	{
		var color = ThemeColor.Parse(NameColor.Readable("#5aa9ff", "#ffffff", "#eeeeee")!);
		await Assert.That(ThemeColor.Contrast(color, ThemeColor.Parse("#ffffff"))).IsGreaterThanOrEqualTo(4.5);
		await Assert.That(ThemeColor.Contrast(color, ThemeColor.Parse("#eeeeee"))).IsGreaterThanOrEqualTo(4.5);
	}

	[Test]
	[Arguments("#5aa9ff", "#ebe7fb")]
	[Arguments("#ffb454", "#ffffff")]
	[Arguments("#222222", "#16181b")]
	[Arguments("#00aa88", "#887766")]
	public async Task NameColor_FitsTheActualSurface(string color, string background)
	{
		var readable = NameColor.Readable(color, background)!;
		await Assert.That(ThemeColor.Contrast(ThemeColor.Parse(readable), ThemeColor.Parse(background))).IsGreaterThanOrEqualTo(4.5);
	}

	[Test]
	public async Task NameColor_KeepsAnAlreadyReadableHue()
		=> await Assert.That(NameColor.Readable("#5aa9ff", "#16181b")).IsEqualTo("#5aa9ff");

	[Test]
	[Arguments("/api/wiki-assets/abc/x.jpg", true)]
	[Arguments("https://cdn.example/x.jpg", true)]
	[Arguments("http://cdn.example/x.jpg", false)]
	[Arguments("//cdn.example/x.jpg", false)]
	[Arguments("javascript:alert(1)", false)]
	[Arguments("data:image/png;base64,AAAA", false)]
	[Arguments("", false)]
	[Arguments(null, false)]
	[Arguments("  /x.jpg", true)]
	[Arguments("/\\evil.com/x.png", false)]
	[Arguments("/\t\\evil.com/x.png", false)]
	[Arguments("https://cdn.example/a\\b.jpg", false)]
	[Arguments("/a\nb.jpg", false)]
	[Arguments("HTTPS://cdn.example/x.jpg", true)]
	public async Task ImageUrlPolicy_AcceptsOnlySiteRelativeOrHttps(string? url, bool expected)
		=> await Assert.That(ImageUrlPolicy.IsRenderable(url)).IsEqualTo(expected);

	[Test]
	public async Task NameHue_IsStableAndInRange()
	{
		var a = NameHue.Of("Tomas Reyes");
		await Assert.That(a).IsEqualTo(NameHue.Of("Tomas Reyes"));
		await Assert.That(a).IsGreaterThanOrEqualTo(0);
		await Assert.That(a).IsLessThan(360);
		await Assert.That(NameHue.Of(null)).IsGreaterThanOrEqualTo(0);
	}

	[Test]
	public async Task NameHue_DiffersBetweenNames()
		=> await Assert.That(NameHue.Of("Tomas Reyes")).IsNotEqualTo(NameHue.Of("Wren Halloway"));

	[Test]
	[Arguments("Tomas Reyes", "TR")]
	[Arguments("Wren", "W")]
	[Arguments("  dace   kellan  ", "DK")]
	[Arguments("", "?")]
	[Arguments(null, "?")]
	[Arguments("\U0001F98A Fox", "\U0001F98AF")]
	[Arguments("Tomas\u00A0Reyes", "TR")]
	[Arguments("张伟", "张")]
	[Arguments("élodie varn", "ÉV")]
	public async Task Initials_TakeTheFirstTwoWords(string? name, string expected)
		=> await Assert.That(Initials.From(name)).IsEqualTo(expected);

	[Test]
	[Arguments("#5aa9ff", "#5aa9ff")]
	[Arguments("#ABCDEF", "#ABCDEF")]
	[Arguments("#fff", null)]
	[Arguments("#5aa9ff;x:y", null)]
	[Arguments("red", null)]
	[Arguments("#gggggg", null)]
	[Arguments("", null)]
	[Arguments(null, null)]
	public async Task NameColor_AcceptsOnlySixDigitHex(string? color, string? expected)
		=> await Assert.That(NameColor.Safe(color)).IsEqualTo(expected);
}
