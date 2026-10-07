using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Tools.ClientData;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// The portal's <c>data/markup.css</c> is the packages' own stylesheet, written at build by tools/ClientData,
/// so the classes the HTML emitters write are styled by the version of the package that wrote them.
/// </summary>
public class MarkupStylesheetTests
{
	[Test]
	public async Task MarkupCss_IsThePackagesStylesheets()
	{
		var stylesheet = ClientDataGenerator.MarkupStylesheet;

		await Assert.That(stylesheet).Contains(AnsiCss.Fixed);
		await Assert.That(stylesheet).Contains(LayoutCss.Fixed);
	}
}
