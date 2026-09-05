using MarkupString.MarkupImplementation;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// <c>hyperlink()</c> makes a link the way the helpfiles do — as markup, not as text.
///
/// <para>The two existing routes both put the burden in the wrong place. <c>tagwrap()</c> writes
/// Pueblo/MXP tags inline, so the sender has to know what the reader's client can parse and every
/// other client is shown a literal <c>&lt;a href=…&gt;</c>. The web portal parses no such tags at
/// all: it receives markup and renders it, which is why the links in <c>help</c> work there. Nothing
/// let softcode reach that mechanism.</para>
///
/// <para>A markup link needs no capability check, because the decision moves to where the client is
/// actually known: <see cref="AnsiMarkup.WrapAsHtmlClass"/> writes an anchor, the Pueblo renderer
/// writes Pueblo, and a plain terminal gets the text.</para>
/// </summary>
public class HyperlinkFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Hyperlink_RendersAnAnchorInHtml()
	{
		var result = (await Parser.FunctionParse(
			MModule.single("hyperlink(the scene,https://example.test/scenes/1)")))!.Message!;

		var html = result.Render("html");

		await Assert.That(html).Contains("href=\"https://example.test/scenes/1\"")
			.Because("the portal renders markup, so a link must arrive as markup to become an anchor");
		await Assert.That(html).Contains("the scene");
	}

	/// <summary>The text is what a client without links shows; the address is not smuggled into it.</summary>
	[Test]
	public async Task Hyperlink_LeavesThePlainTextAlone()
	{
		var result = (await Parser.FunctionParse(
			MModule.single("hyperlink(the scene,https://example.test/scenes/1)")))!.Message!;

		await Assert.That(MModule.plainText(result)).IsEqualTo("the scene");
	}
}
