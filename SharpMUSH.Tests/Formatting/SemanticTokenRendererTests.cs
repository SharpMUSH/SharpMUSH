using MarkupString.MarkupImplementation;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using Range = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.Tests.Formatting;

public class SemanticTokenRendererTests
{
	private static SemanticToken Tok(int start, string text, SemanticTokenType type) => new()
	{
		Range = new Range { Start = new Position(0, start), End = new Position(0, start + text.Length) },
		TokenType = type,
		Text = text
	};

	[Test]
	public async Task NoTokens_ReturnsSourceUnchanged()
	{
		var result = SemanticTokenRenderer.Render(MModule.single("add(1,2)"), []);
		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task PlainTextIsPreserved_WhenStylesApply()
	{
		var src = MModule.single("add(1,2)");
		var result = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function), Tok(4, "1", SemanticTokenType.Number)]);

		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task StylesAreActuallyApplied()
	{
		var src = MModule.single("add(1,2)");
		var styled = SemanticTokenRenderer.Render(src, [Tok(0, "add(", SemanticTokenType.Function)]);

		await Assert.That(MModule.render("ansi", styled)).IsNotEqualTo(MModule.render("ansi", src));
	}

	[Test]
	public async Task OverrideTakesPrecedenceOverPalette()
	{
		var src = MModule.single("add(1,2)");
		var red = AnsiCodeParser.ParseCodes("r");
		var withOverride = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function)], offset => offset < 4 ? red : null);
		var withoutOverride = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function)]);

		await Assert.That(MModule.render("ansi", withOverride)).IsNotEqualTo(MModule.render("ansi", withoutOverride));
		await Assert.That(MModule.plainText(withOverride)).IsEqualTo("add(1,2)");
	}
}
